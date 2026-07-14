using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Google.Apis.Tasks.v1;
using Google.Apis.Util.Store;
using Google.Apis.Auth.OAuth2.Responses;
using System.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Task_Flyout.Models;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Reflection;

namespace Task_Flyout.Services
{
    public class GoogleSyncProvider : ISyncProvider
    {
        public string ProviderName => "Google";
        public CalendarService? CalendarSvc { get; private set; }
        public TasksService? TasksSvc { get; private set; }
        public GmailService? GmailSvc { get; private set; }
        private ResourceLoader _loader = new ResourceLoader();
        private static readonly TimeSpan CalendarListCacheDuration = TimeSpan.FromMinutes(30);
        private readonly SemaphoreSlim _calendarListLock = new(1, 1);
        private List<SubscribedCalendarInfo>? _cachedCalendarList;
        private DateTimeOffset _calendarListCacheExpiresAt = DateTimeOffset.MinValue;
        private static readonly string[] CalendarTaskScopes =
        {
            CalendarService.Scope.Calendar,
            TasksService.Scope.Tasks
        };

        private static readonly string[] GmailScopes =
        {
            GmailService.Scope.GmailReadonly,
            GmailService.Scope.GmailSend,
            GmailService.Scope.GmailModify
        };

        private string[] _gmailGrantedScopes = Array.Empty<string>();

        public async Task ClearLocalAuthorizationAsync()
        {
            CalendarSvc = null;
            TasksSvc = null;
            GmailSvc = null;
            _gmailGrantedScopes = Array.Empty<string>();
            _cachedCalendarList = null;
            _calendarListCacheExpiresAt = DateTimeOffset.MinValue;

            await new ProtectedGoogleDataStore().ClearAsync();

            string legacyTokenPath = ProviderAuthCleanup.GoogleLegacyTokenPath;
            ProviderAuthCleanup.DeleteGoogleLegacyTokenStore(legacyTokenPath);
        }

        public async Task EnsureAuthorizedAsync(CancellationToken cancellationToken = default)
        {
            if (CalendarSvc != null && TasksSvc != null) return;

            string tokenPath = ProviderAuthCleanup.GoogleLegacyTokenPath;
            var dataStore = new ProtectedGoogleDataStore();
            await TryMigrateLegacyTokenStoreAsync(tokenPath, dataStore);
            var requiredScopes = MergeScopes(CalendarTaskScopes, _gmailGrantedScopes.Where(scope => GmailScopes.Contains(scope, StringComparer.OrdinalIgnoreCase)));
            UserCredential credential = await AuthorizeAsync(dataStore, requiredScopes, cancellationToken);

            var grantedScopes = credential.Token?.Scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            if (!HasScopes(grantedScopes, CalendarTaskScopes))
            {
                CalendarSvc = null;
                TasksSvc = null;
                GmailSvc = null;
                _gmailGrantedScopes = Array.Empty<string>();
                if (Directory.Exists(tokenPath)) Directory.Delete(tokenPath, true);
                await dataStore.ClearAsync();
                credential = await AuthorizeAsync(dataStore, requiredScopes, cancellationToken);
                grantedScopes = credential.Token?.Scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            }

            CalendarSvc = new CalendarService(new BaseClientService.Initializer() { HttpClientInitializer = credential, ApplicationName = "Task Flyout" });
            TasksSvc = new TasksService(new BaseClientService.Initializer() { HttpClientInitializer = credential, ApplicationName = "Task Flyout" });
            _gmailGrantedScopes = grantedScopes;
            GmailSvc = HasScopes(grantedScopes, new[] { GmailService.Scope.GmailReadonly })
                ? new GmailService(new BaseClientService.Initializer() { HttpClientInitializer = credential, ApplicationName = "Task Flyout" })
                : null;
        }

        public async Task<GmailService> EnsureGmailAuthorizedAsync(bool requireModify = false, bool requireSend = false, CancellationToken cancellationToken = default)
        {
            var requiredScopes = BuildGmailScopes(requireModify, requireSend);
            if (CalendarSvc != null && TasksSvc != null)
                requiredScopes = MergeScopes(requiredScopes, CalendarTaskScopes);
            requiredScopes = MergeScopes(requiredScopes, _gmailGrantedScopes.Where(scope => GmailScopes.Contains(scope, StringComparer.OrdinalIgnoreCase)));
            if (GmailSvc != null && HasScopes(_gmailGrantedScopes, requiredScopes)) return GmailSvc;

            string tokenPath = ProviderAuthCleanup.GoogleLegacyTokenPath;
            var dataStore = new ProtectedGoogleDataStore();
            await TryMigrateLegacyTokenStoreAsync(tokenPath, dataStore);
            UserCredential credential = await AuthorizeAsync(dataStore, requiredScopes, cancellationToken);

            var grantedScopes = credential.Token?.Scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            if (!HasScopes(grantedScopes, requiredScopes))
            {
                CalendarSvc = null;
                TasksSvc = null;
                GmailSvc = null;
                _gmailGrantedScopes = Array.Empty<string>();
                if (Directory.Exists(tokenPath)) Directory.Delete(tokenPath, true);
                await dataStore.ClearAsync();
                credential = await AuthorizeAsync(dataStore, requiredScopes, cancellationToken);
                grantedScopes = credential.Token?.Scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            }

            _gmailGrantedScopes = grantedScopes;
            if (HasScopes(grantedScopes, CalendarTaskScopes))
            {
                CalendarSvc = new CalendarService(new BaseClientService.Initializer() { HttpClientInitializer = credential, ApplicationName = "Task Flyout" });
                TasksSvc = new TasksService(new BaseClientService.Initializer() { HttpClientInitializer = credential, ApplicationName = "Task Flyout" });
            }

            GmailSvc = new GmailService(new BaseClientService.Initializer() { HttpClientInitializer = credential, ApplicationName = "Task Flyout" });
            return GmailSvc;
        }

        private static string[] BuildGmailScopes(bool requireModify, bool requireSend)
        {
            var scopes = new List<string> { GmailService.Scope.GmailReadonly };
            if (requireModify) scopes.Add(GmailService.Scope.GmailModify);
            if (requireSend) scopes.Add(GmailService.Scope.GmailSend);
            return scopes.ToArray();
        }

        private static string[] MergeScopes(params IEnumerable<string>[] scopeGroups)
        {
            return scopeGroups
                .SelectMany(scope => scope)
                .Where(scope => !string.IsNullOrWhiteSpace(scope))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private async Task<UserCredential> AuthorizeAsync(IDataStore dataStore, string[] scopes, CancellationToken cancellationToken = default)
        {
            try
            {
                using (var stream = OpenCredentialsStream())
                {
                    return await GoogleWebAuthorizationBroker.AuthorizeAsync(
                        GoogleClientSecrets.FromStream(stream).Secrets,
                        scopes,
                        "user",
                        cancellationToken,
                        dataStore);
                }
            }
            catch (FileNotFoundException)
            {
                throw new Exception(_loader.GetStringOrDefault("TextCredNotFound") ?? "Credential file not found.");
            }
        }

        private static bool HasScopes(IEnumerable<string> grantedScopes, IEnumerable<string> requiredScopes)
        {
            var granted = grantedScopes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return requiredScopes.All(scope => granted.Contains(scope));
        }

        private static async Task TryMigrateLegacyTokenStoreAsync(string tokenPath, IDataStore targetStore)
        {
            if (!Directory.Exists(tokenPath))
                return;

            try
            {
                if (await targetStore.GetAsync<TokenResponse>("user") != null)
                    return;

                var legacyStore = new FileDataStore(tokenPath, true);
                var legacyToken = await legacyStore.GetAsync<TokenResponse>("user");
                if (legacyToken == null)
                    return;

                await targetStore.StoreAsync("user", legacyToken);
                Directory.Delete(tokenPath, recursive: true);
            }
            catch
            {
                // A failed migration should fall back to the normal authorization flow.
            }
        }

        private Stream OpenCredentialsStream()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = Array.Find(
                assembly.GetManifestResourceNames(),
                name => name.EndsWith(".credentials.json", StringComparison.OrdinalIgnoreCase));

            Stream? stream = resourceName == null ? null : assembly.GetManifestResourceStream(resourceName);
            if (stream != null) return stream;

            string credPath = Path.Combine(AppContext.BaseDirectory, "credentials.json");
            if (File.Exists(credPath))
                return File.OpenRead(credPath);

            throw new FileNotFoundException("Google OAuth credentials were not found.", "credentials.json");
        }

        public async Task<List<SubscribedCalendarInfo>> FetchCalendarListAsync()
            => await FetchCalendarListAsync(CancellationToken.None);

        private async Task<List<SubscribedCalendarInfo>> FetchCalendarListAsync(CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            if (_cachedCalendarList != null && _calendarListCacheExpiresAt > now)
                return CloneCalendars(_cachedCalendarList);

            await _calendarListLock.WaitAsync(cancellationToken);
            try
            {
                now = DateTimeOffset.UtcNow;
                if (_cachedCalendarList != null && _calendarListCacheExpiresAt > now)
                    return CloneCalendars(_cachedCalendarList);

                var fetched = await FetchCalendarListFromApiAsync(cancellationToken);
                _cachedCalendarList = fetched;
                _calendarListCacheExpiresAt = now.Add(CalendarListCacheDuration);
                return CloneCalendars(fetched);
            }
            finally
            {
                _calendarListLock.Release();
            }
        }

        private async Task<List<SubscribedCalendarInfo>> FetchCalendarListFromApiAsync(CancellationToken cancellationToken)
        {
            var result = new List<SubscribedCalendarInfo>();
            if (CalendarSvc == null) return result;

            try
            {
                string? pageToken = null;
                var seenPageTokens = new HashSet<string>(StringComparer.Ordinal);
                do
                {
                    var listRequest = CalendarSvc.CalendarList.List();
                    listRequest.PageToken = pageToken;
                    var calendarList = await listRequest.ExecuteAsync(cancellationToken);
                    if (calendarList?.Items != null)
                    {
                        foreach (var cal in calendarList.Items)
                        {
                            result.Add(new SubscribedCalendarInfo
                            {
                                Id = cal.Id,
                                Name = cal.SummaryOverride ?? cal.Summary ?? cal.Id,
                                IsVisible = true
                            });
                        }
                    }

                    pageToken = SyncPaginationPolicy.GetNextPageToken(pageToken, calendarList?.NextPageToken, seenPageTokens);
                } while (!string.IsNullOrEmpty(pageToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Google] Failed to fetch calendar list: {ex.Message}");
                throw;
            }

            return result;
        }

        private static List<SubscribedCalendarInfo> CloneCalendars(IEnumerable<SubscribedCalendarInfo> calendars) =>
            calendars
                .Select(cal => new SubscribedCalendarInfo
                {
                    Id = cal.Id,
                    Name = cal.Name,
                    IsVisible = cal.IsVisible
                })
                .ToList();

        public async Task<List<AgendaItem>> FetchDataAsync(DateTime min, DateTime max, CancellationToken cancellationToken)
        {
            await EnsureAuthorizedAsync(cancellationToken);
            var calendarSvc = CalendarSvc!;
            var tasksSvc = TasksSvc!;

            // Fetch events from all calendars
            var calendars = await FetchCalendarListAsync(cancellationToken);
            if (calendars.Count == 0)
            {
                calendars.Add(new SubscribedCalendarInfo { Id = "primary", Name = "Primary" });
            }

            using var calendarGate = new SemaphoreSlim(4);
            var calendarTasks = calendars
                .Select(async cal =>
                {
                    await calendarGate.WaitAsync(cancellationToken);
                    try
                    {
                        return await FetchCalendarEventsAsync(calendarSvc, cal, min, max, cancellationToken);
                    }
                    finally
                    {
                        calendarGate.Release();
                    }
                })
                .ToList();
            var tasksTask = FetchGoogleTasksAsync(tasksSvc, min, max, cancellationToken);

            await Task.WhenAll(calendarTasks.Append(tasksTask));

            var items = new List<AgendaItem>();
            foreach (var calendarTask in calendarTasks)
                items.AddRange(await calendarTask);
            items.AddRange(await tasksTask);

            return items;
        }

        private async Task<List<AgendaItem>> FetchCalendarEventsAsync(CalendarService calendarSvc, SubscribedCalendarInfo cal, DateTime min, DateTime max, CancellationToken cancellationToken)
        {
            var items = new List<AgendaItem>();
            var allEvents = new List<Google.Apis.Calendar.v3.Data.Event>();
            string? pageToken = null;
            var seenPageTokens = new HashSet<string>(StringComparer.Ordinal);
            do
            {
                try
                {
                    var req = calendarSvc.Events.List(cal.Id);
                    req.TimeMinDateTimeOffset = min;
                    req.TimeMaxDateTimeOffset = max;
                    req.SingleEvents = true;
                    req.MaxResults = 2500;
                    req.PageToken = pageToken;
                    var events = await ExecuteGoogleRequestAsync(
                        ct => req.ExecuteAsync(ct),
                        cancellationToken).ConfigureAwait(false);
                    if (events?.Items != null)
                        allEvents.AddRange(events.Items);
                    pageToken = SyncPaginationPolicy.GetNextPageToken(pageToken, events?.NextPageToken, seenPageTokens);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Google] Failed to fetch events from calendar {cal.Id}: {ex.Message}");
                    throw;
                }
            } while (pageToken != null);

            var recurrenceKinds = await FetchGoogleMasterRecurrenceKindsAsync(calendarSvc, cal.Id, allEvents, cancellationToken);
            foreach (var ev in allEvents)
            {
                DateTime? date = ev.Start?.DateTimeDateTimeOffset?.DateTime ?? (DateTime.TryParse(ev.Start?.Date, out var d) ? d : null);
                if (date == null) continue;

                var endDate2 = ev.End?.DateTimeDateTimeOffset?.DateTime ?? (DateTime.TryParse(ev.End?.Date, out var ed) ? ed : (DateTime?)null);
                string recurringEventId = ev.RecurringEventId ?? "";
                string recurrenceKind = GetGoogleRecurrenceKind(ev.Recurrence);
                if (recurrenceKind == "None" && recurrenceKinds.TryGetValue(recurringEventId, out var masterKind))
                    recurrenceKind = masterKind;

                var mapped = SyncItemMappingPolicy.MapEvent(
                    ev.Id, ev.Summary, ev.Location, ev.Description, ProviderName, cal.Id, cal.Name,
                    date.Value, ev.Start?.DateTimeDateTimeOffset?.DateTime, endDate2,
                    ev.Start?.DateTimeDateTimeOffset == null,
                    _loader.GetStringOrDefault("TextAllDay") ?? "All Day",
                    recurringEventId, ev.Recurrence?.Count > 0, recurrenceKind);

                items.Add(new AgendaItem
                {
                    Id = mapped.Id, Title = mapped.Title, Subtitle = mapped.Subtitle,
                    Location = mapped.Location, Description = mapped.Description, IsEvent = true,
                    Provider = mapped.Provider, CalendarId = mapped.CalendarId,
                    CalendarName = mapped.CalendarName, DateKey = mapped.DateKey,
                    StartDateTime = mapped.StartDateTime, EndDateTime = mapped.EndDateTime,
                    IsRecurring = mapped.IsRecurring, RecurringEventId = mapped.RecurringEventId,
                    RecurrenceKind = mapped.RecurrenceKind
                });
            }
            return items;
        }

        private async Task<List<AgendaItem>> FetchGoogleTasksAsync(TasksService tasksSvc, DateTime min, DateTime max, CancellationToken cancellationToken)
        {
            var items = new List<AgendaItem>();
            var range = SyncRangePolicy.NormalizeHalfOpenDateRange(min, max);
            string? taskPageToken = null;
            var seenTaskPageTokens = new HashSet<string>(StringComparer.Ordinal);
            do
            {
                var tasksReq = tasksSvc.Tasks.List("@default");
                tasksReq.ShowCompleted = true; tasksReq.ShowHidden = true; tasksReq.MaxResults = 100; tasksReq.PageToken = taskPageToken;
                var tasks = await tasksReq.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                if (tasks?.Items != null)
                {
                    foreach (var t in tasks.Items)
                    {
                        bool isDone = t.Status == "completed";
                        DateTime taskDate = DateTime.Today;
                        if (!string.IsNullOrEmpty(t.Due) && DateTime.TryParse(t.Due, out var dueTime)) taskDate = dueTime.Date;
                        else if (isDone && !string.IsNullOrEmpty(t.Completed) && DateTime.TryParse(t.Completed, out var compTime)) taskDate = compTime.Date;

                        if (!SyncRangePolicy.ShouldIncludeTask(taskDate, isDone, range.StartDate, range.EndDate))
                            continue;

                        var mapped = SyncItemMappingPolicy.MapTask(
                            t.Id,
                            t.Title,
                            _loader.GetStringOrDefault("TextTask") ?? "Task",
                            t.Notes,
                            ProviderName,
                            null,
                            null,
                            taskDate,
                            isDone);

                        items.Add(new AgendaItem
                        {
                            Id = mapped.Id,
                            Title = mapped.Title,
                            Subtitle = mapped.Subtitle,
                            IsEvent = false,
                            IsTask = true,
                            IsCompleted = mapped.IsCompleted,
                            Description = mapped.Description,
                            Provider = mapped.Provider,
                            CalendarId = mapped.CalendarId,
                            CalendarName = mapped.CalendarName,
                            DateKey = mapped.DateKey
                        });
                    }
                }
                taskPageToken = SyncPaginationPolicy.GetNextPageToken(taskPageToken, tasks?.NextPageToken, seenTaskPageTokens);
            } while (taskPageToken != null);
            return items;
        }
        public async Task UpdateItemAsync(string itemId, bool isEvent, string title, string location, string description, DateTime targetDate, TimeSpan? startTime, TimeSpan? endTime)
        {
            if (isEvent)
            {
                await EnsureAuthorizedAsync();
                var calendarSvc = CalendarSvc!;
                var ev = await calendarSvc.Events.Get("primary", itemId).ExecuteAsync();
                ev.Summary = title;
                ev.Location = location;
                ev.Description = description;

                var window = SyncEventTimePolicy.Create(targetDate, startTime, endTime);
                if (window.IsAllDay)
                {
                    ev.Start = new Google.Apis.Calendar.v3.Data.EventDateTime { Date = window.Start.ToString("yyyy-MM-dd") };
                    ev.End = new Google.Apis.Calendar.v3.Data.EventDateTime { Date = window.End.ToString("yyyy-MM-dd") };
                }
                else
                {
                    ev.Start = new Google.Apis.Calendar.v3.Data.EventDateTime { DateTimeDateTimeOffset = window.Start };
                    ev.End = new Google.Apis.Calendar.v3.Data.EventDateTime { DateTimeDateTimeOffset = window.End };
                }
                await calendarSvc.Events.Update(ev, "primary", itemId).ExecuteAsync();
            }
            else
            {
                await EnsureAuthorizedAsync();
                var tasksSvc = TasksSvc!;
                var task = await tasksSvc.Tasks.Get("@default", itemId).ExecuteAsync();
                task.Title = title;
                task.Notes = description;
                task.Due = targetDate.ToString("yyyy-MM-dd'T'00:00:00.000'Z'");
                await tasksSvc.Tasks.Update(task, "@default", itemId).ExecuteAsync();
            }
        }

        public async Task UpdateTaskStatusAsync(string taskId, bool isCompleted)
        {
            var status = isCompleted ? "completed" : "needsAction";
            await EnsureAuthorizedAsync();
            var updateRequest = TasksSvc!.Tasks.Patch(new Google.Apis.Tasks.v1.Data.Task { Id = taskId, Status = status }, "@default", taskId);
            await updateRequest.ExecuteAsync();
        }

        public async Task CreateEventAsync(string title, DateTime targetDate, TimeSpan startTime, TimeSpan endTime, string location, bool isAllDay, EventRecurrenceKind recurrence = EventRecurrenceKind.None)
        {
            var newEvent = new Google.Apis.Calendar.v3.Data.Event
            {
                Summary = title,
                Location = string.IsNullOrWhiteSpace(location) ? null : location
            };

            var window = SyncEventTimePolicy.Create(targetDate, isAllDay ? null : startTime, endTime);
            if (window.IsAllDay)
            {
                newEvent.Start = new Google.Apis.Calendar.v3.Data.EventDateTime { Date = window.Start.ToString("yyyy-MM-dd") };
                newEvent.End = new Google.Apis.Calendar.v3.Data.EventDateTime { Date = window.End.ToString("yyyy-MM-dd") };
            }
            else
            {
                newEvent.Start = new Google.Apis.Calendar.v3.Data.EventDateTime { DateTimeDateTimeOffset = window.Start };
                newEvent.End = new Google.Apis.Calendar.v3.Data.EventDateTime { DateTimeDateTimeOffset = window.End };
            }

            if (recurrence != EventRecurrenceKind.None)
                newEvent.Recurrence = new List<string> { $"RRULE:FREQ={RecurrencePolicy.ToGoogleFrequency(recurrence.ToString())}" };

            await EnsureAuthorizedAsync();
            await CalendarSvc!.Events.Insert(newEvent, "primary").ExecuteAsync();
        }

        public async Task CreateTaskAsync(string title, DateTime targetDate, TimeSpan startTime, bool isAllDay)
        {
            var newTask = new Google.Apis.Tasks.v1.Data.Task { Title = title };

            newTask.Due = targetDate.ToString("yyyy-MM-dd'T'00:00:00.000'Z'");

            await EnsureAuthorizedAsync();
            await TasksSvc!.Tasks.Insert(newTask, "@default").ExecuteAsync();
        }

        public async Task DeleteItemAsync(string itemId, bool isEvent, RecurringDeleteMode recurringDeleteMode = RecurringDeleteMode.Single, DateTime? occurrenceDate = null, string recurringEventId = "")
        {
            if (isEvent)
            {
                if (CalendarSvc != null)
                {
                    if (recurringDeleteMode == RecurringDeleteMode.All && !string.IsNullOrWhiteSpace(recurringEventId))
                    {
                        await CalendarSvc.Events.Delete("primary", recurringEventId).ExecuteAsync();
                    }
                    else if (recurringDeleteMode == RecurringDeleteMode.ThisAndFollowing && !string.IsNullOrWhiteSpace(recurringEventId) && occurrenceDate.HasValue)
                    {
                        var master = await CalendarSvc.Events.Get("primary", recurringEventId).ExecuteAsync();
                        master.Recurrence = ClampGoogleRecurrence(master.Recurrence, occurrenceDate.Value.AddDays(-1));
                        await CalendarSvc.Events.Update(master, "primary", recurringEventId).ExecuteAsync();
                    }
                    else
                    {
                        await CalendarSvc.Events.Delete("primary", itemId).ExecuteAsync();
                    }
                }
            }
            else
            {
                if (TasksSvc != null)
                {
                    await TasksSvc.Tasks.Delete("@default", itemId).ExecuteAsync();
                }
            }
        }

        private static async Task<Dictionary<string, string>> FetchGoogleMasterRecurrenceKindsAsync(
            CalendarService calendarSvc,
            string calendarId,
            IEnumerable<Google.Apis.Calendar.v3.Data.Event> events,
            CancellationToken cancellationToken)
        {
            var eventIds = events
                .Where(item => GetGoogleRecurrenceKind(item.Recurrence) == "None")
                .Select(item => item.RecurringEventId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .ToList();
            if (eventIds.Count == 0)
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            using var gate = new SemaphoreSlim(4);
            var fetchTasks = eventIds.Select(async eventId =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    var master = await ExecuteGoogleRequestAsync(
                        ct => calendarSvc.Events.Get(calendarId, eventId).ExecuteAsync(ct),
                        cancellationToken);
                    return (EventId: eventId, Kind: GetGoogleRecurrenceKind(master.Recurrence));
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

        private static string GetGoogleRecurrenceKind(IList<string>? recurrence)
        {
            return RecurrencePolicy.ToDisplayKindFromGoogleRRules(recurrence);
        }

        private static async Task<T> ExecuteGoogleRequestAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken)
        {
            int retriesCompleted = 0;
            while (true)
            {
                try
                {
                    return await operation(cancellationToken).ConfigureAwait(false);
                }
                catch (Google.GoogleApiException ex) when (
                    SyncRetryPolicy.ShouldRetryHttpStatus((int)ex.HttpStatusCode, retriesCompleted))
                {
                    await Task.Delay(SyncRetryPolicy.GetDelay(retriesCompleted), cancellationToken).ConfigureAwait(false);
                    retriesCompleted++;
                }
                catch (HttpRequestException ex) when (
                    ex.StatusCode.HasValue &&
                    SyncRetryPolicy.ShouldRetryHttpStatus((int)ex.StatusCode.Value, retriesCompleted))
                {
                    await Task.Delay(SyncRetryPolicy.GetDelay(retriesCompleted), cancellationToken).ConfigureAwait(false);
                    retriesCompleted++;
                }
            }
        }

        private static List<string> ClampGoogleRecurrence(IList<string>? recurrence, DateTime untilDate)
        {
            var result = recurrence?.ToList() ?? new List<string>();
            string until = untilDate.Date.ToString("yyyyMMdd") + "T235959Z";

            for (int i = 0; i < result.Count; i++)
            {
                if (!result[i].StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase)) continue;

                var parts = result[i].Split(';')
                    .Where(part => !part.StartsWith("UNTIL=", StringComparison.OrdinalIgnoreCase) &&
                                   !part.StartsWith("COUNT=", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                parts.Add($"UNTIL={until}");
                result[i] = string.Join(";", parts);
                return result;
            }

            return result;
        }
    }
}
