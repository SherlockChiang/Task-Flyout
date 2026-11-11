using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Task_Flyout.Models;

namespace Task_Flyout.Services
{
    public enum ProviderHealthKind
    {
        Ready,
        Syncing,
        Cached,
        ReconnectRequired,
        Failed
    }

    public sealed record ProviderHealthSnapshot(
        string ProviderName,
        ProviderHealthKind Kind,
        DateTimeOffset? LastAttemptUtc,
        DateTimeOffset? LastSuccessUtc,
        bool HasCachedData)
    {
        public string AccountId { get; init; } = "";
        public string ProviderKey => AccountIdentityPolicy.CreateProviderKey(ProviderName, AccountId);
    }

    public sealed record VersionedDayItemsSnapshot(
        long Version,
        Dictionary<string, List<AgendaItem>> DayItems);

    internal sealed class AgendaCachePublishedEventArgs : EventArgs
    {
        public AgendaCachePublishedEventArgs(long version) => Version = version;
        public long Version { get; }
    }

    public class SyncManager
    {
        private readonly List<ISyncProvider> _providers = new();
        private readonly object _providerLock = new();
        private AppCache _cache = new();
        private PublishedCacheSnapshot _publishedCache = new(0, new AppCache());
        private int _cacheLoaded;
        private long _cacheVersion;
        private readonly ReaderWriterLockSlim _cacheLock = new(LockRecursionPolicy.NoRecursion);
        private readonly SharedRequestCoordinator<List<AgendaItem>> _dataRequests = new();
        private readonly ConcurrentDictionary<string, ProviderHealthSnapshot> _providerHealth = new(StringComparer.OrdinalIgnoreCase);
        private readonly VersionedAsyncWriter<string> _cacheWriter = new(
            json => LocalSqliteStore.WriteProtectedTextAsync(StoreScope, CacheKey, json));
        private const string StoreScope = "calendar";
        private const string CacheKey = "local_cache_winui3";
        private const int RetainedTaskPastYears = 1;
        private const int RetainedTaskFutureYears = 3;

        public IReadOnlyList<ISyncProvider> Providers => GetProvidersSnapshot();
        public AccountManager AccountManager { get; } = new AccountManager();
        public event EventHandler? ProviderHealthChanged;
        internal event EventHandler<AgendaCachePublishedEventArgs>? CachePublished;

        public void RegisterProvider(ISyncProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);
            lock (_providerLock)
            {
                if (_providers.Any(existing => string.Equals(existing.ProviderKey, provider.ProviderKey, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("A sync provider with the same account identity is already registered.");
                _providers.Add(provider);
            }
        }

        public GoogleSyncProvider RegisterGoogleProviderForNewAccount()
        {
            while (true)
            {
                string accountId = AccountIdentityPolicy.CreateAccountId();
                if (AccountManager.GetAccountById(accountId) != null || GetProvider("Google", accountId) != null)
                    continue;

                var provider = new GoogleSyncProvider(accountId);
                RegisterProvider(provider);
                return provider;
            }
        }

        public GoogleSyncProvider EnsureGoogleProvider(string accountId, string? displayName = null)
        {
            string resolvedAccountId = AccountIdentityPolicy.ResolveAccountId("Google", accountId);
            if (GetProvider("Google", resolvedAccountId) is GoogleSyncProvider existing)
            {
                existing.SetAccountDisplayName(displayName);
                return existing;
            }

            var provider = new GoogleSyncProvider(resolvedAccountId, displayName);
            RegisterProvider(provider);
            return provider;
        }

        public void HydrateAccountProviders()
        {
            foreach (var account in AccountManager.Accounts
                .Where(account => string.Equals(
                    ProviderAuthorizationLifecycle.NormalizeProviderName(account.ProviderName),
                    "Google",
                    StringComparison.OrdinalIgnoreCase))
                .ToList())
            {
                EnsureGoogleProvider(account.AccountId, account.DisplayName);
            }
        }

        public bool UnregisterProvider(string providerName, string? accountId)
        {
            string providerKey = AccountIdentityPolicy.CreateProviderKey(providerName, accountId);
            bool removed;
            lock (_providerLock)
            {
                removed = _providers.RemoveAll(provider => string.Equals(
                    provider.ProviderKey,
                    providerKey,
                    StringComparison.OrdinalIgnoreCase)) > 0;
            }

            if (removed)
            {
                _providerHealth.TryRemove(providerKey, out _);
                ProviderHealthChanged?.Invoke(this, EventArgs.Empty);
            }
            return removed;
        }

        public ProviderHealthSnapshot GetProviderHealth(string providerName)
        {
            var providers = GetProvidersSnapshot();
            var provider = providers.FirstOrDefault(candidate =>
                string.Equals(candidate.ProviderName, providerName, StringComparison.OrdinalIgnoreCase)
                && IsProviderConnected(candidate))
                ?? providers.FirstOrDefault(candidate =>
                    string.Equals(candidate.ProviderName, providerName, StringComparison.OrdinalIgnoreCase));
            return GetProviderHealth(providerName, provider?.AccountId);
        }

        public ProviderHealthSnapshot GetProviderHealth(string providerName, string? accountId)
        {
            string resolvedAccountId = AccountIdentityPolicy.ResolveAccountId(providerName, accountId);
            string providerKey = AccountIdentityPolicy.CreateProviderKey(providerName, resolvedAccountId);
            if (_providerHealth.TryGetValue(providerKey, out var health)) return health;
            bool hasCachedData = HasCachedData(providerName, resolvedAccountId);
            return new ProviderHealthSnapshot(
                providerName,
                hasCachedData ? ProviderHealthKind.Cached : ProviderHealthKind.Ready,
                null,
                null,
                hasCachedData)
            {
                AccountId = resolvedAccountId
            };
        }

        public IReadOnlyList<ProviderHealthSnapshot> GetProviderHealthSnapshot()
            => GetProvidersSnapshot().Select(provider => GetProviderHealth(provider.ProviderName, provider.AccountId)).ToList();

        public async Task SyncAllCalendarsAsync()
        {
            var activeProviders = GetProvidersSnapshot().Where(IsProviderConnected).ToList();

            foreach (var provider in activeProviders)
            {
                try
                {
                    await provider.EnsureAuthorizedAsync();
                    var remoteCalendars = await provider.FetchCalendarListAsync();
                    var account = AccountManager.GetAccount(provider.ProviderName, provider.AccountId);

                    if (account != null && remoteCalendars != null && remoteCalendars.Count > 0)
                    {
                        bool changed = false;

                        foreach (var rCal in remoteCalendars)
                        {
                            var existing = account.Calendars.FirstOrDefault(c => c.Id == rCal.Id);
                            if (existing == null)
                            {
                                account.Calendars.Add(new SubscribedCalendarInfo
                                {
                                    Id = rCal.Id,
                                    Name = rCal.Name,
                                    ColorHex = rCal.ColorHex,
                                    IsVisible = true
                                });
                                changed = true;
                            }
                            else
                            {
                                if (existing.Name != rCal.Name)
                                {
                                    existing.Name = rCal.Name;
                                    changed = true;
                                }
                                if (string.IsNullOrWhiteSpace(existing.ColorHex) && !string.IsNullOrWhiteSpace(rCal.ColorHex))
                                {
                                    existing.ColorHex = rCal.ColorHex;
                                    changed = true;
                                }
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
            return CloneCache(Volatile.Read(ref _publishedCache).Cache);
        }

        public Task WarmCacheAsync() => Task.Run(() =>
        {
            try { EnsureCacheLoaded(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Agenda cache warm failed: {ex.Message}"); }
        });

        public Dictionary<string, List<AgendaItem>> GetDayItemsSnapshot(IEnumerable<string> dateKeys)
            => GetVersionedDayItemsSnapshot(dateKeys).DayItems;

        public VersionedDayItemsSnapshot GetVersionedDayItemsSnapshot(IEnumerable<string> dateKeys)
        {
            EnsureCacheLoaded();
            var published = Volatile.Read(ref _publishedCache);
            return CreateDayItemsSnapshotIfChanged(
                published,
                long.MinValue,
                dateKeys)!;
        }

        public VersionedDayItemsSnapshot? GetVersionedDayItemsSnapshotIfChanged(
            long knownVersion,
            IEnumerable<string> dateKeys)
        {
            EnsureCacheLoaded();
            var published = Volatile.Read(ref _publishedCache);
            return CreateDayItemsSnapshotIfChanged(published, knownVersion, dateKeys);
        }

        private static VersionedDayItemsSnapshot? CreateDayItemsSnapshotIfChanged(
            PublishedCacheSnapshot published,
            long knownVersion,
            IEnumerable<string> dateKeys)
        {
            var slice = VersionedBucketSlicePolicy.CreateIfChanged(
                knownVersion,
                published.Version,
                published.Cache.DayItems,
                dateKeys,
                CloneAgendaItem);
            return slice == null
                ? null
                : new VersionedDayItemsSnapshot(slice.Version, slice.Buckets);
        }

        public AppCache GetRangeCacheSnapshot(DateTime min, DateTime max, bool tasksOnly = false)
        {
            EnsureCacheLoaded();
            min = min.Date;
            max = max.Date;

            var cache = Volatile.Read(ref _publishedCache).Cache;
            var dayItems = new Dictionary<string, List<AgendaItem>>(StringComparer.Ordinal);
            for (var day = min; day < max; day = day.AddDays(1))
            {
                var key = day.ToString("yyyy-MM-dd");
                if (!cache.DayItems.TryGetValue(key, out var items)) continue;

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

        public AppCache GetTaskCacheSnapshot()
        {
            EnsureCacheLoaded();

            var cache = Volatile.Read(ref _publishedCache).Cache;
            var dayItems = cache.DayItems
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

        public async Task UpsertCachedItemAsync(AgendaItem item, string? oldDateKey = null)
        {
            EnsureCacheLoaded();
            _cacheLock.EnterWriteLock();
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
                PublishCacheSnapshotNoLock();
            }
            finally
            {
                _cacheLock.ExitWriteLock();
            }

            await SaveCacheAsync();
        }

        public async Task RemoveCachedItemAsync(AgendaItem item)
        {
            EnsureCacheLoaded();
            _cacheLock.EnterWriteLock();
            try
            {
                RemoveMatchingCachedItems(item);
                RebuildMarkedDates();
                PublishCacheSnapshotNoLock();
            }
            finally
            {
                _cacheLock.ExitWriteLock();
            }

            await SaveCacheAsync();
        }

        public async Task SetCachedTaskCompletionAsync(AgendaItem target, bool isCompleted)
        {
            EnsureCacheLoaded();
            _cacheLock.EnterWriteLock();
            try
            {
                foreach (var items in _cache.DayItems.Values)
                {
                    foreach (var item in items.Where(item => item.IsTask && IsSameCachedItem(item, target)))
                        item.IsCompleted = isCompleted;
                }

                PublishCacheSnapshotNoLock();
            }
            finally
            {
                _cacheLock.ExitWriteLock();
            }

            await SaveCacheAsync();
        }

        public async Task SaveLocalCacheAsync(AppCache? localCache = null)
        {
            EnsureCacheLoaded();
            _cacheLock.EnterWriteLock();
            try
            {
                if (localCache != null)
                    _cache = CloneCache(localCache);
                RebuildMarkedDates();
                PublishCacheSnapshotNoLock();
            }
            finally
            {
                _cacheLock.ExitWriteLock();
            }

            await SaveCacheAsync();
        }

        public async Task<List<AgendaItem>> GetAllDataAsync(DateTime min, DateTime max, bool forceRefresh = false)
            => await GetAllDataAsync(min, max, forceRefresh, CancellationToken.None);

        // A caller can stop waiting without cancelling work still needed by another caller.
        // The provider request is cancelled only after its final waiter leaves.
        public async Task<List<AgendaItem>> GetAllDataAsync(DateTime min, DateTime max, bool forceRefresh, CancellationToken cancellationToken)
        {
            min = min.Date;
            max = max.Date;

            var providerKey = string.Join(',',
                GetProvidersSnapshot()
                    .Where(IsProviderConnected)
                    .Select(provider => provider.ProviderKey)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
            var requestKey = $"{min:yyyy-MM-dd}|{max:yyyy-MM-dd}|{forceRefresh}|{providerKey}";

            var result = await _dataRequests.RunAsync(
                requestKey,
                requestCancellation => GetAllDataCoreAsync(min, max, forceRefresh, requestCancellation),
                cancellationToken);
            return result.Select(CloneAgendaItem).ToList();
        }

        private async Task<List<AgendaItem>> GetAllDataCoreAsync(DateTime min, DateTime max, bool forceRefresh, CancellationToken cancellationToken)
        {
            EnsureCacheLoaded();
            min = min.Date;
            max = max.Date;

            var activeProviders = GetProvidersSnapshot().Where(IsProviderConnected).ToList();
            if (activeProviders.Count == 0)
                return GetCachedItems(min, max);

            if (!forceRefresh && IsRangeCached(min, max, activeProviders))
                return GetCachedItems(min, max);

            var remoteSpan = PerformanceDiagnostics.StartSpanUntilSuccess(
                "calendar.remote.sync",
                "calendar",
                "first_remote_sync_completion",
                "remote");

            var allItems = new List<AgendaItem>();
            var successfulProviders = new List<ISyncProvider>();

            var fetchTasks = activeProviders.Select(async provider =>
            {
                SetProviderHealth(provider, ProviderHealthKind.Syncing, DateTimeOffset.UtcNow, null);
                try
                {
                    await provider.EnsureAuthorizedAsync(cancellationToken);
                    var items = await provider.FetchDataAsync(min, max, cancellationToken);
                    foreach (var item in items ?? Enumerable.Empty<AgendaItem>())
                    {
                        if (string.IsNullOrWhiteSpace(item.Provider)) item.Provider = provider.ProviderName;
                        if (string.IsNullOrWhiteSpace(item.AccountId)) item.AccountId = provider.AccountId;
                    }
                    SetProviderHealth(provider, ProviderHealthKind.Ready, null, DateTimeOffset.UtcNow);
                    return (Provider: provider, Items: items ?? new List<AgendaItem>(), Success: true);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (AuthorizationInteractionRequiredException)
                {
                    SetProviderHealth(provider, ProviderHealthKind.ReconnectRequired, null, null);
                    return (Provider: provider, Items: new List<AgendaItem>(), Success: false);
                }
                catch
                {
                    SetProviderHealth(provider, HasCachedData(provider.ProviderName, provider.AccountId) ? ProviderHealthKind.Cached : ProviderHealthKind.Failed, null, null);
                    return (Provider: provider, Items: new List<AgendaItem>(), Success: false);
                }
            });

            (ISyncProvider Provider, List<AgendaItem> Items, bool Success)[] results;
            try
            {
                results = await Task.WhenAll(fetchTasks);
            }
            catch
            {
                remoteSpan.Complete("failure");
                throw;
            }
            if (cancellationToken.IsCancellationRequested)
            {
                remoteSpan.Complete("failure");
                cancellationToken.ThrowIfCancellationRequested();
            }
            foreach (var result in results)
            {
                if (result.Success)
                {
                    successfulProviders.Add(result.Provider);
                    allItems.AddRange(result.Items);
                }
            }

            try
            {
                bool changed = await MergeIntoCacheAsync(min, max, allItems, successfulProviders, activeProviders);
                if (changed)
                    await SaveCacheAsync();
            }
            catch
            {
                remoteSpan.Complete("failure");
                throw;
            }

            remoteSpan.Complete(successfulProviders.Count > 0 ? "success" : "failure");

            return GetCachedItems(min, max);
        }

        private bool HasCachedData(string providerName, string? accountId)
        {
            EnsureCacheLoaded();
            var cache = Volatile.Read(ref _publishedCache).Cache;
            return cache.CachedRanges.Any(range => AccountIdentityPolicy.Matches(range.ProviderName, range.AccountId, providerName, accountId))
                || cache.DayItems.Values.Any(items => items.Any(item => AccountIdentityPolicy.Matches(item.Provider, item.AccountId, providerName, accountId)));
        }

        private void SetProviderHealth(ISyncProvider provider, ProviderHealthKind kind, DateTimeOffset? attempt, DateTimeOffset? success)
        {
            _providerHealth.AddOrUpdate(
                provider.ProviderKey,
                _ => new ProviderHealthSnapshot(provider.ProviderName, kind, attempt, success, HasCachedData(provider.ProviderName, provider.AccountId))
                {
                    AccountId = provider.AccountId
                },
                (_, current) => current with
                {
                    Kind = kind,
                    LastAttemptUtc = attempt ?? current.LastAttemptUtc,
                    LastSuccessUtc = success ?? current.LastSuccessUtc,
                    HasCachedData = HasCachedData(provider.ProviderName, provider.AccountId),
                    AccountId = provider.AccountId
                });
            ProviderHealthChanged?.Invoke(this, EventArgs.Empty);
        }

        public async Task UpdateTaskStatusAsync(string providerName, string taskId, bool isCompleted, string taskListId = "", string? accountId = null)
        {
            var provider = ResolveProvider(providerName, accountId);
            if (provider == null || !IsProviderConnected(provider))
                throw new InvalidOperationException("The task provider is unavailable.");
            await provider.UpdateTaskStatusAsync(taskId, isCompleted, taskListId);
        }

        public async Task UpdateItemAsync(string providerName, string itemId, bool isEvent, string title, string location, string description, DateTime targetDate, TimeSpan? startTime, TimeSpan? endTime, string taskListId = "", string? accountId = null)
        {
            var provider = ResolveProvider(providerName, accountId);
            if (provider == null || !IsProviderConnected(provider))
                throw new InvalidOperationException("The item provider is unavailable.");
            await provider.UpdateItemAsync(itemId, isEvent, title, location, description, targetDate, startTime, endTime, taskListId);
        }

        public async Task CreateItemAsync(string title, bool isEvent, bool isAllDay, DateTime targetDate, TimeSpan startTime, TimeSpan endTime, string location, string? providerName = null, string? accountId = null)
            => await CreateItemAsync(title, isEvent, isAllDay, targetDate, startTime, endTime, location, EventRecurrenceKind.None, providerName, accountId);

        public async Task CreateItemAsync(string title, bool isEvent, bool isAllDay, DateTime targetDate, TimeSpan startTime, TimeSpan endTime, string location, EventRecurrenceKind recurrence, string? providerName = null, string? accountId = null)
        {
            var provider = providerName != null
                ? ResolveProvider(providerName, accountId)
                : GetProvidersSnapshot().FirstOrDefault(IsProviderConnected);
            if (provider == null) return;

            if (isEvent)
                await provider.CreateEventAsync(title, targetDate, startTime, endTime, location, isAllDay, recurrence);
            else
                await provider.CreateTaskAsync(title, targetDate, startTime, isAllDay);
        }

        public async Task DeleteItemAsync(string providerName, string itemId, bool isEvent, string taskListId = "", string? accountId = null)
            => await DeleteItemAsync(providerName, itemId, isEvent, RecurringDeleteMode.Single, null, "", taskListId, accountId);

        public async Task DeleteItemAsync(string providerName, string itemId, bool isEvent, RecurringDeleteMode recurringDeleteMode, DateTime? occurrenceDate, string recurringEventId, string taskListId = "", string? accountId = null)
        {
            var provider = ResolveProvider(providerName, accountId);
            if (provider == null || !IsProviderConnected(provider))
                throw new InvalidOperationException("The item provider is unavailable.");
            await provider.DeleteItemAsync(itemId, isEvent, recurringDeleteMode, occurrenceDate, recurringEventId, taskListId);
        }

        public async Task RemoveAgendaAccountAsync(string providerName, string? accountId = null)
        {
            var account = AccountManager.GetAccount(providerName, accountId);
            if (account == null) return;
            AccountManager.RemoveAccountById(account.AccountId);
            RemoveProviderFromCache(account.ProviderName, account.AccountId);
            await SaveCacheAsync();
        }

        internal Task ClearProviderAuthorizationForDisconnectAsync(string providerName, string? accountId = null)
            => ClearProviderAuthorizationAsync(providerName, accountId);

        private async Task ClearProviderAuthorizationAsync(string providerName, string? accountId)
        {
            var provider = ResolveProvider(providerName, accountId);
            try
            {
                if (provider is GoogleSyncProvider google)
                    await google.ClearLocalAuthorizationAsync();
                else if (provider is MicrosoftSyncProvider microsoft)
                    await microsoft.ClearLocalAuthorizationAsync();
                else if (provider is ICloudSyncProvider iCloud)
                    await iCloud.ClearLocalAuthorizationAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Provider auth cleanup failed for {providerName}: {ex.Message}");
                throw;
            }
        }

        public ISyncProvider? GetProvider(string providerName)
            => ResolveProvider(providerName, null);

        public ISyncProvider? GetProvider(string providerName, string? accountId)
            => ResolveProvider(providerName, accountId);

        private ISyncProvider? ResolveProvider(string providerName, string? accountId)
        {
            var matches = GetProvidersSnapshot().Where(provider => string.Equals(
                provider.ProviderName,
                ProviderAuthorizationLifecycle.NormalizeProviderName(providerName),
                StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(accountId))
                return matches.FirstOrDefault(provider => AccountIdentityPolicy.Matches(
                    provider.ProviderName,
                    provider.AccountId,
                    providerName,
                    accountId));
            return matches.FirstOrDefault(IsProviderConnected) ?? matches.FirstOrDefault();
        }

        private bool IsProviderConnected(ISyncProvider provider)
            => AccountManager.IsConnected(provider.ProviderName, provider.AccountId);

        private List<ISyncProvider> GetProvidersSnapshot()
        {
            lock (_providerLock)
                return _providers.ToList();
        }

        private void EnsureCacheLoaded()
        {
            if (Volatile.Read(ref _cacheLoaded) != 0) return;

            _cacheLock.EnterWriteLock();
            try
            {
                if (Volatile.Read(ref _cacheLoaded) != 0) return;

                AppCache loaded;
                try
                {
                    string? json = LocalSqliteStore.ReadProtectedText(StoreScope, CacheKey);
                    loaded = JsonFallbackPolicy.DeserializeOrDefault(
                        json,
                        value => JsonSerializer.Deserialize(value, AppJsonContext.Default.AppCache),
                        () => new AppCache());
                }
                catch
                {
                    loaded = new AppCache();
                }

                loaded.MarkedDates ??= new HashSet<string>();
                loaded.DayItems ??= new Dictionary<string, List<AgendaItem>>();
                loaded.CachedRanges ??= new List<AgendaCacheRange>();
                loaded.CachedRanges = loaded.CachedRanges
                    .Where(range => !string.IsNullOrWhiteSpace(range.ProviderName)
                                    && !string.IsNullOrWhiteSpace(range.StartDateKey)
                                    && !string.IsNullOrWhiteSpace(range.EndDateKey))
                    .ToList();

                bool identityMigrated = false;
                foreach (var range in loaded.CachedRanges)
                {
                    if (!string.IsNullOrWhiteSpace(range.AccountId)) continue;
                    range.AccountId = AccountIdentityPolicy.CreateLegacyAccountId(range.ProviderName);
                    identityMigrated = true;
                }
                foreach (var item in loaded.DayItems.Values.SelectMany(items => items))
                {
                    if (!string.IsNullOrWhiteSpace(item.AccountId)) continue;
                    item.AccountId = AccountIdentityPolicy.CreateLegacyAccountId(item.Provider);
                    identityMigrated = true;
                }

                _cache = loaded;

                if (identityMigrated || CompactCache(DateTime.Today))
                    SaveCacheSync(CloneCache(_cache));
                PublishCacheSnapshotNoLock();

                Volatile.Write(ref _cacheLoaded, 1);
            }
            finally
            {
                _cacheLock.ExitWriteLock();
            }
        }

        private void RemoveProviderFromCache(string providerName, string accountId)
        {
            EnsureCacheLoaded();
            _cacheLock.EnterWriteLock();
            try
            {
                foreach (var key in _cache.DayItems.Keys.ToList())
                {
                    _cache.DayItems[key].RemoveAll(item => AccountIdentityPolicy.Matches(
                        item.Provider,
                        item.AccountId,
                        providerName,
                        accountId));
                    if (_cache.DayItems[key].Count == 0)
                        _cache.DayItems.Remove(key);
                }

                _cache.CachedRanges.RemoveAll(range => AccountIdentityPolicy.Matches(
                    range.ProviderName,
                    range.AccountId,
                    providerName,
                    accountId));
                RebuildMarkedDates();
                PublishCacheSnapshotNoLock();
            }
            finally
            {
                _cacheLock.ExitWriteLock();
            }
        }

        private async Task SaveCacheAsync()
        {
            try
            {
                var published = Volatile.Read(ref _publishedCache);
                string json = JsonSerializer.Serialize(published.Cache, AppJsonContext.Default.AppCache);
                await _cacheWriter.WriteAsync(published.Version, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Calendar cache save failed: {ex.Message}");
            }
        }

        private bool IsRangeCached(DateTime min, DateTime max, IEnumerable<ISyncProvider> providers)
        {
            string start = min.ToString("yyyy-MM-dd");
            string end = max.AddDays(-1).ToString("yyyy-MM-dd");
            var providerList = providers
                .GroupBy(provider => provider.ProviderKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            if (providerList.Count == 0) return false;

            var cache = Volatile.Read(ref _publishedCache).Cache;
            return providerList.All(provider => cache.CachedRanges.Any(range =>
                AccountIdentityPolicy.Matches(range.ProviderName, range.AccountId, provider.ProviderName, provider.AccountId) &&
                string.Compare(range.StartDateKey, start, StringComparison.Ordinal) <= 0 &&
                string.Compare(range.EndDateKey, end, StringComparison.Ordinal) >= 0));
        }

        private List<AgendaItem> GetCachedItems(DateTime min, DateTime max)
        {
            string start = min.ToString("yyyy-MM-dd");
            string end = max.AddDays(-1).ToString("yyyy-MM-dd");

            var cache = Volatile.Read(ref _publishedCache).Cache;
            return cache.DayItems
                .Where(kvp =>
                    string.Compare(kvp.Key, start, StringComparison.Ordinal) >= 0 &&
                    string.Compare(kvp.Key, end, StringComparison.Ordinal) <= 0)
                .SelectMany(kvp => kvp.Value.Select(CloneAgendaItem))
                .ToList();
        }

        private Task<bool> MergeIntoCacheAsync(DateTime min, DateTime max, List<AgendaItem> items, List<ISyncProvider> successfulProviders, List<ISyncProvider> attemptedProviders)
        {
            string start = min.ToString("yyyy-MM-dd");
            string end = max.AddDays(-1).ToString("yyyy-MM-dd");
            var successfulProviderSet = successfulProviders.Select(provider => provider.ProviderKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var attemptedProviderSet = attemptedProviders.Select(provider => provider.ProviderKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

            _cacheLock.EnterWriteLock();
            try
            {
                // Snapshot the affected slice before mutating so a no-op periodic sync
                // (remote data unchanged) can skip the expensive DPAPI-encrypt + SQLite
                // write in SaveCacheAsync.
                string beforeItemsSig = BuildRangeItemsSignature(min, max);
                string beforeRangeSig = GetRangeSignature(_cache.CachedRanges);

                for (var d = min; d < max; d = d.AddDays(1))
                {
                    var key = d.ToString("yyyy-MM-dd");
                    if (!_cache.DayItems.ContainsKey(key)) continue;

                    _cache.DayItems[key].RemoveAll(item =>
                        successfulProviderSet.Contains(item.ProviderKey));

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
                    attemptedProviderSet.Contains(AccountIdentityPolicy.CreateProviderKey(range.ProviderName, range.AccountId)) &&
                    string.Compare(range.EndDateKey, start, StringComparison.Ordinal) >= 0 &&
                    string.Compare(range.StartDateKey, end, StringComparison.Ordinal) <= 0);

                foreach (var provider in successfulProviders)
                    _cache.CachedRanges.Add(new AgendaCacheRange
                    {
                        ProviderName = provider.ProviderName,
                        AccountId = provider.AccountId,
                        StartDateKey = start,
                        EndDateKey = end
                    });

                MergeCacheRanges();
                bool compacted = CompactCache(DateTime.Today);
                RebuildMarkedDates();
                PublishCacheSnapshotNoLock();

                // CompactCache may touch items outside [min,max); OR it in directly.
                return Task.FromResult(compacted
                    || beforeRangeSig != GetRangeSignature(_cache.CachedRanges)
                    || beforeItemsSig != BuildRangeItemsSignature(min, max));
            }
            finally
            {
                _cacheLock.ExitWriteLock();
            }
        }

        // Order-independent content signature of the day items within [min,max).
        private string BuildRangeItemsSignature(DateTime min, DateTime max)
        {
            var sb = new System.Text.StringBuilder();
            for (var d = min; d < max; d = d.AddDays(1))
            {
                var key = d.ToString("yyyy-MM-dd");
                if (!_cache.DayItems.TryGetValue(key, out var items) || items.Count == 0) continue;

                sb.Append(key).Append('=');
                foreach (var line in items
                    .Select(i => $"{i.ProviderKey}{i.Id}{i.Title}{i.IsCompleted}{(i.StartDateTime?.Ticks ?? 0)}{(i.EndDateTime?.Ticks ?? 0)}{i.Subtitle}")
                    .OrderBy(s => s, StringComparer.Ordinal))
                {
                    sb.Append(line).Append('');
                }
                sb.Append('');
            }
            return sb.ToString();
        }

        private void MergeCacheRanges()
        {
            var ranges = _cache.CachedRanges
                .Where(range => !string.IsNullOrWhiteSpace(range.ProviderName) &&
                                !string.IsNullOrWhiteSpace(range.StartDateKey) &&
                                !string.IsNullOrWhiteSpace(range.EndDateKey))
                .OrderBy(range => AccountIdentityPolicy.CreateProviderKey(range.ProviderName, range.AccountId), StringComparer.OrdinalIgnoreCase)
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
                if (AccountIdentityPolicy.Matches(range.ProviderName, range.AccountId, last.ProviderName, last.AccountId) &&
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
                    AccountId = range.AccountId,
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
                ranges.Select(range => $"{AccountIdentityPolicy.CreateProviderKey(range.ProviderName, range.AccountId)}|{range.StartDateKey}|{range.EndDateKey}"));

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
                return $"{item.ProviderKey}|task|{item.Id}";

            return $"{item.ProviderKey}|task|{item.Title}|{item.DateKey}";
        }

        private static string GetEventCacheKey(AgendaItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.Id))
                return $"{item.ProviderKey}|event|{item.Id}|{item.DateKey}";

            return $"{item.ProviderKey}|event|{item.Title}|{item.Subtitle}|{item.DateKey}";
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

        private void PublishCacheSnapshotNoLock()
        {
            var previous = Volatile.Read(ref _publishedCache).Cache;
            var snapshot = new AppCache
            {
                MarkedDates = _cache.MarkedDates?.ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(),
                DayItems = IncrementalBucketSnapshot.Create(
                    _cache.DayItems,
                    previous.DayItems,
                    AreAgendaItemsEqual,
                    CloneAgendaItem),
                CachedRanges = _cache.CachedRanges?
                    .Select(range => new AgendaCacheRange
                    {
                        ProviderName = range.ProviderName,
                        AccountId = range.AccountId,
                        StartDateKey = range.StartDateKey,
                        EndDateKey = range.EndDateKey
                    })
                    .ToList()
                    ?? new List<AgendaCacheRange>()
            };
            long version = ++_cacheVersion;
            Volatile.Write(ref _publishedCache, new PublishedCacheSnapshot(version, snapshot));
            QueueCachePublished(version);
        }

        private void QueueCachePublished(long version)
        {
            var handlers = CachePublished;
            if (handlers == null) return;

            _ = Task.Run(() =>
            {
                var args = new AgendaCachePublishedEventArgs(version);
                foreach (EventHandler<AgendaCachePublishedEventArgs> handler in handlers.GetInvocationList())
                {
                    try { handler(this, args); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Agenda cache subscriber failed: {ex.Message}"); }
                }
            });
        }

        private sealed record PublishedCacheSnapshot(long Version, AppCache Cache);

        private static void SaveCacheSync(AppCache snapshot)
        {
            try
            {
                string json = JsonSerializer.Serialize(snapshot, AppJsonContext.Default.AppCache);
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
                return AccountIdentityPolicy.Matches(a.Provider, a.AccountId, b.Provider, b.AccountId)
                    && string.Equals(a.Id, b.Id, StringComparison.Ordinal);

            return AccountIdentityPolicy.Matches(a.Provider, a.AccountId, b.Provider, b.AccountId)
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
                        AccountId = range.AccountId,
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
                AccountId = item.AccountId,
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

        private static bool AreAgendaItemsEqual(AgendaItem left, AgendaItem right)
            => left.Id == right.Id
               && left.Title == right.Title
               && left.Subtitle == right.Subtitle
               && left.IsTask == right.IsTask
               && left.IsEvent == right.IsEvent
               && left.IsCompleted == right.IsCompleted
               && left.Location == right.Location
               && left.Description == right.Description
               && left.Provider == right.Provider
               && left.AccountId == right.AccountId
               && left.CalendarId == right.CalendarId
               && left.CalendarName == right.CalendarName
               && left.DateKey == right.DateKey
               && left.ColorHex == right.ColorHex
               && left.IsRecurring == right.IsRecurring
               && left.RecurringEventId == right.RecurringEventId
               && left.RecurrenceKind == right.RecurrenceKind
               && left.StartDateTime == right.StartDateTime
               && left.EndDateTime == right.EndDateTime;
    }
}
