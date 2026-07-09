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
        private GraphServiceClient _graphClient = null!;

        private string _defaultTodoListId = "";

        private readonly string[] _scopes = new[]
        {
            "Calendars.ReadWrite",
            "Tasks.ReadWrite",
            "Mail.ReadWrite",
            "Mail.Send",
            "User.Read"
        };

        public GraphServiceClient? GraphClient => _graphClient;

        public Task ClearLocalAuthorizationAsync()
        {
            _graphClient = null!;
            _defaultTodoListId = "";

            try
            {
                var authRecordPath = GetAuthRecordPath();
                if (File.Exists(authRecordPath))
                    File.Delete(authRecordPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Microsoft auth record cleanup failed: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        public async Task EnsureAuthorizedAsync()
        {
            if (_graphClient != null) return;

            string authRecordPath = GetAuthRecordPath();

            AuthenticationRecord? authRecord = null;

            try
            {
                var encrypted = ProtectedLocalStore.ReadText(authRecordPath);
                if (!string.IsNullOrEmpty(encrypted))
                {
                    var bytes = Convert.FromBase64String(encrypted);
                    using var stream = new MemoryStream(bytes);
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
                    await SaveAuthRecordAsync(authRecordPath, authRecord);
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
                await SaveAuthRecordAsync(authRecordPath, authRecord);
            }

            _graphClient = new GraphServiceClient(credential, _scopes);
        }

        private static string GetAuthRecordPath()
        {
            string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskFlyout");
            Directory.CreateDirectory(appDataPath);
            return Path.Combine(appDataPath, "ms_auth_record.bin");
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
                            Id = cal.Id ?? "",
                            Name = cal.Name ?? cal.Id ?? "",
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

        private async Task<string?> GetDefaultTodoListIdAsync()
        {
            if (!string.IsNullOrEmpty(_defaultTodoListId)) return _defaultTodoListId;

            try
            {
                var lists = await _graphClient.Me.Todo.Lists.GetAsync();
                _defaultTodoListId = lists?.Value?.FirstOrDefault()?.Id ?? "";
                System.Diagnostics.Debug.WriteLine($"[Microsoft To Do] Got default list ID: {_defaultTodoListId}");
                return _defaultTodoListId;
            }
            catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx)
            {
                System.Diagnostics.Debug.WriteLine($"[Microsoft To Do] OData error fetching lists!");
                System.Diagnostics.Debug.WriteLine($"Error code: {odataEx.Error?.Code}");
                System.Diagnostics.Debug.WriteLine($"Details: {odataEx.Error?.Message}");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Microsoft To Do] Other error fetching lists: {ex.Message}");
                return null;
            }
        }

        public async Task<List<AgendaItem>> FetchDataAsync(DateTime startDate, DateTime endDate)
        {
            await EnsureAuthorizedAsync();

            // Fetch calendars to get names for CalendarId/CalendarName
            var calendarMap = new Dictionary<string, string>();
            try
            {
                var calendars = await _graphClient.Me.Calendars.GetAsync();
                if (calendars?.Value != null)
                {
                    foreach (var cal in calendars.Value)
                    {
                        if (!string.IsNullOrEmpty(cal.Id))
                            calendarMap[cal.Id] = cal.Name ?? cal.Id;
                    }
                }
            }
            catch { }

            // Fetch events and tasks in parallel
            var eventsTask = FetchCalendarEventsAsync(startDate, endDate, calendarMap);
            var tasksTask = FetchMicrosoftTasksAsync(startDate, endDate);

            await Task.WhenAll(eventsTask, tasksTask);

            var results = new List<AgendaItem>();
            results.AddRange(await eventsTask);
            results.AddRange(await tasksTask);
            return results;
        }

        private async Task<List<AgendaItem>> FetchCalendarEventsAsync(DateTime startDate, DateTime endDate, Dictionary<string, string> calendarMap)
        {
            var results = new List<AgendaItem>();
            try
            {
                var events = await _graphClient.Me.CalendarView.GetAsync(req =>
                {
                    req.QueryParameters.StartDateTime = startDate.ToString("o");
                    req.QueryParameters.EndDateTime = endDate.ToString("o");
                    req.QueryParameters.Top = 500;
                    req.QueryParameters.Select = new[] { "id", "subject", "start", "end", "isAllDay", "location", "bodyPreview", "calendar", "recurrence", "seriesMasterId", "type" };
                });

                var allEvents = new List<Event>();
                if (events != null)
                {
                    var pageIterator = PageIterator<Event, EventCollectionResponse>.CreatePageIterator(
                        _graphClient,
                        events,
                        ev =>
                        {
                            allEvents.Add(ev);
                            return true;
                        });
                    await pageIterator.IterateAsync();
                }

                if (allEvents.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[Microsoft Calendar] Fetched {allEvents.Count} events");
                    var recurrenceKinds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var ev in allEvents)
                    {
                        DateTime.TryParse(ev.Start?.DateTime, out var start);

                        string? calId = null;
                        string? calName = null;
                        if (ev.AdditionalData != null && ev.AdditionalData.TryGetValue("calendar@odata.associationLink", out var calLink))
                        {
                            calId = calLink?.ToString();
                        }
                        if (calId == null && calendarMap.Count > 0)
                        {
                            var first = calendarMap.First();
                            calId = first.Key;
                            calName = first.Value;
                        }
                        if (calId != null && calName == null)
                            calendarMap.TryGetValue(calId, out calName);

                        DateTime.TryParse(ev.End?.DateTime, out var end);
                        string recurringEventId = ev.SeriesMasterId ?? "";
                        string recurrenceKind = GetMicrosoftRecurrenceKind(ev.Recurrence);
                        if (recurrenceKind == "None" && !string.IsNullOrWhiteSpace(recurringEventId))
                            recurrenceKind = await GetMicrosoftMasterRecurrenceKindAsync(recurringEventId, recurrenceKinds);

                        results.Add(new AgendaItem
                        {
                            Id = ev.Id ?? "",
                            Title = ev.Subject ?? "",
                            Subtitle = ev.IsAllDay == true ? (_loader.GetStringOrDefault("TextAllDay") ?? "All Day") : start.ToString("HH:mm"),
                            Location = ev.Location?.DisplayName ?? "",
                            Description = ev.BodyPreview ?? "",
                            IsEvent = true,
                            IsTask = false,
                            Provider = ProviderName,
                            CalendarId = calId ?? "",
                            CalendarName = calName ?? "",
                            DateKey = start.ToString("yyyy-MM-dd"),
                            StartDateTime = start,
                            EndDateTime = end,
                            IsRecurring = ev.Recurrence != null || !string.IsNullOrWhiteSpace(recurringEventId),
                            RecurringEventId = recurringEventId,
                            RecurrenceKind = recurrenceKind
                        });
                    }
                }
            }
            catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx)
            {
                System.Diagnostics.Debug.WriteLine($"[Microsoft Calendar] OData error: Code={odataEx.Error?.Code}, Message={odataEx.Error?.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Microsoft Calendar] Other error: {ex.Message}");
            }
            return results;
        }

        private async Task<List<AgendaItem>> FetchMicrosoftTasksAsync(DateTime startDate, DateTime endDate)
        {
            var results = new List<AgendaItem>();
            startDate = startDate.Date;
            endDate = endDate.Date;
            try
            {
                var lists = await _graphClient.Me.Todo.Lists.GetAsync(req =>
                {
                    req.QueryParameters.Top = 100;
                });

                var allLists = new List<TodoTaskList>();
                if (lists != null)
                {
                    var listIterator = PageIterator<TodoTaskList, TodoTaskListCollectionResponse>.CreatePageIterator(
                        _graphClient,
                        lists,
                        list =>
                        {
                            allLists.Add(list);
                            return true;
                        });
                    await listIterator.IterateAsync();
                }

                foreach (var list in allLists.Where(list => !string.IsNullOrWhiteSpace(list.Id)))
                {
                    var tasks = await _graphClient.Me.Todo.Lists[list.Id].Tasks.GetAsync(req =>
                    {
                        req.QueryParameters.Top = 500;
                    });

                    var allTasks = new List<TodoTask>();
                    if (tasks != null)
                    {
                        var pageIterator = PageIterator<TodoTask, TodoTaskCollectionResponse>.CreatePageIterator(
                            _graphClient,
                            tasks,
                            task =>
                            {
                                allTasks.Add(task);
                                return true;
                            });
                        await pageIterator.IterateAsync();
                    }

                    if (allTasks.Count > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Microsoft To Do] Fetched {allTasks.Count} tasks. Parsing...");

                        foreach (var task in allTasks)
                        {
                            bool isCompleted = task.Status == Microsoft.Graph.Models.TaskStatus.Completed;
                            DateTime targetDate = DateTime.Today;

                            if (task.DueDateTime != null)
                            {
                                DateTime.TryParse(task.DueDateTime.DateTime, out targetDate);
                                if (task.DueDateTime.TimeZone == "UTC" || task.DueDateTime.TimeZone == "Utc")
                                {
                                    targetDate = targetDate.ToLocalTime();
                                }
                            }
                            else if (isCompleted && task.CompletedDateTime != null)
                            {
                                DateTime.TryParse(task.CompletedDateTime.DateTime, out targetDate);
                                targetDate = targetDate.ToLocalTime();
                            }

                            if (!isCompleted && !IsInDateRange(targetDate, startDate, endDate))
                                continue;

                            results.Add(new AgendaItem
                            {
                                Id = task.Id ?? "",
                                Title = task.Title ?? "",
                                Subtitle = _loader.GetStringOrDefault("TextTask") ?? "Task",
                                Description = task.Body?.Content ?? "",
                                IsEvent = false,
                                IsTask = true,
                                IsCompleted = isCompleted,
                                Provider = ProviderName,
                                CalendarId = list.Id ?? "",
                                CalendarName = list.DisplayName ?? "",
                                DateKey = targetDate.ToString("yyyy-MM-dd")
                            });
                        }
                    }
                }
            }
            catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx)
            {
                System.Diagnostics.Debug.WriteLine($"[Microsoft To Do] OData error fetching tasks!");
                System.Diagnostics.Debug.WriteLine($"Code: {odataEx.Error?.Code}");
                System.Diagnostics.Debug.WriteLine($"Message: {odataEx.Error?.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Microsoft To Do] Other error fetching tasks: {ex.Message}");
            }
            return results;
        }

        private static bool IsInDateRange(DateTime date, DateTime startDate, DateTime endDate)
        {
            var day = date.Date;
            return day >= startDate && day < endDate;
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

        public async Task CreateEventAsync(string title, DateTime targetDate, TimeSpan startTime, TimeSpan endTime, string location, bool isAllDay, EventRecurrenceKind recurrence = EventRecurrenceKind.None)
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

            if (recurrence != EventRecurrenceKind.None)
                newEvent.Recurrence = CreateMicrosoftRecurrence(recurrence, targetDate);

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

        public async Task DeleteItemAsync(string itemId, bool isEvent, RecurringDeleteMode recurringDeleteMode = RecurringDeleteMode.Single, DateTime? occurrenceDate = null, string recurringEventId = "")
        {
            await EnsureAuthorizedAsync();

            if (isEvent)
            {
                if (recurringDeleteMode == RecurringDeleteMode.All && !string.IsNullOrWhiteSpace(recurringEventId))
                {
                    await _graphClient.Me.Events[recurringEventId].DeleteAsync();
                }
                else if (recurringDeleteMode == RecurringDeleteMode.ThisAndFollowing && !string.IsNullOrWhiteSpace(recurringEventId) && occurrenceDate.HasValue)
                {
                    var master = await _graphClient.Me.Events[recurringEventId].GetAsync(request =>
                    {
                        request.QueryParameters.Select = new[] { "id", "recurrence" };
                    });

                    if (master?.Recurrence?.Range != null)
                    {
                        master.Recurrence.Range.Type = RecurrenceRangeType.EndDate;
                        master.Recurrence.Range.EndDate = DateOnly.FromDateTime(occurrenceDate.Value.Date.AddDays(-1));
                        await _graphClient.Me.Events[recurringEventId].PatchAsync(new Event { Recurrence = master.Recurrence });
                    }
                }
                else
                {
                    await _graphClient.Me.Events[itemId].DeleteAsync();
                }
            }
            else
            {
                var listId = await GetDefaultTodoListIdAsync();
                await _graphClient.Me.Todo.Lists[listId].Tasks[itemId].DeleteAsync();
            }
        }

        private static PatternedRecurrence CreateMicrosoftRecurrence(EventRecurrenceKind recurrence, DateTime startDate)
        {
            return new PatternedRecurrence
            {
                Pattern = new RecurrencePattern
                {
                    Type = recurrence switch
                    {
                        EventRecurrenceKind.Daily => RecurrencePatternType.Daily,
                        EventRecurrenceKind.Weekly => RecurrencePatternType.Weekly,
                        EventRecurrenceKind.Monthly => RecurrencePatternType.AbsoluteMonthly,
                        EventRecurrenceKind.Yearly => RecurrencePatternType.AbsoluteYearly,
                        _ => RecurrencePatternType.Daily
                    },
                    Interval = 1
                },
                Range = new RecurrenceRange
                {
                    Type = RecurrenceRangeType.NoEnd,
                    StartDate = DateOnly.FromDateTime(startDate.Date)
                }
            };
        }

        private async Task<string> GetMicrosoftMasterRecurrenceKindAsync(string eventId, Dictionary<string, string> cache)
        {
            if (cache.TryGetValue(eventId, out var cached))
                return cached;

            try
            {
                var master = await _graphClient.Me.Events[eventId].GetAsync(request =>
                {
                    request.QueryParameters.Select = new[] { "id", "recurrence" };
                });
                cached = GetMicrosoftRecurrenceKind(master?.Recurrence);
            }
            catch
            {
                cached = "None";
            }

            cache[eventId] = cached;
            return cached;
        }

        private static string GetMicrosoftRecurrenceKind(PatternedRecurrence? recurrence)
        {
            return recurrence?.Pattern?.Type switch
            {
                RecurrencePatternType.Daily => "Daily",
                RecurrencePatternType.Weekly => "Weekly",
                RecurrencePatternType.AbsoluteMonthly or RecurrencePatternType.RelativeMonthly => "Monthly",
                RecurrencePatternType.AbsoluteYearly or RecurrencePatternType.RelativeYearly => "Yearly",
                _ => "None"
            };
        }

        private static async Task SaveAuthRecordAsync(string path, AuthenticationRecord record)
        {
            using var stream = new MemoryStream();
            await record.SerializeAsync(stream);
            var base64 = Convert.ToBase64String(stream.ToArray());
            ProtectedLocalStore.WriteText(path, base64);
        }
    }
}
