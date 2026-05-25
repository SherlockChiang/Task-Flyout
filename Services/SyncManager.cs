using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Task_Flyout.Models;

namespace Task_Flyout.Services
{
    public class SyncManager
    {
        private readonly List<ISyncProvider> _providers = new();
        private AppCache _cache = new();
        private int _cacheLoaded;
        private readonly SemaphoreSlim _cacheLock = new(1, 1);
        private const string StoreScope = "calendar";
        private const string CacheKey = "local_cache_winui3";
        private const int RetainedTaskPastYears = 1;
        private const int RetainedTaskFutureYears = 3;

        public IReadOnlyList<ISyncProvider> Providers => _providers;
        public AccountManager AccountManager { get; } = new AccountManager();

        public void RegisterProvider(ISyncProvider provider) => _providers.Add(provider);

        public async Task SyncAllCalendarsAsync()
        {
            var activeProviders = _providers.Where(p => AccountManager.IsConnected(p.ProviderName)).ToList();

            foreach (var provider in activeProviders)
            {
                try
                {
                    await provider.EnsureAuthorizedAsync();
                    var remoteCalendars = await provider.FetchCalendarListAsync();
                    var account = AccountManager.GetAccount(provider.ProviderName);

                    if (account != null && remoteCalendars != null && remoteCalendars.Count > 0)
                    {
                        bool changed = false;

                        foreach (var rCal in remoteCalendars)
                        {
                            var existing = account.Calendars.FirstOrDefault(c => c.Id == rCal.Id);
                            if (existing == null)
                            {
                                account.Calendars.Add(new SubscribedCalendarInfo { Id = rCal.Id, Name = rCal.Name, IsVisible = true });
                                changed = true;
                            }
                            else if (existing.Name != rCal.Name)
                            {
                                existing.Name = rCal.Name;
                                changed = true;
                            }
                        }

                        var toRemove = account.Calendars.Where(c => !remoteCalendars.Any(r => r.Id == c.Id)).ToList();
                        foreach (var c in toRemove)
                        {
                            account.Calendars.Remove(c);
                            changed = true;
                        }

                        if (changed)
                        {
                            AccountManager.EnsureDefaultColors();
                            AccountManager.Save();
                        }
                    }
                }
                catch { }
            }

            AccountManager.EnsureDefaultColors();
        }

        public AppCache GetLocalCache()
        {
            EnsureCacheLoaded();
            _cacheLock.Wait();
            try
            {
                return CloneCache(_cache);
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        public Dictionary<string, List<AgendaItem>> GetDayItemsSnapshot(IEnumerable<string> dateKeys)
        {
            EnsureCacheLoaded();
            var keys = dateKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            _cacheLock.Wait();
            try
            {
                var result = new Dictionary<string, List<AgendaItem>>(StringComparer.Ordinal);
                foreach (var key in keys)
                {
                    if (_cache.DayItems.TryGetValue(key, out var items))
                        result[key] = items.Select(CloneAgendaItem).ToList();
                }

                return result;
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        public AppCache GetRangeCacheSnapshot(DateTime min, DateTime max, bool tasksOnly = false)
        {
            EnsureCacheLoaded();
            min = min.Date;
            max = max.Date;

            _cacheLock.Wait();
            try
            {
                var dayItems = new Dictionary<string, List<AgendaItem>>(StringComparer.Ordinal);
                for (var day = min; day < max; day = day.AddDays(1))
                {
                    var key = day.ToString("yyyy-MM-dd");
                    if (!_cache.DayItems.TryGetValue(key, out var items)) continue;

                    var snapshotItems = items
                        .Where(item => !tasksOnly || item.IsTask)
                        .Select(CloneAgendaItem)
                        .ToList();
                    if (snapshotItems.Count > 0)
                        dayItems[key] = snapshotItems;
                }

                return new AppCache
                {
                    DayItems = dayItems,
                    MarkedDates = dayItems.Keys.ToHashSet(StringComparer.Ordinal)
                };
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        public AppCache GetTaskCacheSnapshot()
        {
            EnsureCacheLoaded();

            _cacheLock.Wait();
            try
            {
                var dayItems = _cache.DayItems
                    .Select(kvp => new
                    {
                        kvp.Key,
                        Items = kvp.Value
                            .Where(item => item.IsTask)
                            .Select(CloneAgendaItem)
                            .ToList()
                    })
                    .Where(kvp => kvp.Items.Count > 0)
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Items, StringComparer.Ordinal);

                return new AppCache
                {
                    DayItems = dayItems,
                    MarkedDates = dayItems.Keys.ToHashSet(StringComparer.Ordinal)
                };
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        public async Task UpsertCachedItemAsync(AgendaItem item, string? oldDateKey = null)
        {
            EnsureCacheLoaded();
            await _cacheLock.WaitAsync();
            try
            {
                RemoveMatchingCachedItems(item, oldDateKey);

                if (!string.IsNullOrWhiteSpace(item.DateKey))
                {
                    if (!_cache.DayItems.TryGetValue(item.DateKey, out var items))
                    {
                        items = new List<AgendaItem>();
                        _cache.DayItems[item.DateKey] = items;
                    }

                    items.Add(CloneAgendaItem(item));
                }

                RebuildMarkedDates();
            }
            finally
            {
                _cacheLock.Release();
            }

            await SaveCacheAsync();
        }

        public async Task RemoveCachedItemAsync(AgendaItem item)
        {
            EnsureCacheLoaded();
            await _cacheLock.WaitAsync();
            try
            {
                RemoveMatchingCachedItems(item);
                RebuildMarkedDates();
            }
            finally
            {
                _cacheLock.Release();
            }

            await SaveCacheAsync();
        }

        public async Task SetCachedTaskCompletionAsync(AgendaItem target, bool isCompleted)
        {
            EnsureCacheLoaded();
            await _cacheLock.WaitAsync();
            try
            {
                foreach (var items in _cache.DayItems.Values)
                {
                    foreach (var item in items.Where(item => item.IsTask && IsSameCachedItem(item, target)))
                        item.IsCompleted = isCompleted;
                }
            }
            finally
            {
                _cacheLock.Release();
            }

            await SaveCacheAsync();
        }

        public async Task SaveLocalCacheAsync(AppCache? localCache = null)
        {
            EnsureCacheLoaded();
            await _cacheLock.WaitAsync();
            try
            {
                if (localCache != null)
                    _cache = CloneCache(localCache);
                RebuildMarkedDates();
            }
            finally
            {
                _cacheLock.Release();
            }

            await SaveCacheAsync();
        }

        public async Task<List<AgendaItem>> GetAllDataAsync(DateTime min, DateTime max, bool forceRefresh = false)
        {
            EnsureCacheLoaded();
            min = min.Date;
            max = max.Date;

            var activeProviders = _providers.Where(p => AccountManager.IsConnected(p.ProviderName)).ToList();
            if (activeProviders.Count == 0)
                return GetCachedItems(min, max);

            if (!forceRefresh && IsRangeCached(min, max, activeProviders.Select(provider => provider.ProviderName)))
                return GetCachedItems(min, max);

            var allItems = new List<AgendaItem>();
            var successfulProviders = new List<string>();
            var attemptedProviders = activeProviders.Select(provider => provider.ProviderName).ToList();

            var fetchTasks = activeProviders.Select(async provider =>
            {
                try
                {
                    await provider.EnsureAuthorizedAsync();
                    var items = await provider.FetchDataAsync(min, max);
                    return (Provider: provider.ProviderName, Items: items ?? new List<AgendaItem>(), Success: true);
                }
                catch { return (Provider: provider.ProviderName, Items: new List<AgendaItem>(), Success: false); }
            });

            var results = await Task.WhenAll(fetchTasks);
            foreach (var result in results)
            {
                if (result.Success)
                {
                    successfulProviders.Add(result.Provider);
                    allItems.AddRange(result.Items);
                }
            }

            await MergeIntoCacheAsync(min, max, allItems, successfulProviders, attemptedProviders);
            await SaveCacheAsync();

            return GetCachedItems(min, max);
        }

        public async Task UpdateTaskStatusAsync(string providerName, string taskId, bool isCompleted)
        {
            var provider = _providers.FirstOrDefault(p => p.ProviderName == providerName);
            if (provider != null) await provider.UpdateTaskStatusAsync(taskId, isCompleted);
        }

        public async Task UpdateItemAsync(string providerName, string itemId, bool isEvent, string title, string location, string description, DateTime targetDate, TimeSpan? startTime, TimeSpan? endTime)
        {
            var provider = _providers.FirstOrDefault(p => p.ProviderName == providerName);
            if (provider != null) await provider.UpdateItemAsync(itemId, isEvent, title, location, description, targetDate, startTime, endTime);
        }

        public async Task CreateItemAsync(string title, bool isEvent, bool isAllDay, DateTime targetDate, TimeSpan startTime, TimeSpan endTime, string location, string? providerName = null)
            => await CreateItemAsync(title, isEvent, isAllDay, targetDate, startTime, endTime, location, EventRecurrenceKind.None, providerName);

        public async Task CreateItemAsync(string title, bool isEvent, bool isAllDay, DateTime targetDate, TimeSpan startTime, TimeSpan endTime, string location, EventRecurrenceKind recurrence, string? providerName = null)
        {
            var provider = providerName != null ? _providers.FirstOrDefault(p => p.ProviderName == providerName) : _providers.FirstOrDefault();
            if (provider == null) return;

            if (isEvent)
                await provider.CreateEventAsync(title, targetDate, startTime, endTime, location, isAllDay, recurrence);
            else
                await provider.CreateTaskAsync(title, targetDate, startTime, isAllDay);
        }

        public async Task DeleteItemAsync(string providerName, string itemId, bool isEvent)
            => await DeleteItemAsync(providerName, itemId, isEvent, RecurringDeleteMode.Single, null, "");

        public async Task DeleteItemAsync(string providerName, string itemId, bool isEvent, RecurringDeleteMode recurringDeleteMode, DateTime? occurrenceDate, string recurringEventId)
        {
            var provider = _providers.FirstOrDefault(p => p.ProviderName == providerName);
            if (provider != null)
            {
                await provider.DeleteItemAsync(itemId, isEvent, recurringDeleteMode, occurrenceDate, recurringEventId);
            }
        }

        public async Task RemoveAccountAsync(string providerName)
        {
            AccountManager.RemoveAccount(providerName);
            RemoveProviderFromCache(providerName);
            await SaveCacheAsync();
        }

        public ISyncProvider? GetProvider(string providerName)
            => _providers.FirstOrDefault(p => p.ProviderName == providerName);

        private void EnsureCacheLoaded()
        {
            if (Interlocked.CompareExchange(ref _cacheLoaded, 1, 0) != 0) return;

            try
            {
                string? json = LocalSqliteStore.ReadProtectedText(StoreScope, CacheKey);
                if (!string.IsNullOrWhiteSpace(json))
                    _cache = JsonSerializer.Deserialize(json, AppJsonContext.Default.AppCache) ?? new AppCache();
            }
            catch
            {
                _cache = new AppCache();
            }

            _cache.MarkedDates ??= new HashSet<string>();
            _cache.DayItems ??= new Dictionary<string, List<AgendaItem>>();
            _cache.CachedRanges ??= new List<AgendaCacheRange>();
            _cache.CachedRanges = _cache.CachedRanges
                .Where(range => !string.IsNullOrWhiteSpace(range.ProviderName)
                                && !string.IsNullOrWhiteSpace(range.StartDateKey)
                                && !string.IsNullOrWhiteSpace(range.EndDateKey))
                .ToList();

            if (CompactCache(DateTime.Today))
                SaveCacheSyncNoLock();
        }

        private void RemoveProviderFromCache(string providerName)
        {
            EnsureCacheLoaded();
            _cacheLock.Wait();
            try
            {
                foreach (var key in _cache.DayItems.Keys.ToList())
                {
                    _cache.DayItems[key].RemoveAll(item => string.Equals(item.Provider, providerName, StringComparison.OrdinalIgnoreCase));
                    if (_cache.DayItems[key].Count == 0)
                        _cache.DayItems.Remove(key);
                }

                RebuildMarkedDates();
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        private async Task SaveCacheAsync()
        {
            try
            {
                string json;
                await _cacheLock.WaitAsync();
                try
                {
                    json = JsonSerializer.Serialize(_cache, AppJsonContext.Default.AppCache);
                }
                finally
                {
                    _cacheLock.Release();
                }
                await LocalSqliteStore.WriteProtectedTextAsync(StoreScope, CacheKey, json);
            }
            catch { }
        }

        private bool IsRangeCached(DateTime min, DateTime max, IEnumerable<string> providerNames)
        {
            string start = min.ToString("yyyy-MM-dd");
            string end = max.AddDays(-1).ToString("yyyy-MM-dd");
            var providers = providerNames
                .Where(provider => !string.IsNullOrWhiteSpace(provider))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return providers.Count > 0 && providers.All(provider => _cache.CachedRanges.Any(range =>
                string.Equals(range.ProviderName, provider, StringComparison.OrdinalIgnoreCase) &&
                string.Compare(range.StartDateKey, start, StringComparison.Ordinal) <= 0 &&
                string.Compare(range.EndDateKey, end, StringComparison.Ordinal) >= 0));
        }

        private List<AgendaItem> GetCachedItems(DateTime min, DateTime max)
        {
            string start = min.ToString("yyyy-MM-dd");
            string end = max.AddDays(-1).ToString("yyyy-MM-dd");

            return _cache.DayItems
                .Where(kvp =>
                    string.Compare(kvp.Key, start, StringComparison.Ordinal) >= 0 &&
                    string.Compare(kvp.Key, end, StringComparison.Ordinal) <= 0)
                .SelectMany(kvp => kvp.Value)
                .ToList();
        }

        private async Task MergeIntoCacheAsync(DateTime min, DateTime max, List<AgendaItem> items, List<string> successfulProviders, List<string> attemptedProviders)
        {
            string start = min.ToString("yyyy-MM-dd");
            string end = max.AddDays(-1).ToString("yyyy-MM-dd");
            var successfulProviderSet = successfulProviders.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var attemptedProviderSet = attemptedProviders.ToHashSet(StringComparer.OrdinalIgnoreCase);

            await _cacheLock.WaitAsync();
            try
            {
                for (var d = min; d < max; d = d.AddDays(1))
                {
                    var key = d.ToString("yyyy-MM-dd");
                    if (!_cache.DayItems.ContainsKey(key)) continue;

                    _cache.DayItems[key].RemoveAll(item =>
                        successfulProviderSet.Contains(item.Provider));

                    if (_cache.DayItems[key].Count == 0)
                        _cache.DayItems.Remove(key);
                }

                foreach (var item in items.Where(item => !string.IsNullOrWhiteSpace(item.DateKey)))
                {
                    if (!ShouldKeepSyncedItem(item, min, max))
                        continue;

                    if (!_cache.DayItems.ContainsKey(item.DateKey))
                        _cache.DayItems[item.DateKey] = new List<AgendaItem>();

                    _cache.DayItems[item.DateKey].Add(item);
                }

                _cache.CachedRanges.RemoveAll(range =>
                    attemptedProviderSet.Contains(range.ProviderName) &&
                    string.Compare(range.EndDateKey, start, StringComparison.Ordinal) >= 0 &&
                    string.Compare(range.StartDateKey, end, StringComparison.Ordinal) <= 0);

                foreach (var providerName in successfulProviderSet)
                    _cache.CachedRanges.Add(new AgendaCacheRange { ProviderName = providerName, StartDateKey = start, EndDateKey = end });

                MergeCacheRanges();
                CompactCache(DateTime.Today);
                RebuildMarkedDates();
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        private void MergeCacheRanges()
        {
            var ranges = _cache.CachedRanges
                .Where(range => !string.IsNullOrWhiteSpace(range.ProviderName) &&
                                !string.IsNullOrWhiteSpace(range.StartDateKey) &&
                                !string.IsNullOrWhiteSpace(range.EndDateKey))
                .OrderBy(range => range.ProviderName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(range => range.StartDateKey)
                .ToList();

            var merged = new List<AgendaCacheRange>();
            foreach (var range in ranges)
            {
                if (merged.Count == 0)
                {
                    merged.Add(range);
                    continue;
                }

                var last = merged[^1];
                if (string.Equals(range.ProviderName, last.ProviderName, StringComparison.OrdinalIgnoreCase) &&
                    string.Compare(range.StartDateKey, last.EndDateKey, StringComparison.Ordinal) <= 0)
                {
                    if (string.Compare(range.EndDateKey, last.EndDateKey, StringComparison.Ordinal) > 0)
                        last.EndDateKey = range.EndDateKey;
                }
                else
                {
                    merged.Add(range);
                }
            }

            _cache.CachedRanges = merged;
        }

        private bool CompactCache(DateTime today)
        {
            int originalItemCount = _cache.DayItems.Sum(kvp => kvp.Value.Count);
            int originalDayCount = _cache.DayItems.Count;
            int originalRangeCount = _cache.CachedRanges.Count;
            string originalRangeSignature = GetRangeSignature(_cache.CachedRanges);
            bool replacedTask = false;

            var minTaskDate = today.Date.AddYears(-RetainedTaskPastYears);
            var maxTaskDate = today.Date.AddYears(RetainedTaskFutureYears);
            var minTaskKey = minTaskDate.ToString("yyyy-MM-dd");
            var maxTaskEndKey = maxTaskDate.AddDays(-1).ToString("yyyy-MM-dd");

            var compactedDayItems = new Dictionary<string, List<AgendaItem>>(StringComparer.Ordinal);
            var taskItems = new Dictionary<string, AgendaItem>(StringComparer.OrdinalIgnoreCase);
            var eventKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in _cache.DayItems.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
            {
                foreach (var item in kvp.Value)
                {
                    if (string.IsNullOrWhiteSpace(item.DateKey))
                        item.DateKey = kvp.Key;

                    if (!TryParseDateKey(item.DateKey, out var itemDate))
                        continue;

                    if (item.IsTask)
                    {
                        if (!item.IsCompleted && (itemDate < minTaskDate || itemDate >= maxTaskDate))
                            continue;

                        var taskKey = GetTaskCacheKey(item);
                        if (!taskItems.TryGetValue(taskKey, out var existing) || ShouldReplaceTask(existing, item))
                        {
                            replacedTask |= existing != null;
                            taskItems[taskKey] = item;
                        }

                        continue;
                    }

                    var eventKey = GetEventCacheKey(item);
                    if (eventKeys.Add(eventKey))
                        AddCachedItem(compactedDayItems, item);
                }
            }

            foreach (var item in taskItems.Values)
                AddCachedItem(compactedDayItems, item);

            _cache.DayItems = compactedDayItems
                .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal);

            _cache.CachedRanges = _cache.CachedRanges
                .Select(range => new AgendaCacheRange
                {
                    ProviderName = range.ProviderName,
                    StartDateKey = string.Compare(range.StartDateKey, minTaskKey, StringComparison.Ordinal) < 0 ? minTaskKey : range.StartDateKey,
                    EndDateKey = string.Compare(range.EndDateKey, maxTaskEndKey, StringComparison.Ordinal) > 0 ? maxTaskEndKey : range.EndDateKey
                })
                .Where(range => string.Compare(range.StartDateKey, range.EndDateKey, StringComparison.Ordinal) <= 0)
                .ToList();
            MergeCacheRanges();
            RebuildMarkedDates();

            return originalItemCount != _cache.DayItems.Sum(kvp => kvp.Value.Count)
                || originalDayCount != _cache.DayItems.Count
                || originalRangeCount != _cache.CachedRanges.Count
                || originalRangeSignature != GetRangeSignature(_cache.CachedRanges)
                || replacedTask;
        }

        private static string GetRangeSignature(IEnumerable<AgendaCacheRange> ranges)
            => string.Join(
                "\n",
                ranges.Select(range => $"{range.ProviderName}|{range.StartDateKey}|{range.EndDateKey}"));

        private static void AddCachedItem(Dictionary<string, List<AgendaItem>> dayItems, AgendaItem item)
        {
            if (!dayItems.TryGetValue(item.DateKey, out var items))
            {
                items = new List<AgendaItem>();
                dayItems[item.DateKey] = items;
            }

            items.Add(item);
        }

        private static bool ShouldReplaceTask(AgendaItem existing, AgendaItem candidate)
        {
            var existingKey = existing.DateKey ?? "";
            var candidateKey = candidate.DateKey ?? "";
            var compare = string.Compare(candidateKey, existingKey, StringComparison.Ordinal);
            if (compare != 0) return compare > 0;

            if (string.IsNullOrWhiteSpace(existing.Description) && !string.IsNullOrWhiteSpace(candidate.Description))
                return true;

            return false;
        }

        private static string GetTaskCacheKey(AgendaItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.Id))
                return $"{item.Provider}|task|{item.Id}";

            return $"{item.Provider}|task|{item.Title}|{item.DateKey}";
        }

        private static string GetEventCacheKey(AgendaItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.Id))
                return $"{item.Provider}|event|{item.Id}|{item.DateKey}";

            return $"{item.Provider}|event|{item.Title}|{item.Subtitle}|{item.DateKey}";
        }

        private static bool IsDateKeyInRange(string dateKey, DateTime min, DateTime max)
            => TryParseDateKey(dateKey, out var date) && date >= min.Date && date < max.Date;

        private static bool ShouldKeepSyncedItem(AgendaItem item, DateTime min, DateTime max)
        {
            if (!TryParseDateKey(item.DateKey, out var date)) return false;
            if (date >= min.Date && date < max.Date) return true;
            return item.IsTask && item.IsCompleted;
        }

        private static bool TryParseDateKey(string? dateKey, out DateTime date)
            => DateTime.TryParseExact(
                dateKey,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date);

        private void SaveCacheSyncNoLock()
        {
            try
            {
                string json = JsonSerializer.Serialize(_cache, AppJsonContext.Default.AppCache);
                LocalSqliteStore.WriteProtectedText(StoreScope, CacheKey, json);
            }
            catch { }
        }

        private void RebuildMarkedDates()
        {
            _cache.MarkedDates = _cache.DayItems
                .Where(kvp => kvp.Value.Any(AccountManager.IsItemVisible))
                .Select(kvp => kvp.Key)
                .ToHashSet();
        }

        private void RemoveMatchingCachedItems(AgendaItem target, string? preferredDateKey = null)
        {
            var keys = !string.IsNullOrWhiteSpace(preferredDateKey)
                ? new[] { preferredDateKey }.Concat(_cache.DayItems.Keys.Where(key => key != preferredDateKey)).ToList()
                : _cache.DayItems.Keys.ToList();

            foreach (var key in keys)
            {
                if (!_cache.DayItems.TryGetValue(key, out var items)) continue;
                items.RemoveAll(item => IsSameCachedItem(item, target));
                if (items.Count == 0)
                    _cache.DayItems.Remove(key);
            }
        }

        private static bool IsSameCachedItem(AgendaItem a, AgendaItem b)
        {
            if (!string.IsNullOrWhiteSpace(a.Id) && !string.IsNullOrWhiteSpace(b.Id))
                return string.Equals(a.Provider, b.Provider, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(a.Id, b.Id, StringComparison.Ordinal);

            return string.Equals(a.Provider, b.Provider, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.Title, b.Title, StringComparison.Ordinal)
                && string.Equals(a.DateKey, b.DateKey, StringComparison.Ordinal);
        }

        private static AppCache CloneCache(AppCache cache)
            => new()
            {
                MarkedDates = cache.MarkedDates?.ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(),
                DayItems = cache.DayItems?
                    .ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value.Select(CloneAgendaItem).ToList(),
                        StringComparer.Ordinal)
                    ?? new Dictionary<string, List<AgendaItem>>(StringComparer.Ordinal),
                CachedRanges = cache.CachedRanges?
                    .Select(range => new AgendaCacheRange
                    {
                        ProviderName = range.ProviderName,
                        StartDateKey = range.StartDateKey,
                        EndDateKey = range.EndDateKey
                    })
                    .ToList()
                    ?? new List<AgendaCacheRange>()
            };

        private static AgendaItem CloneAgendaItem(AgendaItem item)
            => new()
            {
                Id = item.Id,
                Title = item.Title,
                Subtitle = item.Subtitle,
                IsTask = item.IsTask,
                IsEvent = item.IsEvent,
                IsCompleted = item.IsCompleted,
                Location = item.Location,
                Description = item.Description,
                Provider = item.Provider,
                CalendarId = item.CalendarId,
                CalendarName = item.CalendarName,
                DateKey = item.DateKey,
                ColorHex = item.ColorHex,
                IsRecurring = item.IsRecurring,
                RecurringEventId = item.RecurringEventId,
                RecurrenceKind = item.RecurrenceKind,
                StartDateTime = item.StartDateTime,
                EndDateTime = item.EndDateTime
            };
    }
}
