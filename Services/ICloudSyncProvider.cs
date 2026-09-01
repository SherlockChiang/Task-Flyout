using Microsoft.Windows.ApplicationModel.Resources;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Task_Flyout.Models;

namespace Task_Flyout.Services
{
    public sealed class ICloudSyncProvider : ISyncProvider, IDisposable
    {
        private static readonly TimeSpan CalendarListCacheDuration = TimeSpan.FromMinutes(30);
        private readonly SemaphoreSlim _operationGate = new(1, 1);
        private readonly ResourceLoader _loader = new();
        private ICloudCalDavClient? _client;
        private List<ICloudCalendarDescriptor>? _cachedCalendars;
        private DateTimeOffset _calendarListExpiresAt = DateTimeOffset.MinValue;
        private bool _disposed;

        public string ProviderName => "iCloud";

        public Task ConnectWithCredentialsAsync(
            string accountName,
            string appSpecificPassword,
            CancellationToken cancellationToken = default)
        {
            var credentials = new ICloudCredentialEnvelope(accountName.Trim(), appSpecificPassword.Trim());
            ICloudCalDavClient.ValidateCredentials(credentials);

            return RunExclusiveAsync(async () =>
            {
                using var candidate = new ICloudCalDavClient(credentials);
                var calendars = await candidate.DiscoverCalendarsAsync(cancellationToken);
                if (calendars.Count == 0)
                    throw new InvalidDataException("No iCloud calendars were found for this Apple Account.");

                await ICloudCredentialStore.SaveAsync(credentials);
                ReplaceClient(new ICloudCalDavClient(credentials));
                SetCalendarCache(calendars);
            }, cancellationToken);
        }

        public Task ConnectInteractivelyAsync(CancellationToken cancellationToken = default)
            => RunExclusiveAsync(async () =>
            {
                EnsureAuthorizedCore();
                await RefreshCalendarListCoreAsync(cancellationToken);
            }, cancellationToken);

        public Task EnsureAuthorizedAsync(CancellationToken cancellationToken = default)
            => RunExclusiveAsync(() =>
            {
                EnsureAuthorizedCore();
                return Task.CompletedTask;
            }, cancellationToken);

        public Task ClearLocalAuthorizationAsync()
            => RunExclusiveAsync(async () =>
            {
                ReplaceClient(null);
                _cachedCalendars = null;
                _calendarListExpiresAt = DateTimeOffset.MinValue;
                await ICloudCredentialStore.ClearAsync();
            });

        public Task<List<SubscribedCalendarInfo>> FetchCalendarListAsync()
            => RunExclusiveAsync(async () =>
            {
                var calendars = await GetCalendarsCoreAsync(CancellationToken.None);
                return calendars.Select(ToSubscribedCalendar).ToList();
            });

        public Task<List<AgendaItem>> FetchDataAsync(
            DateTime min,
            DateTime max,
            CancellationToken cancellationToken)
            => RunExclusiveAsync(() => FetchDataCoreAsync(min, max, cancellationToken), cancellationToken);

        private async Task<List<AgendaItem>> FetchDataCoreAsync(
            DateTime min,
            DateTime max,
            CancellationToken cancellationToken)
        {
            EnsureAuthorizedCore();
            var calendars = await GetCalendarsCoreAsync(cancellationToken);
            var client = _client ?? throw new AuthorizationInteractionRequiredException(ProviderName, "Reconnect iCloud Calendar.");
            using var calendarGate = new SemaphoreSlim(2, 2);

            var fetchTasks = calendars.Select(async calendar =>
            {
                await calendarGate.WaitAsync(cancellationToken);
                try
                {
                    var resources = await client.QueryCalendarAsync(calendar.Uri, min, max, cancellationToken);
                    var items = new List<AgendaItem>();
                    foreach (var resource in resources)
                    {
                        foreach (var calendarEvent in ICloudIcsCodec.ParseEvents(resource.CalendarData, resource.Uri, min, max))
                        {
                            var mapped = SyncItemMappingPolicy.MapEvent(
                                calendarEvent.Id,
                                calendarEvent.Title,
                                calendarEvent.Location,
                                calendarEvent.Description,
                                ProviderName,
                                calendar.Uri.AbsoluteUri,
                                calendar.Name,
                                calendarEvent.Start,
                                calendarEvent.Start,
                                calendarEvent.End,
                                calendarEvent.IsAllDay,
                                _loader.GetStringOrDefault("TextAllDay") ?? "All Day",
                                calendarEvent.RecurringEventId,
                                calendarEvent.IsRecurring,
                                calendarEvent.RecurrenceKind);

                            items.Add(new AgendaItem
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
                    return items;
                }
                finally
                {
                    calendarGate.Release();
                }
            }).ToList();

            var fetched = await Task.WhenAll(fetchTasks);
            return fetched.SelectMany(items => items).ToList();
        }

        public Task UpdateItemAsync(
            string itemId,
            bool isEvent,
            string title,
            string location,
            string description,
            DateTime targetDate,
            TimeSpan? startTime,
            TimeSpan? endTime,
            string taskListId = "")
            => RunExclusiveAsync(() => UpdateItemCoreAsync(
                itemId,
                isEvent,
                title,
                location,
                description,
                targetDate,
                startTime,
                endTime));

        private async Task UpdateItemCoreAsync(
            string itemId,
            bool isEvent,
            string title,
            string location,
            string description,
            DateTime targetDate,
            TimeSpan? startTime,
            TimeSpan? endTime)
        {
            EnsureEventOperation(isEvent);
            ICloudEventReference reference = DecodeReference(itemId);
            EnsureAuthorizedCore();
            var client = _client!;
            var current = await client.GetResourceAsync(reference.ResourceUri, CancellationToken.None);
            EnsureConcurrencyToken(current.ETag);
            var window = SyncEventTimePolicy.Create(targetDate, startTime, endTime);
            string updated = ICloudIcsCodec.UpdateCalendar(
                current.Content,
                reference,
                title,
                location,
                description,
                window.Start,
                window.End,
                window.IsAllDay);
            await client.UpdateResourceAsync(reference.ResourceUri, updated, current.ETag, CancellationToken.None);
        }

        public Task UpdateTaskStatusAsync(string taskId, bool isCompleted, string taskListId = "")
            => Task.FromException(new NotSupportedException("iCloud Calendar does not provide task synchronization."));

        public Task CreateEventAsync(
            string title,
            DateTime targetDate,
            TimeSpan startTime,
            TimeSpan endTime,
            string location,
            bool isAllDay,
            EventRecurrenceKind recurrence = EventRecurrenceKind.None)
            => RunExclusiveAsync(() => CreateEventCoreAsync(
                title,
                targetDate,
                startTime,
                endTime,
                location,
                isAllDay,
                recurrence));

        private async Task CreateEventCoreAsync(
            string title,
            DateTime targetDate,
            TimeSpan startTime,
            TimeSpan endTime,
            string location,
            bool isAllDay,
            EventRecurrenceKind recurrence)
        {
            EnsureAuthorizedCore();
            var calendars = await GetCalendarsCoreAsync(CancellationToken.None);
            var calendar = calendars.FirstOrDefault(value => value.IsWritable)
                ?? throw new InvalidOperationException("No writable iCloud calendar is available.");
            string uid = Guid.NewGuid().ToString("D");
            var window = SyncEventTimePolicy.Create(targetDate, isAllDay ? null : startTime, endTime);
            string calendarData = ICloudIcsCodec.CreateCalendar(
                uid,
                title,
                location,
                "",
                window.Start,
                window.End,
                window.IsAllDay,
                recurrence.ToString());
            Uri resourceUri = CreateResourceUri(calendar.Uri, uid);
            await _client!.CreateResourceAsync(resourceUri, calendarData, CancellationToken.None);
        }

        public Task CreateTaskAsync(string title, DateTime targetDate, TimeSpan startTime, bool isAllDay)
            => Task.FromException(new NotSupportedException("iCloud Calendar does not provide task synchronization."));

        public Task DeleteItemAsync(
            string itemId,
            bool isEvent,
            RecurringDeleteMode recurringDeleteMode = RecurringDeleteMode.Single,
            DateTime? occurrenceDate = null,
            string recurringEventId = "",
            string taskListId = "")
            => RunExclusiveAsync(() => DeleteItemCoreAsync(itemId, isEvent, recurringDeleteMode));

        private async Task DeleteItemCoreAsync(
            string itemId,
            bool isEvent,
            RecurringDeleteMode recurringDeleteMode)
        {
            EnsureEventOperation(isEvent);
            ICloudEventReference reference = DecodeReference(itemId);
            EnsureAuthorizedCore();
            var client = _client!;
            var current = await client.GetResourceAsync(reference.ResourceUri, CancellationToken.None);
            EnsureConcurrencyToken(current.ETag);

            if (!reference.IsRecurring || recurringDeleteMode == RecurringDeleteMode.All)
            {
                await client.DeleteResourceAsync(reference.ResourceUri, current.ETag, CancellationToken.None);
                return;
            }

            string updated = ICloudIcsCodec.DeleteOccurrence(current.Content, reference, recurringDeleteMode);
            await client.UpdateResourceAsync(reference.ResourceUri, updated, current.ETag, CancellationToken.None);
        }

        private async Task<IReadOnlyList<ICloudCalendarDescriptor>> GetCalendarsCoreAsync(CancellationToken cancellationToken)
        {
            if (_cachedCalendars != null && _calendarListExpiresAt > DateTimeOffset.UtcNow)
                return _cachedCalendars.ToList();
            return await RefreshCalendarListCoreAsync(cancellationToken);
        }

        private async Task<IReadOnlyList<ICloudCalendarDescriptor>> RefreshCalendarListCoreAsync(CancellationToken cancellationToken)
        {
            EnsureAuthorizedCore();
            if (_cachedCalendars != null && _calendarListExpiresAt > DateTimeOffset.UtcNow)
                return _cachedCalendars.ToList();
            var calendars = await _client!.DiscoverCalendarsAsync(cancellationToken);
            SetCalendarCache(calendars);
            return _cachedCalendars!.ToList();
        }

        private void EnsureAuthorizedCore()
        {
            if (_client != null) return;

            var credentials = ICloudCredentialStore.Load();
            if (credentials == null)
                throw new AuthorizationInteractionRequiredException(
                    ProviderName,
                    "Connect iCloud with an Apple Account and app-specific password.");

            try
            {
                ICloudCalDavClient.ValidateCredentials(credentials);
            }
            catch (ArgumentException ex)
            {
                throw new AuthorizationInteractionRequiredException(
                    ProviderName,
                    "The saved iCloud credentials are invalid. Reconnect iCloud Calendar.",
                    ex);
            }

            ReplaceClient(new ICloudCalDavClient(credentials));
        }

        private void SetCalendarCache(IEnumerable<ICloudCalendarDescriptor> calendars)
        {
            _cachedCalendars = calendars.ToList();
            _calendarListExpiresAt = DateTimeOffset.UtcNow.Add(CalendarListCacheDuration);
        }

        private static SubscribedCalendarInfo ToSubscribedCalendar(ICloudCalendarDescriptor calendar)
            => new()
            {
                Id = calendar.Uri.AbsoluteUri,
                Name = calendar.Name,
                ColorHex = calendar.ColorHex,
                IsVisible = true
            };

        private static ICloudEventReference DecodeReference(string itemId)
        {
            if (!ICloudEventReferenceCodec.TryDecode(itemId, out var reference))
                throw new InvalidDataException("The iCloud event reference is invalid or expired.");
            return reference;
        }

        private static Uri CreateResourceUri(Uri calendarUri, string uid)
        {
            string baseUri = calendarUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
                ? calendarUri.AbsoluteUri
                : calendarUri.AbsoluteUri + "/";
            return ICloudCalDavPolicy.ResolveEndpoint(new Uri(baseUri), Uri.EscapeDataString(uid) + ".ics");
        }

        private static void EnsureEventOperation(bool isEvent)
        {
            if (!isEvent)
                throw new NotSupportedException("iCloud Calendar does not provide task synchronization.");
        }

        private static void EnsureConcurrencyToken(string etag)
        {
            if (string.IsNullOrWhiteSpace(etag))
                throw new InvalidDataException("iCloud did not return an event version. Refresh the calendar and try again.");
        }

        private void ReplaceClient(ICloudCalDavClient? client)
        {
            var previous = _client;
            _client = client;
            previous?.Dispose();
        }

        private async Task RunExclusiveAsync(
            Func<Task> operation,
            CancellationToken cancellationToken = default)
        {
            await _operationGate.WaitAsync(cancellationToken);
            try
            {
                ThrowIfDisposed();
                await operation();
            }
            finally
            {
                _operationGate.Release();
            }
        }

        private async Task<T> RunExclusiveAsync<T>(
            Func<Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            await _operationGate.WaitAsync(cancellationToken);
            try
            {
                ThrowIfDisposed();
                return await operation();
            }
            finally
            {
                _operationGate.Release();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ICloudSyncProvider));
        }

        public void Dispose()
        {
            _operationGate.Wait();
            try
            {
                if (_disposed) return;
                _disposed = true;
                ReplaceClient(null);
                _cachedCalendars = null;
            }
            finally
            {
                _operationGate.Release();
            }
        }
    }
}
