using Azure.Core;
using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Task_Flyout.Models;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Task_Flyout.Services
{
    public class MicrosoftSyncProvider : ISyncProvider
    {
        public string ProviderName => "Microsoft";
        private ResourceLoader _loader = new ResourceLoader();
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
            catch {}

            var options = new InteractiveBrowserCredentialOptions
            {
                TenantId = "common",
                ClientId = Secrets.MicrosoftClientId,
                AuthorityHost = AzureAuthorityHosts.AzurePublicCloud,
                RedirectUri = new Uri("http://localhost"),
                TokenCachePersistenceOptions = new TokenCachePersistenceOptions { Name = "TaskFlyout_MSAL_Cache" },
                AuthenticationRecord = authRecord
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

        public async Task<List<SubscribedCalendarInfo>> FetchCalendarListAsync()
        {
            var result = new List<SubscribedCalendarInfo>();
            if (_graphClient == null) return result;

            try
            {
                var calendars = await _graphClient.Me.Calendars.GetAsync();
                if (calendars?.Value != null)
                {
                    foreach (var cal in calendars.Value)
                    {
                        result.Add(new SubscribedCalendarInfo
                        {
                            Id = cal.Id,
                            Name = cal.Name ?? cal.Id,
                            IsVisible = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Microsoft] Failed to fetch calendar list: {ex.Message}");
            }

            return result;
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

            // Fetch calendars to get names for CalendarId/CalendarName
            var calendarMap = new Dictionary<string, string>();
            try
            {
                var calendars = await _graphClient.Me.Calendars.GetAsync();
                if (calendars?.Value != null)
                {
                    foreach (var cal in calendars.Value)
                        calendarMap[cal.Id] = cal.Name ?? cal.Id;
                }
            }
            catch { }

            try
            {
                var events = await _graphClient.Me.CalendarView.GetAsync(req =>
                {
                    req.QueryParameters.StartDateTime = startDate.ToString("o");
                    req.QueryParameters.EndDateTime = endDate.ToString("o");
                    req.QueryParameters.Top = 500;
                    req.QueryParameters.Select = new[] { "id", "subject", "start", "end", "isAllDay", "location", "bodyPreview", "calendar" };
                });

                if (events?.Value != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[Microsoft Calendar] 获取到 {events.Value.Count} 个日程");
                    foreach (var ev in events.Value)
                    {
                        DateTime.TryParse(ev.Start?.DateTime, out var start);

                        string calId = null;
                        string calName = null;
                        // Try to extract calendar info from the event
                        if (ev.AdditionalData != null && ev.AdditionalData.TryGetValue("calendar@odata.associationLink", out var calLink))
                        {
                            calId = calLink?.ToString();
                        }
                        // Use the first calendar as default if we can't determine
                        if (calId == null && calendarMap.Count > 0)
                        {
                            var first = calendarMap.First();
                            calId = first.Key;
                            calName = first.Value;
                        }
                        if (calId != null && calName == null)
                            calendarMap.TryGetValue(calId, out calName);

                        DateTime.TryParse(ev.End?.DateTime, out var end);
                        results.Add(new AgendaItem
                        {
                            Id = ev.Id,
                            Title = ev.Subject,
                            Subtitle = ev.IsAllDay == true ? _loader.GetString("TextAllDay") : start.ToString("HH:mm"),
                            Location = ev.Location?.DisplayName ?? "",
                            Description = ev.BodyPreview ?? "",
                            IsEvent = true,
                            IsTask = false,
                            Provider = ProviderName,
                            CalendarId = calId,
                            CalendarName = calName,
                            DateKey = start.ToString("yyyy-MM-dd"),
                            StartDateTime = start,
                            EndDateTime = end
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

            try
            {
                var listId = await GetDefaultTodoListIdAsync();
                if (!string.IsNullOrEmpty(listId))
                {
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
                                Subtitle = _loader.GetString("TextTask"),
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
