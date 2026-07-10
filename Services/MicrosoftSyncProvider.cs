using Azure.Core;
using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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

        private static readonly string[] AgendaScopes = new[]
        {
            "Calendars.ReadWrite",
            "Tasks.ReadWrite",
            "User.Read"
        };

        private static readonly string[] MailScopes = new[]
        {
            "Mail.Read"
        };

        private static readonly string[] MailWriteScopes = new[] { "Mail.ReadWrite" };
        private static readonly string[] MailSendScopes = new[] { "Mail.Send" };
        private string[] _graphClientScopes = Array.Empty<string>();

        public GraphServiceClient? GraphClient => _graphClient;

        public Task ClearLocalAuthorizationAsync()
        {
            _graphClient = null!;
            _graphClientScopes = Array.Empty<string>();
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

        public async Task EnsureAuthorizedAsync(CancellationToken cancellationToken = default)
        {
            if (_graphClient != null && HasScopes(_graphClientScopes, AgendaScopes)) return;

            var scopes = MergeScopes(AgendaScopes, _graphClientScopes);
            _graphClient = await CreateAuthorizedGraphClientAsync(scopes, cancellationToken);
            _graphClientScopes = scopes;
        }

        public async Task<GraphServiceClient> EnsureMailAuthorizedAsync(bool requireWrite = false, bool requireSend = false, CancellationToken cancellationToken = default)
        {
            var scopes = MergeScopes(BuildMailScopes(requireWrite, requireSend), _graphClientScopes);
            if (_graphClient == null || !HasScopes(_graphClientScopes, scopes))
            {
                _graphClient = await CreateAuthorizedGraphClientAsync(scopes, cancellationToken);
                _graphClientScopes = scopes;
            }

            return _graphClient;
        }

        private static string[] BuildMailScopes(bool requireWrite, bool requireSend)
        {
            var scopes = new List<string> { "User.Read" };
            scopes.AddRange(requireWrite ? MailWriteScopes : MailScopes);
            if (requireSend) scopes.AddRange(MailSendScopes);
            return scopes.ToArray();
        }

        private static bool HasScopes(IEnumerable<string> grantedScopes, IEnumerable<string> requiredScopes)
        {
            var granted = grantedScopes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return requiredScopes.All(scope =>
                granted.Contains(scope) ||
                (string.Equals(scope, "Mail.Read", StringComparison.OrdinalIgnoreCase) && granted.Contains("Mail.ReadWrite")));
        }

        private static string[] MergeScopes(params IEnumerable<string>[] scopeGroups)
        {
            return scopeGroups
                .SelectMany(scope => scope)
                .Where(scope => !string.IsNullOrWhiteSpace(scope))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private async Task<GraphServiceClient> CreateAuthorizedGraphClientAsync(string[] scopes, CancellationToken cancellationToken = default)
        {

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
                var tokenContext = new TokenRequestContext(scopes);

                if (authRecord == null)
                {
                    authRecord = await credential.AuthenticateAsync(tokenContext, cancellationToken);
                    await SaveAuthRecordAsync(authRecordPath, authRecord);
                }
                else
                {
                    await credential.GetTokenAsync(tokenContext, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                if (File.Exists(authRecordPath)) File.Delete(authRecordPath);
                options.AuthenticationRecord = null;

                authRecord = await credential.AuthenticateAsync(new TokenRequestContext(scopes), cancellationToken);
                await SaveAuthRecordAsync(authRecordPath, authRecord);
            }

            return new GraphServiceClient(credential, scopes);
        }

        private static string GetAuthRecordPath()
        {
            return ProviderAuthCleanup.MicrosoftAuthRecordPath;
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

        public async Task<List<AgendaItem>> FetchDataAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
        {
            await EnsureAuthorizedAsync(cancellationToken);

            // Fetch calendars to get names for CalendarId/CalendarName
            var calendarMap = new Dictionary<string, string>();
            try
            {
                var calendars = await _graphClient.Me.Calendars.GetAsync(cancellationToken: cancellationToken);
                if (calendars?.Value != null)
                {
                    foreach (var cal in calendars.Value)
                    {
                        if (!string.IsNullOrEmpty(cal.Id))
                            calendarMap[cal.Id] = cal.Name ?? cal.Id;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { }

            // Fetch events and tasks in parallel
            var eventsTask = FetchCalendarEventsAsync(startDate, endDate, calendarMap, cancellationToken);
            var tasksTask = FetchMicrosoftTasksAsync(startDate, endDate, cancellationToken);

            await Task.WhenAll(eventsTask, tasksTask);

            var results = new List<AgendaItem>();
            results.AddRange(await eventsTask);
            results.AddRange(await tasksTask);
            return results;
        }

        private async Task<List<AgendaItem>> FetchCalendarEventsAsync(DateTime startDate, DateTime endDate, Dictionary<string, string> calendarMap, CancellationToken cancellationToken)
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
                }, cancellationToken);

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
                    await pageIterator.IterateAsync(cancellationToken);
                }

                if (allEvents.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[Microsoft Calendar] Fetched {allEvents.Count} events");
                    var recurrenceKinds = await FetchMicrosoftMasterRecurrenceKindsAsync(allEvents, cancellationToken);
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
                        if (recurrenceKind == "None" &&
                            !string.IsNullOrWhiteSpace(recurringEventId) &&
                            recurrenceKinds.TryGetValue(recurringEventId, out var masterKind))
                            recurrenceKind = masterKind;

                        var mapped = SyncItemMappingPolicy.MapEvent(
                            ev.Id,
                            ev.Subject,
                            ev.Location?.DisplayName,
                            ev.BodyPreview,
                            ProviderName,
                            calId,
                            calName,
                            start,
                            start,
                            end,
                            ev.IsAllDay == true,
                            _loader.GetStringOrDefault("TextAllDay") ?? "All Day",
                            recurringEventId,
                            ev.Recurrence != null,
                            recurrenceKind);

                        results.Add(new AgendaItem
                        {
                            Id = mapped.Id,
                            Title = mapped.Title,
                            Subtitle = mapped.Subtitle,
                            Location = mapped.Location,
                            Description = mapped.Description,
                            IsEvent = true,
                            IsTask = false,
                            Provider = mapped.Provider,
                            CalendarId = mapped.CalendarId,
                            CalendarName = mapped.CalendarName,
                            DateKey = mapped.DateKey,
                            StartDateTime = mapped.StartDateTime,
                            EndDateTime = mapped.EndDateTime,
                            IsRecurring = mapped.IsRecurring,
                            RecurringEventId = mapped.RecurringEventId,
                            RecurrenceKind = mapped.RecurrenceKind
                        });
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx)
            {
                System.Diagnostics.Debug.WriteLine($"[Microsoft Calendar] OData error: Code={odataEx.Error?.Code}, Message={odataEx.Error?.Message}");
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Microsoft Calendar] Other error: {ex.Message}");
                throw;
            }
            return results;
        }

        private async Task<List<AgendaItem>> FetchMicrosoftTasksAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken)
        {
            var results = new List<AgendaItem>();
            var range = SyncRangePolicy.NormalizeHalfOpenDateRange(startDate, endDate);
            try
            {
                var lists = await _graphClient.Me.Todo.Lists.GetAsync(req =>
                {
                    req.QueryParameters.Top = 100;
                }, cancellationToken);

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
                    await listIterator.IterateAsync(cancellationToken);
                }

                using var listGate = new System.Threading.SemaphoreSlim(4);
                var listTasks = allLists
                    .Where(list => !string.IsNullOrWhiteSpace(list.Id))
                    .Select(async list =>
                    {
                        await listGate.WaitAsync(cancellationToken);
                        try
                        {
                            var tasks = await _graphClient.Me.Todo.Lists[list.Id].Tasks.GetAsync(req =>
                            {
                                req.QueryParameters.Top = 500;
                            }, cancellationToken);

                            var fetchedTasks = new List<TodoTask>();
                            if (tasks != null)
                            {
                                var pageIterator = PageIterator<TodoTask, TodoTaskCollectionResponse>.CreatePageIterator(
                                    _graphClient,
                                    tasks,
                                    task =>
                                    {
                                        fetchedTasks.Add(task);
                                        return true;
                                    });
                                await pageIterator.IterateAsync(cancellationToken);
                            }
                            return (List: list, Tasks: fetchedTasks);
                        }
                        finally
                        {
                            listGate.Release();
                        }
                    });

                foreach (var listResult in await Task.WhenAll(listTasks))
                {
                    var list = listResult.List;
                    var allTasks = listResult.Tasks;
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

                            if (!SyncRangePolicy.ShouldIncludeTask(targetDate, isCompleted, range.StartDate, range.EndDate))
                                continue;

                            var mapped = SyncItemMappingPolicy.MapTask(
                                task.Id,
                                task.Title,
                                _loader.GetStringOrDefault("TextTask") ?? "Task",
                                task.Body?.Content,
                                ProviderName,
                                list.Id,
                                list.DisplayName,
                                targetDate,
                                isCompleted);

                            results.Add(new AgendaItem
                            {
                                Id = mapped.Id,
                                Title = mapped.Title,
                                Subtitle = mapped.Subtitle,
                                Description = mapped.Description,
                                IsEvent = false,
                                IsTask = true,
                                IsCompleted = mapped.IsCompleted,
                                Provider = mapped.Provider,
                                CalendarId = mapped.CalendarId,
                                CalendarName = mapped.CalendarName,
                                DateKey = mapped.DateKey
                            });
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx)
            {
                System.Diagnostics.Debug.WriteLine($"[Microsoft To Do] OData error fetching tasks!");
                System.Diagnostics.Debug.WriteLine($"Code: {odataEx.Error?.Code}");
                System.Diagnostics.Debug.WriteLine($"Message: {odataEx.Error?.Message}");
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Microsoft To Do] Other error fetching tasks: {ex.Message}");
                throw;
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

                var window = SyncEventTimePolicy.Create(targetDate, startTime, endTime);
                updateEvent.Start = new DateTimeTimeZone { DateTime = window.Start.ToString("s"), TimeZone = "UTC" };
                updateEvent.End = new DateTimeTimeZone { DateTime = window.End.ToString("s"), TimeZone = "UTC" };
                updateEvent.IsAllDay = window.IsAllDay;

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

            var window = SyncEventTimePolicy.Create(targetDate, isAllDay ? null : startTime, endTime);
            newEvent.Start = new DateTimeTimeZone { DateTime = window.Start.ToString("s"), TimeZone = "UTC" };
            newEvent.End = new DateTimeTimeZone { DateTime = window.End.ToString("s"), TimeZone = "UTC" };
            newEvent.IsAllDay = window.IsAllDay;

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

        private async Task<Dictionary<string, string>> FetchMicrosoftMasterRecurrenceKindsAsync(IEnumerable<Event> events, CancellationToken cancellationToken)
        {
            var eventIds = events
                .Where(item => GetMicrosoftRecurrenceKind(item.Recurrence) == "None")
                .Select(item => item.SeriesMasterId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .ToList();
            if (eventIds.Count == 0)
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            using var gate = new System.Threading.SemaphoreSlim(4);
            var fetchTasks = eventIds.Select(async eventId =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    var master = await _graphClient.Me.Events[eventId].GetAsync(request =>
                    {
                        request.QueryParameters.Select = new[] { "id", "recurrence" };
                    }, cancellationToken);
                    return (EventId: eventId, Kind: GetMicrosoftRecurrenceKind(master?.Recurrence));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch
                {
                    return (EventId: eventId, Kind: "None");
                }
                finally
                {
                    gate.Release();
                }
            });

            return (await Task.WhenAll(fetchTasks))
                .ToDictionary(item => item.EventId, item => item.Kind, StringComparer.OrdinalIgnoreCase);
        }

        private static string GetMicrosoftRecurrenceKind(PatternedRecurrence? recurrence)
        {
            return RecurrencePolicy.ToDisplayKindFromMicrosoftPattern(recurrence?.Pattern?.Type?.ToString());
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
