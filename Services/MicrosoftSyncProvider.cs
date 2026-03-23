using Azure.Core;
using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models; // 必须引入 Graph.Models 才能使用 Event, TodoTask 等模型
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Task_Flyout.Models;

namespace Task_Flyout.Services
{
    public class MicrosoftSyncProvider : ISyncProvider
    {
        public string ProviderName => "Microsoft";

        private GraphServiceClient _graphClient;

        private string _defaultTodoListId;

        private readonly string[] _scopes = new[] { "Calendars.ReadWrite", "Tasks.ReadWrite" };

        public async Task EnsureAuthorizedAsync()
        {
            if (_graphClient != null) return;

            string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskFlyout");
            Directory.CreateDirectory(appDataPath);
            string authRecordPath = Path.Combine(appDataPath, "ms_auth_record.bin");

            AuthenticationRecord authRecord = null;

            try
            {
                if (File.Exists(authRecordPath))
                {
                    using var stream = File.OpenRead(authRecordPath);
                    authRecord = await AuthenticationRecord.DeserializeAsync(stream);
                }
            }
            catch { /* 如果文件损坏则忽略，当作没登录过 */ }

            var options = new InteractiveBrowserCredentialOptions
            {
                TenantId = "common",
                ClientId = Secrets.MicrosoftClientId,
                AuthorityHost = AzureAuthorityHosts.AzurePublicCloud,
                RedirectUri = new Uri("http://localhost"),
                TokenCachePersistenceOptions = new TokenCachePersistenceOptions { Name = "TaskFlyout_MSAL_Cache" },
                AuthenticationRecord = authRecord // 👉 关键：把钥匙插进去
            };

            var credential = new InteractiveBrowserCredential(options);

            try
            {
                var tokenContext = new TokenRequestContext(_scopes);

                if (authRecord == null)
                {
                    authRecord = await credential.AuthenticateAsync(tokenContext);
                    using var stream = File.Create(authRecordPath);
                    await authRecord.SerializeAsync(stream);
                }
                else
                {
                    await credential.GetTokenAsync(tokenContext);
                }
            }
            catch (Exception)
            {
                if (File.Exists(authRecordPath)) File.Delete(authRecordPath);
                options.AuthenticationRecord = null;

                authRecord = await credential.AuthenticateAsync(new TokenRequestContext(_scopes));
                using var stream = File.Create(authRecordPath);
                await authRecord.SerializeAsync(stream);
            }

            _graphClient = new GraphServiceClient(credential, _scopes);
        }

        private async Task<string> GetDefaultTodoListIdAsync()
        {
            if (!string.IsNullOrEmpty(_defaultTodoListId)) return _defaultTodoListId;

            try
            {
                var lists = await _graphClient.Me.Todo.Lists.GetAsync();
                _defaultTodoListId = lists?.Value?.FirstOrDefault()?.Id;
                System.Diagnostics.Debug.WriteLine($"[Microsoft To Do] 成功获取默认列表 ID: {_defaultTodoListId}");
                return _defaultTodoListId;
            }
            catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx)
            {
                System.Diagnostics.Debug.WriteLine($"[Microsoft To Do] 获取列表 OData 错误!");
                System.Diagnostics.Debug.WriteLine($"错误代码: {odataEx.Error?.Code}");
                System.Diagnostics.Debug.WriteLine($"详细信息: {odataEx.Error?.Message}");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Microsoft To Do] 获取列表其他错误: {ex.Message}");
                return null;
            }
        }

        public async Task<List<AgendaItem>> FetchDataAsync(DateTime startDate, DateTime endDate)
        {
            await EnsureAuthorizedAsync();
            var results = new List<AgendaItem>();

            // A. 拉取日历
            try
            {
                var events = await _graphClient.Me.CalendarView.GetAsync(req =>
                {
                    req.QueryParameters.StartDateTime = startDate.ToString("o");
                    req.QueryParameters.EndDateTime = endDate.ToString("o");
                    req.QueryParameters.Top = 500;
                });

                if (events?.Value != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[Microsoft Calendar] 获取到 {events.Value.Count} 个日程");
                    foreach (var ev in events.Value)
                    {
                        DateTime.TryParse(ev.Start?.DateTime, out var start);
                        results.Add(new AgendaItem
                        {
                            Id = ev.Id,
                            Title = ev.Subject,
                            Subtitle = ev.IsAllDay == true ? "全天" : start.ToString("HH:mm"),
                            Location = ev.Location?.DisplayName ?? "",
                            Description = ev.BodyPreview ?? "",
                            IsEvent = true,
                            IsTask = false,
                            Provider = ProviderName,
                            DateKey = start.ToString("yyyy-MM-dd")
                        });
                    }
                }
            }
            catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx)
            {
                System.Diagnostics.Debug.WriteLine($"[Microsoft Calendar] 拉取日历 OData 错误! 代码: {odataEx.Error?.Code}, 信息: {odataEx.Error?.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Microsoft Calendar] 拉取日历其他错误: {ex.Message}");
            }

            // B. 拉取待办任务
            try
            {
                var listId = await GetDefaultTodoListIdAsync();
                if (!string.IsNullOrEmpty(listId))
                {
                    // 关键修复 1：扩大请求数量，突破默认 50 个的限制！
                    var tasks = await _graphClient.Me.Todo.Lists[listId].Tasks.GetAsync(req =>
                    {
                        req.QueryParameters.Top = 500;
                    });

                    if (tasks?.Value != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Microsoft To Do] 获取到 {tasks.Value.Count} 个待办任务。开始解析：");

                        foreach (var task in tasks.Value)
                        {
                            DateTime targetDate = DateTime.Today;

                            if (task.DueDateTime != null)
                            {
                                DateTime.TryParse(task.DueDateTime.DateTime, out targetDate);
                                // 关键修复 2：将微软返回的 UTC 时间转为本地时间，防止任务跑到“昨天”
                                if (task.DueDateTime.TimeZone == "UTC" || task.DueDateTime.TimeZone == "Utc")
                                {
                                    targetDate = targetDate.ToLocalTime();
                                }
                            }
                            else if (task.Status == Microsoft.Graph.Models.TaskStatus.Completed && task.CompletedDateTime != null)
                            {
                                DateTime.TryParse(task.CompletedDateTime.DateTime, out targetDate);
                                targetDate = targetDate.ToLocalTime();
                            }
                            else if (task.CreatedDateTime.HasValue)
                            {
                                targetDate = task.CreatedDateTime.Value.UtcDateTime.ToLocalTime();
                            }

                            results.Add(new AgendaItem
                            {
                                Id = task.Id,
                                Title = task.Title,
                                Subtitle = "任务",
                                Description = task.Body?.Content ?? "",
                                IsEvent = false,
                                IsTask = true,
                                IsCompleted = task.Status == Microsoft.Graph.Models.TaskStatus.Completed,
                                Provider = ProviderName,
                                DateKey = targetDate.ToString("yyyy-MM-dd")
                            });
                        }
                    }
                }
            }
            catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx)
            {
                System.Diagnostics.Debug.WriteLine($"[Microsoft To Do] 拉取任务 OData 错误!");
                System.Diagnostics.Debug.WriteLine($"代码: {odataEx.Error?.Code}");
                System.Diagnostics.Debug.WriteLine($"信息: {odataEx.Error?.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Microsoft To Do] 拉取任务其他错误: {ex.Message}");
            }

            return results;
        }

        // 3. 实现 UpdateItemAsync (应用内编辑事件/任务同步到云端)
        public async Task UpdateItemAsync(string itemId, bool isEvent, string title, string location, string description, DateTime targetDate, TimeSpan? startTime, TimeSpan? endTime)
        {
            await EnsureAuthorizedAsync();

            if (isEvent)
            {
                var updateEvent = new Event
                {
                    Subject = title,
                    Location = new Location { DisplayName = location },
                    Body = new ItemBody { Content = description, ContentType = BodyType.Text }
                };

                if (startTime.HasValue)
                {
                    DateTime exactStart = targetDate.Add(startTime.Value);
                    DateTime exactEnd = endTime.HasValue ? targetDate.Add(endTime.Value) : exactStart.AddHours(1);
                    if (exactEnd < exactStart) exactEnd = exactEnd.AddDays(1);

                    updateEvent.Start = new DateTimeTimeZone { DateTime = exactStart.ToString("s"), TimeZone = "UTC" };
                    updateEvent.End = new DateTimeTimeZone { DateTime = exactEnd.ToString("s"), TimeZone = "UTC" };
                    updateEvent.IsAllDay = false;
                }
                else
                {
                    updateEvent.Start = new DateTimeTimeZone { DateTime = targetDate.ToString("yyyy-MM-ddT00:00:00"), TimeZone = "UTC" };
                    updateEvent.End = new DateTimeTimeZone { DateTime = targetDate.AddDays(1).ToString("yyyy-MM-ddT00:00:00"), TimeZone = "UTC" };
                    updateEvent.IsAllDay = true;
                }

                await _graphClient.Me.Events[itemId].PatchAsync(updateEvent);
            }
            else
            {
                var listId = await GetDefaultTodoListIdAsync();
                var updateTask = new TodoTask
                {
                    Title = title,
                    Body = new ItemBody { Content = description, ContentType = BodyType.Text },
                    DueDateTime = new DateTimeTimeZone { DateTime = targetDate.ToString("yyyy-MM-ddT00:00:00"), TimeZone = "UTC" }
                };
                await _graphClient.Me.Todo.Lists[listId].Tasks[itemId].PatchAsync(updateTask);
            }
        }

        // 4. 实现 UpdateTaskStatusAsync (左侧勾选任务框)
        public async Task UpdateTaskStatusAsync(string taskId, bool isCompleted)
        {
            await EnsureAuthorizedAsync();
            var listId = await GetDefaultTodoListIdAsync();

            var updateTask = new TodoTask
            {
                Status = isCompleted ? Microsoft.Graph.Models.TaskStatus.Completed : Microsoft.Graph.Models.TaskStatus.NotStarted
            };

            await _graphClient.Me.Todo.Lists[listId].Tasks[taskId].PatchAsync(updateTask);
        }

        // 5. 实现 CreateEventAsync (新建日程)
        public async Task CreateEventAsync(string title, DateTime targetDate, TimeSpan startTime, TimeSpan endTime, string location, bool isAllDay)
        {
            await EnsureAuthorizedAsync();
            var newEvent = new Event
            {
                Subject = title,
                Location = string.IsNullOrWhiteSpace(location) ? null : new Location { DisplayName = location }
            };

            if (isAllDay)
            {
                newEvent.Start = new DateTimeTimeZone { DateTime = targetDate.ToString("yyyy-MM-ddT00:00:00"), TimeZone = "UTC" };
                newEvent.End = new DateTimeTimeZone { DateTime = targetDate.AddDays(1).ToString("yyyy-MM-ddT00:00:00"), TimeZone = "UTC" };
                newEvent.IsAllDay = true;
            }
            else
            {
                DateTime exactStart = targetDate.Add(startTime);
                DateTime exactEnd = targetDate.Add(endTime);
                newEvent.Start = new DateTimeTimeZone { DateTime = exactStart.ToString("s"), TimeZone = "UTC" };
                newEvent.End = new DateTimeTimeZone { DateTime = exactEnd.ToString("s"), TimeZone = "UTC" };
                newEvent.IsAllDay = false;
            }

            await _graphClient.Me.Events.PostAsync(newEvent);
        }

        // 6. 实现 CreateTaskAsync (新建任务)
        public async Task CreateTaskAsync(string title, DateTime targetDate, TimeSpan startTime, bool isAllDay)
        {
            await EnsureAuthorizedAsync();
            var listId = await GetDefaultTodoListIdAsync();

            var newTask = new TodoTask
            {
                Title = title,
                DueDateTime = new DateTimeTimeZone { DateTime = targetDate.ToString("yyyy-MM-ddT00:00:00"), TimeZone = "UTC" }
            };

            await _graphClient.Me.Todo.Lists[listId].Tasks.PostAsync(newTask);
        }

        // 7. 实现 DeleteItemAsync (在弹窗里点击红色的删除)
        public async Task DeleteItemAsync(string itemId, bool isEvent)
        {
            await EnsureAuthorizedAsync();

            if (isEvent)
            {
                await _graphClient.Me.Events[itemId].DeleteAsync();
            }
            else
            {
                var listId = await GetDefaultTodoListIdAsync();
                await _graphClient.Me.Todo.Lists[listId].Tasks[itemId].DeleteAsync();
            }
        }
    }
}