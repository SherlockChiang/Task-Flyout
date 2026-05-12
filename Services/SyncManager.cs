using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Task_Flyout.Models;

namespace Task_Flyout.Services
{
    public class SyncManager
    {
        private readonly List<ISyncProvider> _providers = new();
        private AppCache _cache = new();
        private bool _cacheLoaded;
        private readonly object _cacheLock = new();
        private static readonly string CacheFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TaskFlyout", "local_cache_winui3.json");

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
            return _cache;
        }

        public async Task SaveLocalCacheAsync()
        {
            EnsureCacheLoaded();
            RebuildMarkedDates();
            await SaveCacheAsync();
        }

        public async Task<List<AgendaItem>> GetAllDataAsync(DateTime min, DateTime max, bool forceRefresh = false)
        {
            EnsureCacheLoaded();
            min = min.Date;
            max = max.Date;

            if (!forceRefresh && IsRangeCached(min, max))
                return GetCachedItems(min, max);

            var allItems = new List<AgendaItem>();
            var activeProviders = _providers.Where(p => AccountManager.IsConnected(p.ProviderName)).ToList();

            var fetchTasks = activeProviders.Select(async provider =>
            {
                try
                {
                    await provider.EnsureAuthorizedAsync();
                    return await provider.FetchDataAsync(min, max);
                }
                catch { return new List<AgendaItem>(); }
            });

            var results = await Task.WhenAll(fetchTasks);
            foreach (var result in results)
            {
                if (result != null) allItems.AddRange(result);
            }

            MergeIntoCache(min, max, allItems);
            await SaveCacheAsync();

            return allItems;
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

        public async Task CreateItemAsync(string title, bool isEvent, bool isAllDay, DateTime targetDate, TimeSpan startTime, TimeSpan endTime, string location, string providerName = null)
            => await CreateItemAsync(title, isEvent, isAllDay, targetDate, startTime, endTime, location, EventRecurrenceKind.None, providerName);

        public async Task CreateItemAsync(string title, bool isEvent, bool isAllDay, DateTime targetDate, TimeSpan startTime, TimeSpan endTime, string location, EventRecurrenceKind recurrence, string providerName = null)
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

        public ISyncProvider GetProvider(string providerName)
            => _providers.FirstOrDefault(p => p.ProviderName == providerName);

        private void EnsureCacheLoaded()
        {
            if (_cacheLoaded) return;
            _cacheLoaded = true;

            try
            {
                if (File.Exists(CacheFilePath))
                {
                    string json = File.ReadAllText(CacheFilePath);
                    _cache = JsonSerializer.Deserialize(json, AppJsonContext.Default.AppCache) ?? new AppCache();
                }
            }
            catch
            {
                _cache = new AppCache();
            }

            _cache.MarkedDates ??= new HashSet<string>();
            _cache.DayItems ??= new Dictionary<string, List<AgendaItem>>();
            _cache.CachedRanges ??= new List<AgendaCacheRange>();
        }

        private void RemoveProviderFromCache(string providerName)
        {
            EnsureCacheLoaded();
            lock (_cacheLock)
            {
                foreach (var key in _cache.DayItems.Keys.ToList())
                {
                    _cache.DayItems[key].RemoveAll(item => string.Equals(item.Provider, providerName, StringComparison.OrdinalIgnoreCase));
                    if (_cache.DayItems[key].Count == 0)
                        _cache.DayItems.Remove(key);
                }

                RebuildMarkedDates();
            }
        }

        private async Task SaveCacheAsync()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CacheFilePath));
                string json;
                lock (_cacheLock)
                {
                    json = JsonSerializer.Serialize(_cache, AppJsonContext.Default.AppCache);
                }
                await File.WriteAllTextAsync(CacheFilePath, json);
            }
            catch { }
        }

        private bool IsRangeCached(DateTime min, DateTime max)
        {
            string start = min.ToString("yyyy-MM-dd");
            string end = max.AddDays(-1).ToString("yyyy-MM-dd");

            return _cache.CachedRanges.Any(range =>
                string.Compare(range.StartDateKey, start, StringComparison.Ordinal) <= 0 &&
                string.Compare(range.EndDateKey, end, StringComparison.Ordinal) >= 0);
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

        private void MergeIntoCache(DateTime min, DateTime max, List<AgendaItem> items)
        {
            string start = min.ToString("yyyy-MM-dd");
            string end = max.AddDays(-1).ToString("yyyy-MM-dd");

            lock (_cacheLock)
            {
                for (var d = min; d < max; d = d.AddDays(1))
                    _cache.DayItems.Remove(d.ToString("yyyy-MM-dd"));

                foreach (var item in items.Where(item => !string.IsNullOrWhiteSpace(item.DateKey)))
                {
                    if (!_cache.DayItems.ContainsKey(item.DateKey))
                        _cache.DayItems[item.DateKey] = new List<AgendaItem>();

                    _cache.DayItems[item.DateKey].Add(item);
                }

                _cache.CachedRanges.RemoveAll(range =>
                    string.Compare(range.EndDateKey, start, StringComparison.Ordinal) >= 0 &&
                    string.Compare(range.StartDateKey, end, StringComparison.Ordinal) <= 0);

                _cache.CachedRanges.Add(new AgendaCacheRange { StartDateKey = start, EndDateKey = end });
                MergeCacheRanges();
                RebuildMarkedDates();
            }
        }

        private void MergeCacheRanges()
        {
            var ranges = _cache.CachedRanges
                .Where(range => !string.IsNullOrWhiteSpace(range.StartDateKey) && !string.IsNullOrWhiteSpace(range.EndDateKey))
                .OrderBy(range => range.StartDateKey)
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
                if (string.Compare(range.StartDateKey, last.EndDateKey, StringComparison.Ordinal) <= 0)
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

        private void RebuildMarkedDates()
        {
            _cache.MarkedDates = _cache.DayItems
                .Where(kvp => kvp.Value.Any(AccountManager.IsItemVisible))
                .Select(kvp => kvp.Key)
                .ToHashSet();
        }
    }
}
