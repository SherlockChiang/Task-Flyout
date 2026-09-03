using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Requests;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Me.Messages.Item.Move;
using MimeKit;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;
using Task_Flyout.Models;
using Windows.Security.Credentials;
using Windows.Storage;
using GmailMessage = Google.Apis.Gmail.v1.Data.Message;
using GmailMessagePart = Google.Apis.Gmail.v1.Data.MessagePart;
using GraphMessage = Microsoft.Graph.Models.Message;

namespace Task_Flyout.Services
{
    public class MailAccount
    {
        private static readonly ResourceLoader _accountLoader = new();
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public MailAccountKind Kind { get; set; } = MailAccountKind.Outlook;
        public string DisplayName { get; set; } = "";
        public string Address { get; set; } = "";
        public bool IsSetupComplete { get; set; }
        public string ImapHost { get; set; } = "";
        public int ImapPort { get; set; } = 993;
        public bool ImapUseSsl { get; set; } = true;
        public string ImapUserName { get; set; } = "";
        public string SmtpHost { get; set; } = "";
        public int SmtpPort { get; set; } = 587;
        public bool SmtpUseSsl { get; set; }
        public string SmtpUserName { get; set; } = "";

        public string ProviderName => Kind switch
        {
            MailAccountKind.Outlook => "Outlook",
            MailAccountKind.Google => "Gmail",
            MailAccountKind.Imap => "IMAP",
            _ => "Mail"
        };

        public string IconGlyph => Kind switch
        {
            MailAccountKind.Outlook => "\uE715",
            MailAccountKind.Google => "\uE77B",
            MailAccountKind.Imap => "\uE8D4",
            _ => "\uE715"
        };

        public string DisplayTitle => string.IsNullOrWhiteSpace(DisplayName) ? ProviderName : DisplayName;
        public string Subtitle => string.IsNullOrWhiteSpace(Address) ? ProviderName : Address;
        public string SetupText => IsSetupComplete ? "" : (_accountLoader.GetStringOrDefault("TextSetupIncomplete") ?? "Setup required");
    }

    public sealed class NewMailNotificationEventArgs : EventArgs
    {
        public required MailAccount Account { get; init; }
        public required MailItem Item { get; init; }
    }

    public sealed class MailSendStatusUnknownException : Exception
    {
        public MailSendStatusUnknownException(Exception innerException)
            : base("The mail provider may have accepted the message before the operation timed out.", innerException)
        {
        }
    }

    public sealed class MailMutationSyncQueuedException : Exception
    {
        public MailMutationSyncQueuedException(Exception innerException)
            : base("The mail update was queued for a later retry.", innerException)
        {
        }
    }

    public sealed class MailMoveOutcomeUnknownException : Exception
    {
        public MailMoveOutcomeUnknownException(Exception innerException)
            : base("Microsoft Graph may have moved the message before the connection was interrupted.", innerException)
        {
        }
    }

    public sealed class GmailActionOutcomeUnknownException : Exception
    {
        public GmailActionOutcomeUnknownException(Exception innerException)
            : base("Gmail may have completed the action before the connection was interrupted.", innerException) { }
    }

    public sealed class ImapMoveOutcomeUnknownException : Exception
    {
        public ImapMoveOutcomeUnknownException(Exception innerException)
            : base("The IMAP server may have moved the message before the connection was interrupted.", innerException) { }
    }

    public sealed class ImapMoveIdentityUnavailableException : Exception
    {
        public ImapMoveIdentityUnavailableException()
            : base("The IMAP server moved the message but did not return its destination identity.") { }
    }

    public sealed record CachedMailMetadata(
        string AccountId,
        string FolderId,
        string MessageId,
        string Subject,
        string Sender,
        string SenderAddress,
        string Preview,
        string ReceivedTime,
        DateTimeOffset? ReceivedAt);

    public class MailService
    {
        private static readonly ResourceLoader _formatLoader = new();
        private readonly ResourceLoader _loader = new();
        private GraphServiceClient? _outlookClient;
        private List<MailAccount> _accounts = new();
        private bool _accountsLoaded;
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);
        private readonly Dictionary<string, CacheEntry<List<MailFolder>>> _folderCache = new();
        private readonly Dictionary<string, CacheEntry<List<MailItem>>> _messageCache = new();
        private readonly ConcurrentDictionary<string, byte> _knownUnreadIds = new(StringComparer.Ordinal);
        private DispatcherTimer? _pendingMutationTimer;
        private bool _pendingMutationRetryStopped;
        private int _isPollingMail;
        private int _knownUnreadLoaded;
        private DateTimeOffset _lastMailPollStartedUtc = DateTimeOffset.MinValue;
        private DateTimeOffset _lastMailPollCompletedUtc = DateTimeOffset.MinValue;
        // Per-account poll backoff: after a failure an account is skipped for an
        // exponentially growing number of poll cycles (capped) so a broken account
        // (expired token, unreachable server) stops being hammered every interval —
        // which both wastes IMAP connect/auth cycles and risks server-side lockouts.
        private sealed class PollBackoff { public int Failures; public DateTimeOffset NextAttemptUtc; }
        private readonly Dictionary<string, PollBackoff> _pollBackoff = new(StringComparer.Ordinal);
        private const int MaxPollBackoffCycles = 16;
        // Persistent IMAP connections reused across background poll cycles to avoid a
        // fresh TLS handshake + LOGIN every interval. Poll-only: CheckNewMailAsync is
        // serialised by _isPollingMail, and the UI keeps using its own ephemeral clients,
        // so these are never accessed concurrently (MailKit clients are not thread-safe).
        private readonly Dictionary<string, ImapClient> _pollImapClients = new(StringComparer.Ordinal);
        private MailPersistentCache? _persistentCache;
        private bool _persistentCacheLoaded;
        private readonly object _persistentCacheSaveLock = new();
        private readonly object _mailCacheLock = new();
        private readonly SemaphoreSlim _persistentCacheWriteGate = new(1, 1);
        private readonly MailCacheRepository _cacheRepository = new();
        private readonly SemaphoreSlim _pendingMutationRetryGate = new(1, 1);
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _mutationGates = new(StringComparer.Ordinal);
        private CancellationTokenSource? _persistentCacheSaveCts;
        private long _persistentCacheVersion;
        private long _lastPersistedCacheVersion;
        private long _publishedCacheVersion;
        private readonly object _accountSaveQueueLock = new();
        private Task _accountSaveQueue = Task.CompletedTask;
        private readonly object _bodyCacheLock = new();
        private readonly Dictionary<string, WeakReference<MailItem>> _bodyCacheItems = new(StringComparer.Ordinal);
        private readonly Dictionary<string, BodyCacheMetadata> _bodyCacheMetadata = new(StringComparer.Ordinal);
        private long _bodyCacheAccessSequence;
        private string? _activeBodyCacheAccountId;
        private string? _protectedBodyCacheKey;
        private const int MaxBodyTextChars = 80_000;
        private const int MaxHtmlBodyChars = 160_000;
        private const long PerAccountMaxVolatileBodyCacheBytes = 1_000_000;
        private const long PerAccountTargetVolatileBodyCacheBytes = 750_000;
        private const long GlobalMaxVolatileBodyCacheBytes = 2_000_000;
        private const long GlobalTargetVolatileBodyCacheBytes = 1_500_000;
        private const int MaxConcurrentGoogleMessageMetadataRequests = 6;
        private const int PersistentCacheSaveDebounceMs = 1500;
        public event EventHandler<NewMailNotificationEventArgs>? NewMailArrived;
        internal event EventHandler<MailCachePublishedEventArgs>? CachePublished;

        private sealed class CacheEntry<T>
        {
            public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
            public T Value { get; set; } = default!;
        }

        private readonly record struct BodyCacheMetadata(string AccountId, long RetainedBytes, long AccessSequence);
        public readonly record struct MailBodyCacheAccountSize(string AccountId, int MessageCount, long RetainedBytes);

        // Lower/upper bounds for how many messages a single fetch returns. The upper
        // bound is the ceiling the "Load more" UI can grow a folder's window to; older
        // messages beyond it stay out until requested.
        public const int MinPageSize = 10;
        public const int MaxPageSize = 200;

        public int PageSize
        {
            get => ApplicationData.Current.LocalSettings.Values["MailPageSize"] as int? ?? 25;
            set => ApplicationData.Current.LocalSettings.Values["MailPageSize"] = value;
        }

        public bool MailPollingEnabled
        {
            get => ApplicationData.Current.LocalSettings.Values["MailPollingEnabled"] as bool? ?? true;
            set => ApplicationData.Current.LocalSettings.Values["MailPollingEnabled"] = value;
        }

        public int MailPollingIntervalMinutes
        {
            get => ApplicationData.Current.LocalSettings.Values["MailPollingIntervalMinutes"] as int? ?? 15;
            set => ApplicationData.Current.LocalSettings.Values["MailPollingIntervalMinutes"] = Math.Clamp(value, 1, 240);
        }

        public bool AutoMarkMailAsRead
        {
            get => ApplicationData.Current.LocalSettings.Values["AutoMarkMailAsRead"] as bool? ?? true;
            set => ApplicationData.Current.LocalSettings.Values["AutoMarkMailAsRead"] = value;
        }

        public IReadOnlyList<MailAccount> GetAccounts()
        {
            EnsureAccountsLoaded();
            return ApplyAccountOrder(_accounts);
        }

        public bool HasSetupCompleteAccounts()
        {
            EnsureAccountsLoaded();
            return _accounts.Any(account => account.IsSetupComplete);
        }

        public int GetPendingMutationCount(string? accountId = null)
        {
            EnsurePersistentCacheLoaded();
            lock (_mailCacheLock)
                return _persistentCache?.PendingMutations.Count(mutation => accountId == null || mutation.AccountId == accountId) ?? 0;
        }

        public void SaveMailAccountOrder(IEnumerable<string> accountIds)
        {
            EnsureAccountsLoaded();
            EnsurePersistentCacheLoaded();
            lock (_mailCacheLock)
            {
                if (_persistentCache == null) return;
                var knownIds = _accounts.Select(account => account.Id).ToHashSet(StringComparer.Ordinal);
                _persistentCache.AccountOrder = accountIds
                    .Where(id => knownIds.Contains(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
            }
            SavePersistentCache();
        }

        private sealed class MailProviderPage
        {
            public List<MailItem> Items { get; init; } = new();
            public MailCursor? NextCursor { get; init; }
            public bool HasMore => NextCursor != null;
        }

        public void SaveMailFolderOrder(string accountId, IEnumerable<string> folderIds)
        {
            EnsurePersistentCacheLoaded();
            lock (_mailCacheLock)
            {
                if (_persistentCache == null) return;
                _persistentCache.FolderOrder[accountId] = folderIds
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                if (_persistentCache.Folders.TryGetValue(accountId, out var folders))
                {
                    var orderedFolders = ApplyFolderOrder(accountId, folders);
                    _persistentCache.Folders[accountId] = orderedFolders;
                    var fetchedAt = _persistentCache.FolderFetchedUtcTicks.TryGetValue(accountId, out var fetchedUtcTicks)
                        ? new DateTimeOffset(fetchedUtcTicks, TimeSpan.Zero)
                        : DateTimeOffset.MinValue;
                    _folderCache[accountId] = new CacheEntry<List<MailFolder>> { CreatedAt = fetchedAt, Value = orderedFolders };
                }
            }

            SavePersistentCache();
        }

        public bool RemoveAccount(string accountId)
        {
            EnsureAccountsLoaded();

            var account = _accounts.FirstOrDefault(a => a.Id == accountId);
            if (account == null) return false;

            _accounts.Remove(account);
            if (account.Kind == MailAccountKind.Imap)
                RemoveImapPassword(account.Id);

            DisconnectPollImapClient(account.Id);
            ClearAccountBackoff(account.Id);
            ClearAccountCache(account.Id);
            RemoveKnownUnreadForAccount(account.Id);
            SaveAccounts();
            UpdateMailPollingSettings();
            return true;
        }

        private async Task SafeInitialMailCheckAsync()
        {
            try { await CheckNewMailAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Initial mail check failed: {ex.Message}"); }
        }

        public void StartMailPolling()
        {
            StartPendingMutationRetryScheduler();
            if (!MailPollingEnabled || !HasSetupCompleteAccounts())
            {
                StopMailPolling();
                return;
            }

            QueueInitialMailCheckIfDue(TimeSpan.FromMinutes(Math.Max(1, MailPollingIntervalMinutes)));
        }

        public async Task RunScheduledPollIfDueAsync(DateTimeOffset now)
        {
            if (!BackgroundRefreshSchedulePolicy.IsMailPollDue(
                now,
                _lastMailPollStartedUtc,
                MailPollingIntervalMinutes,
                MailPollingEnabled,
                HasSetupCompleteAccounts())) return;
            await CheckNewMailAsync();
        }

        private void QueueInitialMailCheckIfDue(TimeSpan interval)
        {
            var now = DateTimeOffset.UtcNow;
            var minGap = TimeSpan.FromTicks(Math.Min(
                TimeSpan.FromMinutes(2).Ticks,
                Math.Max(TimeSpan.FromSeconds(30).Ticks, interval.Ticks / 2)));

            if (now - _lastMailPollStartedUtc < TimeSpan.FromSeconds(30)) return;
            if (now - _lastMailPollCompletedUtc < minGap) return;

            _ = SafeInitialMailCheckAsync();
        }

        public void StopMailPolling()
        {
            DisconnectAllPollImapClients();
        }

        public void StopPendingMutationRetryScheduler()
        {
            _pendingMutationRetryStopped = true;
            _pendingMutationTimer?.Stop();
            _pendingMutationTimer = null;
        }

        private void StartPendingMutationRetryScheduler()
        {
            _pendingMutationRetryStopped = false;
            EnsureAccountsLoaded();
            EnsurePersistentCacheLoaded();
            ScheduleNextPendingMutationRetry();
        }

        private void ScheduleNextPendingMutationRetry()
        {
            if (_pendingMutationRetryStopped) return;

            long? nextAttemptUtcTicks;
            var setupAccountIds = _accounts
                .Where(account => account.IsSetupComplete)
                .Select(account => account.Id)
                .ToHashSet(StringComparer.Ordinal);
            lock (_mailCacheLock)
            {
                nextAttemptUtcTicks = _persistentCache?.PendingMutations
                    .Where(mutation => setupAccountIds.Contains(mutation.AccountId))
                    .Select(mutation => (long?)mutation.NextAttemptUtcTicks)
                    .Min();
            }

            if (!nextAttemptUtcTicks.HasValue)
            {
                _pendingMutationTimer?.Stop();
                _pendingMutationTimer = null;
                return;
            }

            var delay = MailMutationRetryPolicy.GetSchedulerDelay(nextAttemptUtcTicks.Value, DateTimeOffset.UtcNow);

            _pendingMutationTimer ??= new DispatcherTimer();
            _pendingMutationTimer.Stop();
            _pendingMutationTimer.Interval = delay;
            _pendingMutationTimer.Tick -= PendingMutationTimer_Tick;
            _pendingMutationTimer.Tick += PendingMutationTimer_Tick;
            _pendingMutationTimer.Start();
        }

        private async void PendingMutationTimer_Tick(object? sender, object e)
        {
            _pendingMutationTimer?.Stop();
            try
            {
                EnsureAccountsLoaded();
                foreach (var account in _accounts.Where(account => account.IsSetupComplete).ToList())
                    await RetryPendingMutationsAsync(account);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Pending mail mutation retry failed: {ex.Message}");
            }
            finally
            {
                ScheduleNextPendingMutationRetry();
            }
        }

        public void UpdateMailPollingSettings()
        {
            if (MailPollingEnabled && HasSetupCompleteAccounts())
                StartMailPolling();
            else
                StopMailPolling();
        }

        public async Task<MailAccount> AddOutlookAccountAsync(CancellationToken cancellationToken = default)
        {
            EnsureAccountsLoaded();
            await EnsureOutlookMailReadAuthorizedAsync(cancellationToken);
            if (_outlookClient == null)
                throw new InvalidOperationException("Outlook authorization failed.");

            var me = await _outlookClient.Me.GetAsync(request =>
            {
                request.QueryParameters.Select = new[] { "displayName", "mail", "userPrincipalName" };
            }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var address = me?.Mail ?? me?.UserPrincipalName ?? "";
            var existing = _accounts.FirstOrDefault(a =>
                a.Kind == MailAccountKind.Outlook &&
                string.Equals(a.Address, address, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                await EnsureProviderAgendaAccountAsync("Microsoft");
                return existing;
            }

            var account = new MailAccount
            {
                Kind = MailAccountKind.Outlook,
                DisplayName = string.IsNullOrWhiteSpace(me?.DisplayName) ? "Outlook" : me.DisplayName,
                Address = address,
                IsSetupComplete = true
            };

            _accounts.Add(account);
            SaveAccounts();
            UpdateMailPollingSettings();
            await EnsureProviderAgendaAccountAsync("Microsoft");
            return account;
        }

        public MailAccount AddDraftAccount(MailAccountKind kind, string address)
        {
            EnsureAccountsLoaded();

            var account = new MailAccount
            {
                Kind = kind,
                DisplayName = kind == MailAccountKind.Google ? "Gmail" : "IMAP",
                Address = address.Trim(),
                IsSetupComplete = false
            };

            _accounts.Add(account);
            SaveAccounts();
            return account;
        }

        public async Task<MailAccount> AddImapAccountAsync(
            string displayName,
            string address,
            string userName,
            string password,
            string host,
            int port,
            bool useSsl,
            string smtpHost,
            int smtpPort,
            bool smtpUseSsl,
            string smtpUserName,
            CancellationToken cancellationToken = default)
        {
            EnsureAccountsLoaded();

            var account = new MailAccount
            {
                Kind = MailAccountKind.Imap,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? "IMAP" : displayName.Trim(),
                Address = address.Trim(),
                ImapUserName = string.IsNullOrWhiteSpace(userName) ? address.Trim() : userName.Trim(),
                ImapHost = host.Trim(),
                ImapPort = port,
                ImapUseSsl = useSsl,
                SmtpHost = smtpHost.Trim(),
                SmtpPort = smtpPort,
                SmtpUseSsl = smtpUseSsl,
                SmtpUserName = string.IsNullOrWhiteSpace(smtpUserName) ? userName.Trim() : smtpUserName.Trim(),
                IsSetupComplete = true
            };

            await TestImapConnectionAsync(account, password, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var existing = _accounts.FirstOrDefault(a =>
                a.Kind == MailAccountKind.Imap &&
                string.Equals(a.Address, account.Address, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.ImapHost, account.ImapHost, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.DisplayName = account.DisplayName;
                existing.ImapUserName = account.ImapUserName;
                existing.ImapPort = account.ImapPort;
                existing.ImapUseSsl = account.ImapUseSsl;
                existing.SmtpHost = account.SmtpHost;
                existing.SmtpPort = account.SmtpPort;
                existing.SmtpUseSsl = account.SmtpUseSsl;
                existing.SmtpUserName = account.SmtpUserName;
                existing.IsSetupComplete = true;
                SaveImapPassword(existing.Id, password);
                SaveAccounts();
                UpdateMailPollingSettings();
                return existing;
            }

            _accounts.Add(account);
            SaveImapPassword(account.Id, password);
            SaveAccounts();
            UpdateMailPollingSettings();
            return account;
        }

        public async Task<MailAccount> AddGoogleAccountAsync(CancellationToken cancellationToken = default)
        {
            EnsureAccountsLoaded();
            var gmail = await EnsureGoogleMailReadAuthorizedAsync(cancellationToken);
            var profile = await gmail.Users.GetProfile("me").ExecuteAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var address = profile?.EmailAddress ?? "";

            var existing = _accounts.FirstOrDefault(a =>
                a.Kind == MailAccountKind.Google &&
                string.Equals(a.Address, address, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                await EnsureProviderAgendaAccountAsync("Google");
                return existing;
            }

            var account = new MailAccount
            {
                Kind = MailAccountKind.Google,
                DisplayName = "Gmail",
                Address = address,
                IsSetupComplete = true
            };

            _accounts.Add(account);
            SaveAccounts();
            UpdateMailPollingSettings();
            await EnsureProviderAgendaAccountAsync("Google");
            return account;
        }

        public async Task<List<MailFolder>> FetchFoldersAsync(MailAccount account, bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            string cacheKey = account.Id;
            if (!forceRefresh && TryGetCachedFolders(cacheKey, out var cachedFolders))
                return cachedFolders;

            List<MailFolder> folders;
            if (account.Kind == MailAccountKind.Google && account.IsSetupComplete)
            {
                folders = await FetchGoogleFoldersAsync(account, cancellationToken);
            }
            else if (account.Kind == MailAccountKind.Imap && account.IsSetupComplete)
            {
                folders = await FetchImapFoldersAsync(account, cancellationToken);
            }
            else if (account.Kind != MailAccountKind.Outlook || !account.IsSetupComplete)
            {
                folders = new List<MailFolder>
                {
                    new MailFolder
                    {
                        AccountId = account.Id,
                        Id = "setup",
                        DisplayName = _loader.GetStringOrDefault("TextFolderPlaceholder") ?? "Complete setup to view folders",
                        IsPlaceholder = true
                    }
                };
            }
            else
            {
                await EnsureOutlookMailReadAuthorizedAsync(cancellationToken);
                if (_outlookClient == null) return new List<MailFolder>();

                var response = await _outlookClient.Me.MailFolders.GetAsync(request =>
                {
                    request.QueryParameters.Top = 50;
                    request.QueryParameters.Select = new[] { "id", "displayName", "unreadItemCount" };
                }, cancellationToken);

                folders = response?.Value?
                    .Where(folder => folder != null && !string.IsNullOrWhiteSpace(folder.Id))
                    .Select(folder => new MailFolder
                    {
                        AccountId = account.Id,
                        Id = folder.Id ?? "",
                        DisplayName = folder.DisplayName ?? folder.Id ?? "",
                        UnreadCount = folder.UnreadItemCount
                    })
                    .OrderByDescending(folder => string.Equals(folder.DisplayName, "Inbox", StringComparison.OrdinalIgnoreCase))
                    .ThenBy(folder => folder.DisplayName)
                    .ToList() ?? new List<MailFolder>();
            }

            folders = ApplyFolderOrder(cacheKey, folders);
            UpdateFolderWindow(cacheKey, folders);
            return folders;
        }

        public async Task<List<OutlookMoveDestination>> FetchOutlookMoveDestinationsAsync(
            MailAccount account,
            string currentFolderId,
            CancellationToken cancellationToken = default)
        {
            if (account.Kind != MailAccountKind.Outlook)
                return new List<OutlookMoveDestination>();

            await EnsureOutlookMailWriteAuthorizedAsync(cancellationToken);
            if (_outlookClient == null) return new List<OutlookMoveDestination>();

            var result = new List<OutlookMoveDestination>();
            await FetchOutlookFolderPageAsync(null, "", currentFolderId, result, cancellationToken);
            return result.OrderBy(folder => folder.Breadcrumb, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private async Task FetchOutlookFolderPageAsync(
            string? parentFolderId,
            string parentBreadcrumb,
            string currentFolderId,
            List<OutlookMoveDestination> result,
            CancellationToken cancellationToken)
        {
            string? nextLink = null;
            do
            {
                Microsoft.Graph.Models.MailFolderCollectionResponse? response;
                if (nextLink != null)
                {
                    if (!MailPaginationPolicy.IsAllowedGraphNextLink(nextLink))
                        throw new InvalidOperationException("Mail folder continuation URL is invalid.");
                    response = parentFolderId == null
                        ? await _outlookClient!.Me.MailFolders.WithUrl(nextLink).GetAsync(cancellationToken: cancellationToken)
                        : await _outlookClient!.Me.MailFolders[parentFolderId].ChildFolders.WithUrl(nextLink).GetAsync(cancellationToken: cancellationToken);
                }
                else if (parentFolderId == null)
                {
                    response = await _outlookClient!.Me.MailFolders.GetAsync(request =>
                    {
                        request.QueryParameters.Top = 100;
                        request.QueryParameters.Select = new[] { "id", "displayName", "childFolderCount" };
                    }, cancellationToken);
                }
                else
                {
                    response = await _outlookClient!.Me.MailFolders[parentFolderId].ChildFolders.GetAsync(request =>
                    {
                        request.QueryParameters.Top = 100;
                        request.QueryParameters.Select = new[] { "id", "displayName", "childFolderCount" };
                    }, cancellationToken);
                }

                foreach (var folder in response?.Value ?? Enumerable.Empty<Microsoft.Graph.Models.MailFolder>())
                {
                    if (string.IsNullOrWhiteSpace(folder.Id)) continue;
                    string name = folder.DisplayName ?? folder.Id;
                    string breadcrumb = string.IsNullOrWhiteSpace(parentBreadcrumb) ? name : $"{parentBreadcrumb} / {name}";
                    if (!string.Equals(folder.Id, currentFolderId, StringComparison.Ordinal))
                        result.Add(new OutlookMoveDestination { Id = folder.Id, DisplayName = name, Breadcrumb = breadcrumb });
                    if (folder.ChildFolderCount > 0)
                        await FetchOutlookFolderPageAsync(folder.Id, breadcrumb, currentFolderId, result, cancellationToken);
                }
                nextLink = response?.OdataNextLink;
            }
            while (!string.IsNullOrWhiteSpace(nextLink));
        }

        public async Task<MailMoveResult> ArchiveOutlookMessageAsync(MailAccount account, MailItem item, CancellationToken cancellationToken = default)
        {
            string destinationId = await ResolveOutlookWellKnownFolderIdAsync("archive", cancellationToken)
                ?? throw new InvalidOperationException("The Outlook archive folder is unavailable.");
            return await MoveOutlookMessageAsync(account, item, destinationId, cancellationToken);
        }

        public async Task<MailMoveResult> TrashOutlookMessageAsync(MailAccount account, MailItem item, CancellationToken cancellationToken = default)
        {
            string destinationId = await ResolveOutlookWellKnownFolderIdAsync("deleteditems", cancellationToken)
                ?? throw new InvalidOperationException("The Outlook Deleted Items folder is unavailable.");
            return await MoveOutlookMessageAsync(account, item, destinationId, cancellationToken);
        }

        public async Task<MailMoveResult> MoveOutlookMessageAsync(
            MailAccount account,
            MailItem item,
            string destinationFolderId,
            CancellationToken cancellationToken = default)
        {
            if (account.Kind != MailAccountKind.Outlook || !MailMutationCapabilityPolicy.For(account.Kind).Move)
                throw new NotSupportedException("Only Outlook message moves are supported.");
            if (string.IsNullOrWhiteSpace(destinationFolderId) || string.Equals(item.FolderId, destinationFolderId, StringComparison.Ordinal))
                throw new InvalidOperationException("Choose a different destination folder.");

            await EnsureOutlookMailWriteAuthorizedAsync(cancellationToken);
            if (_outlookClient == null) throw new InvalidOperationException("Outlook authorization failed.");

            string sourceFolderId = item.FolderId;
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            GraphMessage? moved;
            try
            {
                moved = await _outlookClient.Me.Messages[item.Id].Move.PostAsync(
                    new MovePostRequestBody { DestinationId = destinationFolderId },
                    requestConfiguration: null,
                    cancellationToken: operationCts.Token);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsAmbiguousMoveFailure(ex, timeoutCts.IsCancellationRequested))
            {
                RemoveBodyCacheIdentity(item);
                InvalidateMoveWindows(account.Id, sourceFolderId, destinationFolderId, item.IsRead, adjustCounts: false);
                throw new MailMoveOutcomeUnknownException(ex);
            }

            if (string.IsNullOrWhiteSpace(moved?.Id) || string.IsNullOrWhiteSpace(moved.ParentFolderId))
            {
                RemoveBodyCacheIdentity(item);
                InvalidateMoveWindows(account.Id, sourceFolderId, destinationFolderId, item.IsRead, adjustCounts: false);
                throw new MailMoveOutcomeUnknownException(new InvalidOperationException("Microsoft Graph did not return the moved message identity."));
            }

            var authoritativeItem = CloneMailItem(item, includeBodies: false);
            authoritativeItem.Id = moved.Id;
            authoritativeItem.FolderId = moved.ParentFolderId;
            RemoveBodyCacheIdentity(item);
            InvalidateMoveWindows(account.Id, sourceFolderId, moved.ParentFolderId, item.IsRead, adjustCounts: true);
            return new MailMoveResult(authoritativeItem, sourceFolderId, moved.ParentFolderId);
        }

        public async Task<List<ImapMoveDestination>> FetchImapMoveDestinationsAsync(
            MailAccount account,
            string currentFolderFullName,
            CancellationToken cancellationToken = default)
        {
            if (account.Kind != MailAccountKind.Imap) throw new NotSupportedException("Only IMAP folders are supported.");
            using var client = new ImapClient();
            await ConnectImapAsync(client, account, GetImapPassword(account.Id), cancellationToken);
            EnsureSafeImapMoveCapabilities(client);

            var result = new List<ImapMoveDestination>();
            foreach (var folderNamespace in client.PersonalNamespaces)
            {
                var folders = await client.GetFoldersAsync(folderNamespace, cancellationToken: cancellationToken);
                foreach (var folder in folders)
                {
                    bool noSelect = (folder.Attributes & FolderAttributes.NoSelect) != 0;
                    bool nonExistent = (folder.Attributes & FolderAttributes.NonExistent) != 0;
                    if (!ImapMovePolicy.IsSelectableDestination(folder.FullName, currentFolderFullName, noSelect, nonExistent)) continue;
                    result.Add(new ImapMoveDestination
                    {
                        FullName = folder.FullName,
                        Breadcrumb = BuildImapFolderBreadcrumb(folder)
                    });
                }
            }
            await client.DisconnectAsync(true, cancellationToken);
            return result
                .GroupBy(folder => folder.FullName, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(folder => folder.Breadcrumb, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public Task<ImapMoveResult> ArchiveImapMessageAsync(MailAccount account, MailItem item, CancellationToken cancellationToken = default)
            => MoveImapMessageInternalAsync(account, item, null, MailKit.SpecialFolder.Archive, cancellationToken);

        public Task<ImapMoveResult> TrashImapMessageAsync(MailAccount account, MailItem item, CancellationToken cancellationToken = default)
            => MoveImapMessageInternalAsync(account, item, null, MailKit.SpecialFolder.Trash, cancellationToken);

        public Task<ImapMoveResult> MoveImapMessageAsync(MailAccount account, MailItem item, string destinationFolderFullName, CancellationToken cancellationToken = default)
            => MoveImapMessageInternalAsync(account, item, destinationFolderFullName, null, cancellationToken);

        public Task<ImapMoveResult> UndoImapMoveAsync(MailAccount account, ImapMoveResult forward, CancellationToken cancellationToken = default)
            => MoveImapMessageInternalAsync(account, forward.Item, forward.Source.FolderFullName, null, cancellationToken);

        private async Task<ImapMoveResult> MoveImapMessageInternalAsync(
            MailAccount account,
            MailItem item,
            string? destinationFolderFullName,
            MailKit.SpecialFolder? specialFolder,
            CancellationToken cancellationToken)
        {
            if (account.Kind != MailAccountKind.Imap || !string.Equals(account.Id, item.AccountId, StringComparison.Ordinal))
                throw new InvalidOperationException("The IMAP message does not belong to the selected account.");
            if (!uint.TryParse(item.Id, out uint sourceUid) || item.ImapUidValidity is not > 0)
                throw new InvalidOperationException("The IMAP message identity is invalid.");

            var gates = new[] { MailMutationKind.SetReadState, MailMutationKind.SetFlagged }
                .Select(kind => _mutationGates.GetOrAdd(GetMutationKey(account.Kind, account.Id, item.FolderId, item.Id, item.ImapUidValidity, kind), _ => new SemaphoreSlim(1, 1)))
                .ToList();
            var acquiredGates = new List<SemaphoreSlim>();
            try
            {
                foreach (var gate in gates)
                {
                    await gate.WaitAsync(cancellationToken);
                    acquiredGates.Add(gate);
                }
                EnsurePersistentCacheLoaded();
                lock (_mailCacheLock)
                {
                    if (_persistentCache?.PendingMutations.Any(mutation =>
                            mutation.AccountId == account.Id && mutation.FolderId == item.FolderId && mutation.MessageId == item.Id) == true)
                        throw new InvalidOperationException("Sync pending read or flag changes before moving this IMAP message.");
                }
                return await MoveImapMessageCoreAsync(account, item, sourceUid, destinationFolderFullName, specialFolder, cancellationToken);
            }
            finally
            {
                foreach (var gate in acquiredGates)
                    gate.Release();
            }
        }

        private async Task<ImapMoveResult> MoveImapMessageCoreAsync(
            MailAccount account,
            MailItem item,
            uint sourceUid,
            string? destinationFolderFullName,
            MailKit.SpecialFolder? specialFolder,
            CancellationToken cancellationToken)
        {

            using var client = new ImapClient();
            await ConnectImapAsync(client, account, GetImapPassword(account.Id), cancellationToken);
            EnsureSafeImapMoveCapabilities(client);

            var source = await client.GetFolderAsync(item.FolderId, cancellationToken);
            IMailFolder? destination = specialFolder.HasValue
                ? client.GetFolder(specialFolder.Value)
                : await client.GetFolderAsync(destinationFolderFullName!, cancellationToken);
            if (destination == null ||
                !ImapMovePolicy.IsSelectableDestination(destination.FullName, source.FullName,
                    (destination.Attributes & FolderAttributes.NoSelect) != 0,
                    (destination.Attributes & FolderAttributes.NonExistent) != 0))
                throw new InvalidOperationException("The requested IMAP destination folder is unavailable.");

            await source.OpenAsync(FolderAccess.ReadWrite, cancellationToken);
            if (!MailPaginationPolicy.IsValidImapMutation(item.ImapUidValidity, source.UidValidity, sourceUid))
            {
                InvalidateMoveWindows(account.Id, item.FolderId, destination.FullName, item.IsRead, adjustCounts: false);
                throw new InvalidOperationException("The IMAP message identity is no longer valid for this folder.");
            }

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            UniqueId? movedUid;
            try
            {
                movedUid = await source.MoveToAsync(new UniqueId(sourceUid), destination, operationCts.Token);
            }
            catch (OperationCanceledException ex)
            {
                RemoveBodyCacheIdentity(item);
                InvalidateMoveWindows(account.Id, source.FullName, destination.FullName, item.IsRead, adjustCounts: false);
                throw new ImapMoveOutcomeUnknownException(ex);
            }
            catch (Exception ex) when (IsAmbiguousImapMoveFailure(ex))
            {
                RemoveBodyCacheIdentity(item);
                InvalidateMoveWindows(account.Id, source.FullName, destination.FullName, item.IsRead, adjustCounts: false);
                throw new ImapMoveOutcomeUnknownException(ex);
            }

            if (!movedUid.HasValue || !ImapMovePolicy.HasAuthoritativeIdentity(movedUid.Value.Id, movedUid.Value.Validity))
            {
                RemoveBodyCacheIdentity(item);
                InvalidateMoveWindows(account.Id, source.FullName, destination.FullName, item.IsRead, adjustCounts: false);
                throw new ImapMoveIdentityUnavailableException();
            }

            var authoritativeItem = CloneMailItem(item, includeBodies: false);
            authoritativeItem.FolderId = destination.FullName;
            authoritativeItem.Id = movedUid.Value.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            authoritativeItem.ImapUidValidity = movedUid.Value.Validity;
            RemoveBodyCacheIdentity(item);
            InvalidateMoveWindows(account.Id, source.FullName, destination.FullName, item.IsRead, adjustCounts: true);
            try { await client.DisconnectAsync(true, CancellationToken.None); } catch { }
            return new ImapMoveResult(authoritativeItem,
                new ImapMessageIdentity(source.FullName, source.UidValidity, sourceUid),
                new ImapMessageIdentity(destination.FullName, movedUid.Value.Validity, movedUid.Value.Id));
        }

        private static void EnsureSafeImapMoveCapabilities(ImapClient client)
        {
            bool nativeMove = (client.Capabilities & ImapCapabilities.Move) != 0;
            bool uidPlus = (client.Capabilities & ImapCapabilities.UidPlus) != 0;
            if (!ImapMovePolicy.SupportsSafeMove(nativeMove, uidPlus))
                throw new NotSupportedException("This IMAP server does not support safe online moves with authoritative destination identity.");
        }

        private static string BuildImapFolderBreadcrumb(IMailFolder folder)
            => folder.DirectorySeparator == '\0'
                ? folder.FullName
                : folder.FullName.Replace(folder.DirectorySeparator.ToString(), " / ", StringComparison.Ordinal);

        private static bool IsAmbiguousImapMoveFailure(Exception exception)
            => exception is IOException or ServiceNotConnectedException or ProtocolException;

        public async Task<List<GmailLabelDestination>> FetchGmailMoveDestinationsAsync(
            MailAccount account,
            string currentLabelId,
            CancellationToken cancellationToken = default)
        {
            if (account.Kind != MailAccountKind.Google) throw new NotSupportedException("Only Gmail labels are supported.");
            var gmail = await EnsureGoogleMailModifyAuthorizedAsync(cancellationToken);
            var response = await gmail.Users.Labels.List("me").ExecuteAsync(cancellationToken);
            return GmailLabelMutationPolicy.Destinations(
                response?.Labels?.Where(label => label != null).Select(label => new GmailLabelDestination
                {
                    Id = label.Id ?? "",
                    DisplayName = label.Name ?? label.Id ?? "",
                    IsUserLabel = string.Equals(label.Type, "user", StringComparison.OrdinalIgnoreCase)
                }) ?? Enumerable.Empty<GmailLabelDestination>(),
                currentLabelId).ToList();
        }

        public Task<GmailLabelMutationResult> ArchiveGmailMessageAsync(MailAccount account, MailItem item, CancellationToken cancellationToken = default)
        {
            if (!string.Equals(item.FolderId, "INBOX", StringComparison.Ordinal))
                throw new InvalidOperationException("Gmail archive is available from Inbox only.");
            return MutateGmailMessageAsync(account, item, GmailOnlineActionKind.Archive, null, cancellationToken);
        }

        public Task<GmailLabelMutationResult> MoveGmailMessageToLabelAsync(MailAccount account, MailItem item, string destinationLabelId, CancellationToken cancellationToken = default)
            => MutateGmailMessageAsync(account, item, GmailOnlineActionKind.MoveToLabel, destinationLabelId, cancellationToken);

        public Task<GmailLabelMutationResult> TrashGmailMessageAsync(MailAccount account, MailItem item, CancellationToken cancellationToken = default)
            => MutateGmailMessageAsync(account, item, GmailOnlineActionKind.Trash, null, cancellationToken);

        private async Task<GmailLabelMutationResult> MutateGmailMessageAsync(
            MailAccount account,
            MailItem item,
            GmailOnlineActionKind kind,
            string? destinationLabelId,
            CancellationToken cancellationToken)
        {
            if (account.Kind != MailAccountKind.Google) throw new NotSupportedException("Only Gmail label actions are supported.");
            if (!string.Equals(account.Id, item.AccountId, StringComparison.Ordinal))
                throw new InvalidOperationException("The Gmail message does not belong to the selected account.");
            if (kind == GmailOnlineActionKind.Trash && string.Equals(item.FolderId, "TRASH", StringComparison.Ordinal))
                throw new InvalidOperationException("The message is already in trash.");

            var gmail = await EnsureGoogleMailModifyAuthorizedAsync(cancellationToken);
            var labelsResponse = await gmail.Users.Labels.List("me").ExecuteAsync(cancellationToken);
            var labels = labelsResponse?.Labels ?? new List<Label>();
            bool sourceIsUser = labels.Any(label => string.Equals(label.Id, item.FolderId, StringComparison.Ordinal) &&
                                                   string.Equals(label.Type, "user", StringComparison.OrdinalIgnoreCase));
            if (kind == GmailOnlineActionKind.MoveToLabel)
            {
                bool destinationIsUser = labels.Any(label => string.Equals(label.Id, destinationLabelId, StringComparison.Ordinal) &&
                                                            string.Equals(label.Type, "user", StringComparison.OrdinalIgnoreCase));
                if (!destinationIsUser || string.Equals(destinationLabelId, item.FolderId, StringComparison.Ordinal))
                    throw new InvalidOperationException("Choose a different custom Gmail label.");
                if (!GmailLabelMutationPolicy.CanRemoveSource(item.FolderId, sourceIsUser))
                    throw new InvalidOperationException("Messages cannot be moved from this Gmail system label.");
            }

            var beforeMessageRequest = gmail.Users.Messages.Get("me", item.Id);
            beforeMessageRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Minimal;
            var beforeMessage = await beforeMessageRequest.ExecuteAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(beforeMessage?.Id) || !string.Equals(beforeMessage.Id, item.Id, StringComparison.Ordinal))
                throw new InvalidOperationException("Gmail did not return the expected message before the action.");
            var before = (beforeMessage?.LabelIds ?? new List<string>()).Distinct(StringComparer.Ordinal).ToList();
            var requestedAdds = kind == GmailOnlineActionKind.MoveToLabel ? new[] { destinationLabelId! } : Array.Empty<string>();
            var requestedRemoves = kind switch
            {
                GmailOnlineActionKind.Archive => new[] { "INBOX" },
                GmailOnlineActionKind.MoveToLabel => new[] { item.FolderId },
                _ => Array.Empty<string>()
            };
            var actualAdds = GmailLabelMutationPolicy.ActualAddedLabels(before, requestedAdds);
            var actualRemoves = GmailLabelMutationPolicy.ActualRemovedLabels(before, requestedRemoves);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            GmailMessage? afterMessage;
            try
            {
                if (kind == GmailOnlineActionKind.Trash)
                {
                    afterMessage = await gmail.Users.Messages.Trash("me", item.Id).ExecuteAsync(operationCts.Token);
                }
                else
                {
                    afterMessage = await gmail.Users.Messages.Modify(new ModifyMessageRequest
                    {
                        AddLabelIds = requestedAdds.Length == 0 ? null : requestedAdds.ToList(),
                        RemoveLabelIds = requestedRemoves.Length == 0 ? null : requestedRemoves.ToList()
                    }, "me", item.Id).ExecuteAsync(operationCts.Token);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                InvalidateAmbiguousGmailAction(account.Id, item, requestedAdds.Concat(requestedRemoves).Append("TRASH"));
                throw;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsAmbiguousGmailActionFailure(ex, timeoutCts.IsCancellationRequested))
            {
                InvalidateAmbiguousGmailAction(account.Id, item, requestedAdds.Concat(requestedRemoves).Append("TRASH"));
                throw new GmailActionOutcomeUnknownException(ex);
            }

            if (string.IsNullOrWhiteSpace(afterMessage?.Id) || !string.Equals(afterMessage.Id, item.Id, StringComparison.Ordinal))
            {
                InvalidateAmbiguousGmailAction(account.Id, item, requestedAdds.Concat(requestedRemoves).Append("TRASH"));
                throw new GmailActionOutcomeUnknownException(new InvalidOperationException("Gmail did not return the expected message identity."));
            }

            var after = (afterMessage.LabelIds ?? new List<string>()).Distinct(StringComparer.Ordinal).ToList();
            CommitConfirmedGmailAction(account.Id, item, before, after);
            return new GmailLabelMutationResult(CloneMailItem(item, includeBodies: false), kind, item.FolderId,
                destinationLabelId, actualAdds, actualRemoves, before, after,
                GmailLabelMutationPolicy.CanRemoveSource(item.FolderId, sourceIsUser),
                kind == GmailOnlineActionKind.Trash || !after.Contains(item.FolderId, StringComparer.Ordinal));
        }

        public async Task<GmailLabelMutationResult> UndoGmailLabelMutationAsync(
            MailAccount account,
            GmailLabelMutationResult forward,
            CancellationToken cancellationToken = default)
        {
            if (account.Kind != MailAccountKind.Google || !string.Equals(account.Id, forward.Item.AccountId, StringComparison.Ordinal))
                throw new InvalidOperationException("The Gmail undo does not belong to the selected account.");
            var gmail = await EnsureGoogleMailModifyAuthorizedAsync(cancellationToken);
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            GmailMessage? restored;
            bool untrashCompleted = false;
            try
            {
                if (forward.Kind == GmailOnlineActionKind.Trash)
                {
                    restored = await gmail.Users.Messages.Untrash("me", forward.Item.Id).ExecuteAsync(operationCts.Token);
                    untrashCompleted = true;
                    if (forward.CanRestoreSourceLabel &&
                        forward.BeforeLabelIds.Contains(forward.SourceLabelId, StringComparer.Ordinal) &&
                        restored?.LabelIds?.Contains(forward.SourceLabelId, StringComparer.Ordinal) != true)
                    {
                        restored = await gmail.Users.Messages.Modify(new ModifyMessageRequest
                        {
                            AddLabelIds = new List<string> { forward.SourceLabelId }
                        }, "me", forward.Item.Id).ExecuteAsync(operationCts.Token);
                    }
                }
                else
                {
                    restored = await gmail.Users.Messages.Modify(new ModifyMessageRequest
                    {
                        AddLabelIds = forward.RemovedLabelIds.Count == 0 ? null : forward.RemovedLabelIds.ToList(),
                        RemoveLabelIds = forward.AddedLabelIds.Count == 0 ? null : forward.AddedLabelIds.ToList()
                    }, "me", forward.Item.Id).ExecuteAsync(operationCts.Token);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                InvalidateAmbiguousGmailAction(account.Id, forward.Item,
                    forward.AddedLabelIds.Concat(forward.RemovedLabelIds).Concat(new[] { forward.DestinationLabelId, "TRASH" }).OfType<string>());
                throw;
            }
            catch (Exception ex) when (untrashCompleted)
            {
                InvalidateAmbiguousGmailAction(account.Id, forward.Item,
                    forward.AddedLabelIds.Concat(forward.RemovedLabelIds).Concat(new[] { forward.DestinationLabelId, "TRASH" }).OfType<string>());
                throw new GmailActionOutcomeUnknownException(ex);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsAmbiguousGmailActionFailure(ex, timeoutCts.IsCancellationRequested))
            {
                InvalidateAmbiguousGmailAction(account.Id, forward.Item,
                    forward.AddedLabelIds.Concat(forward.RemovedLabelIds).Concat(new[] { forward.DestinationLabelId, "TRASH" }).OfType<string>());
                throw new GmailActionOutcomeUnknownException(ex);
            }

            if (string.IsNullOrWhiteSpace(restored?.Id) || !string.Equals(restored.Id, forward.Item.Id, StringComparison.Ordinal))
            {
                InvalidateAmbiguousGmailAction(account.Id, forward.Item,
                    forward.AddedLabelIds.Concat(forward.RemovedLabelIds).Concat(new[] { forward.DestinationLabelId, "TRASH" }).OfType<string>());
                throw new GmailActionOutcomeUnknownException(new InvalidOperationException("Gmail did not return the expected message identity."));
            }
            var after = (restored.LabelIds ?? new List<string>()).Distinct(StringComparer.Ordinal).ToList();
            CommitConfirmedGmailAction(account.Id, forward.Item, forward.AfterLabelIds, after);
            return forward with { BeforeLabelIds = forward.AfterLabelIds, AfterLabelIds = after, RemovedFromSource = !after.Contains(forward.SourceLabelId, StringComparer.Ordinal) };
        }

        private static bool IsAmbiguousGmailActionFailure(Exception exception, bool timedOut)
            => timedOut || exception is IOException ||
               exception is HttpRequestException httpException &&
               (!httpException.StatusCode.HasValue || (int)httpException.StatusCode.Value == 408 ||
                (int)httpException.StatusCode.Value == 429 || (int)httpException.StatusCode.Value >= 500) ||
               exception is Google.GoogleApiException googleException &&
               ((int)googleException.HttpStatusCode == 408 || (int)googleException.HttpStatusCode == 429 || (int)googleException.HttpStatusCode >= 500);

        private void InvalidateAmbiguousGmailAction(string accountId, MailItem item, IEnumerable<string> possiblyAffectedLabels)
        {
            RemoveBodyCacheIdentity(item);
            EnsurePersistentCacheLoaded();
            lock (_mailCacheLock)
            {
                _folderCache.Remove(accountId);
                _persistentCache?.Folders.Remove(accountId);
                _persistentCache?.FolderFetchedUtcTicks.Remove(accountId);
                foreach (string key in GmailLabelMutationPolicy.InvalidatedWindowKeys(accountId, possiblyAffectedLabels.Append(item.FolderId)))
                {
                    _messageCache.Remove(key);
                    _persistentCache?.Messages.Remove(key);
                    _persistentCache?.MessageFetchedUtcTicks.Remove(key);
                    _persistentCache?.MessageCursors.Remove(key);
                    _persistentCache?.MessageHasMore.Remove(key);
                }
            }
            SavePersistentCache();
        }

        private void CommitConfirmedGmailAction(string accountId, MailItem item, IReadOnlyList<string> before, IReadOnlyList<string> after)
        {
            RemoveBodyCacheIdentity(item);
            var affected = GmailLabelMutationPolicy.AffectedLabels(before, after, item.FolderId);
            EnsurePersistentCacheLoaded();
            lock (_mailCacheLock)
            {
                List<MailFolder>? persistentFolders = null;
                foreach (string key in GmailLabelMutationPolicy.InvalidatedWindowKeys(accountId, affected))
                {
                    _messageCache.Remove(key);
                    _persistentCache?.Messages.Remove(key);
                    _persistentCache?.MessageFetchedUtcTicks.Remove(key);
                    _persistentCache?.MessageCursors.Remove(key);
                    _persistentCache?.MessageHasMore.Remove(key);
                }
                if (_persistentCache?.Folders.TryGetValue(accountId, out var storedFolders) == true)
                    persistentFolders = storedFolders;
                bool messageIsRead = !before.Contains("UNREAD", StringComparer.Ordinal);
                AdjustGmailFolderCounts(persistentFolders, messageIsRead, before, after, affected);
                if (_folderCache.TryGetValue(accountId, out var cachedFolders) && !ReferenceEquals(cachedFolders.Value, persistentFolders))
                    AdjustGmailFolderCounts(cachedFolders.Value, messageIsRead, before, after, affected);
            }
            SavePersistentCache();
        }

        private static void AdjustGmailFolderCounts(List<MailFolder>? folders, bool isRead, IReadOnlyCollection<string> before, IReadOnlyCollection<string> after, IEnumerable<string> affected)
        {
            if (folders == null) return;
            foreach (string labelId in affected)
            {
                var folder = folders.FirstOrDefault(candidate => candidate.Id == labelId);
                if (folder != null)
                    folder.UnreadCount = GmailLabelMutationPolicy.AdjustUnreadCount(folder.UnreadCount, isRead, before.Contains(labelId), after.Contains(labelId));
            }
        }

        private static bool IsAmbiguousMoveFailure(Exception exception, bool timedOut)
            => timedOut || exception is HttpRequestException or IOException ||
               exception is Microsoft.Kiota.Abstractions.ApiException { ResponseStatusCode: 0 };

        private async Task<string?> ResolveOutlookWellKnownFolderIdAsync(string wellKnownName, CancellationToken cancellationToken)
        {
            await EnsureOutlookMailWriteAuthorizedAsync(cancellationToken);
            if (_outlookClient == null) return null;
            var folder = await _outlookClient.Me.MailFolders[wellKnownName].GetAsync(request =>
                request.QueryParameters.Select = new[] { "id" }, cancellationToken);
            return string.IsNullOrWhiteSpace(folder?.Id) ? null : folder.Id;
        }

        private void InvalidateMoveWindows(string accountId, string sourceFolderId, string destinationFolderId, bool isRead, bool adjustCounts)
        {
            EnsurePersistentCacheLoaded();
            lock (_mailCacheLock)
            {
                foreach (string key in MailMoveCachePolicy.InvalidatedWindowKeys(accountId, sourceFolderId, destinationFolderId))
                {
                    _messageCache.Remove(key);
                    _persistentCache?.Messages.Remove(key);
                    _persistentCache?.MessageFetchedUtcTicks.Remove(key);
                    _persistentCache?.MessageCursors.Remove(key);
                    _persistentCache?.MessageHasMore.Remove(key);
                }

                if (_persistentCache != null && adjustCounts && _persistentCache.Folders.TryGetValue(accountId, out var folders))
                {
                    var source = folders.FirstOrDefault(folder => folder.Id == sourceFolderId);
                    var destination = folders.FirstOrDefault(folder => folder.Id == destinationFolderId);
                    if (source != null) source.UnreadCount = MailMoveCachePolicy.AdjustSourceUnreadCount(source.UnreadCount, isRead);
                    if (destination != null) destination.UnreadCount = MailMoveCachePolicy.AdjustDestinationUnreadCount(destination.UnreadCount, isRead);
                }
                if (adjustCounts && _folderCache.TryGetValue(accountId, out var cachedFolders) &&
                    (_persistentCache == null ||
                     !_persistentCache.Folders.TryGetValue(accountId, out var persistentFolders) ||
                     !ReferenceEquals(cachedFolders.Value, persistentFolders)))
                {
                    var source = cachedFolders.Value.FirstOrDefault(folder => folder.Id == sourceFolderId);
                    var destination = cachedFolders.Value.FirstOrDefault(folder => folder.Id == destinationFolderId);
                    if (source != null) source.UnreadCount = MailMoveCachePolicy.AdjustSourceUnreadCount(source.UnreadCount, isRead);
                    if (destination != null) destination.UnreadCount = MailMoveCachePolicy.AdjustDestinationUnreadCount(destination.UnreadCount, isRead);
                }
            }
            SavePersistentCache();
        }

        private void RemoveBodyCacheIdentity(MailItem item)
        {
            string key = GetBodyCacheKey(item);
            lock (_bodyCacheLock)
            {
                _bodyCacheItems.Remove(key);
                _bodyCacheMetadata.Remove(key);
                if (string.Equals(_protectedBodyCacheKey, key, StringComparison.Ordinal))
                    _protectedBodyCacheKey = null;
            }
            item.BodyText = "";
            item.HtmlBody = "";
        }

        public async Task<MailMessageWindow> FetchMessagesAsync(
            MailAccount account,
            MailFolder folder,
            bool unreadOnly,
            int? pageSize = null,
            bool forceRefresh = false,
            bool loadMore = false,
            CancellationToken cancellationToken = default)
        {
            int requestedPageSize = Math.Clamp(pageSize ?? PageSize, MinPageSize, MaxPageSize);
            string cacheKey = GetMessageCacheKey(account.Id, folder.Id, unreadOnly);
            if (!forceRefresh && !loadMore && TryGetCachedMessages(cacheKey, out var cachedWindow))
            {
                if (MailPersistentCachePolicy.CanUseWindow(account.Kind, cachedWindow.Items))
                    return cachedWindow;
            }

            MailCursor? cursor = null;
            List<MailItem> existingItems = new();
            if (loadMore)
            {
                EnsurePersistentCacheLoaded();
                lock (_mailCacheLock)
                {
                    if (_persistentCache != null)
                    {
                        _persistentCache.Messages.TryGetValue(cacheKey, out existingItems!);
                        _persistentCache.MessageCursors.TryGetValue(cacheKey, out cursor);
                        existingItems = existingItems == null ? new List<MailItem>() : StripBodies(existingItems);
                    }
                }

                if (cursor == null)
                    return new MailMessageWindow { Items = existingItems, HasMore = false };
            }

            MailProviderPage page;
            try
            {
                page = await FetchMessagesFromProviderAsync(
                    account,
                    folder,
                    unreadOnly,
                    requestedPageSize,
                    cursor,
                    cancellationToken);
            }
            catch when (loadMore && !cancellationToken.IsCancellationRequested)
            {
                InvalidateMessageCursor(cacheKey);
                throw;
            }

            return CommitMessagePage(account, folder, unreadOnly, cacheKey, page, append: loadMore);
        }

        public async Task SendMailAsync(
            MailAccount account,
            string to,
            string subject,
            string body,
            IReadOnlyList<MailAttachmentData>? attachments = null,
            IProgress<MailSendProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            attachments ??= Array.Empty<MailAttachmentData>();
            var validationError = MailAttachmentPolicy.Validate(attachments);
            if (validationError != null) throw new InvalidOperationException(validationError.Value.ToString());
            progress?.Report(new MailSendProgress(MailSendStage.Preparing, 0, attachments.Count));
            using var timeoutCts = new CancellationTokenSource(attachments.Count == 0 ? TimeSpan.FromSeconds(30) : TimeSpan.FromMinutes(2));
            using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            var operationToken = operationCts.Token;

            if (account.Kind == MailAccountKind.Outlook)
            {
                await SendOutlookMailAsync(account, to, subject, body, attachments, progress, operationToken);
                return;
            }

            if (account.Kind == MailAccountKind.Google)
            {
                await SendGoogleMailAsync(account, to, subject, body, attachments, progress, operationToken);
                return;
            }

            if (account.Kind == MailAccountKind.Imap)
            {
                await SendSmtpMailAsync(account, to, subject, body, attachments, progress, operationToken);
                return;
            }

            throw new InvalidOperationException("Unsupported mail account.");
        }

        public void ApplyCachedMutation(MailItem item, MailMutationKind kind, bool value)
        {
            EnsureAccountsLoaded();
            var providerKind = _accounts.FirstOrDefault(account =>
                string.Equals(account.Id, item.AccountId, StringComparison.Ordinal))?.Kind ?? MailAccountKind.Imap;
            var directResult = MailMutationCachePolicy.Apply(new[] { item }, providerKind, item, kind, value);
            UpdateCachedMutation(providerKind, item, kind, value, directResult.ReadStateChangedFolderIds);
        }

        public Task SetReadStateAsync(MailAccount account, MailItem item, bool value, CancellationToken cancellationToken = default)
            => SetMailStateAsync(account, item, MailMutationKind.SetReadState, value, cancellationToken);

        public Task SetFlaggedAsync(MailAccount account, MailItem item, bool value, CancellationToken cancellationToken = default)
            => SetMailStateAsync(account, item, MailMutationKind.SetFlagged, value, cancellationToken);

        private async Task SetMailStateAsync(MailAccount account, MailItem item, MailMutationKind kind, bool value, CancellationToken cancellationToken)
        {
            if (!MailMutationCapabilityPolicy.Supports(account.Kind, kind))
                throw new NotSupportedException("This mail provider does not support the requested mutation.");

            string mutationKey = GetMutationKey(account.Kind, account.Id, item.FolderId, item.Id, item.ImapUidValidity, kind);
            var mutationGate = _mutationGates.GetOrAdd(mutationKey, _ => new SemaphoreSlim(1, 1));
            await mutationGate.WaitAsync(cancellationToken);
            try
            {
                if (RemovePendingMutationKind(new PendingMailMutation
                    {
                        AccountId = account.Id,
                        FolderId = item.FolderId,
                        MessageId = item.Id,
                        ProviderKind = account.Kind,
                        ImapUidValidity = item.ImapUidValidity,
                        Kind = kind
                    }))
                    SavePersistentCache();

                for (int attempt = 0; ; attempt++)
                {
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                    try
                    {
                        await SetMailStateRemoteOnceAsync(account, item, kind, value, operationCts.Token);
                        break;
                    }
                    catch (Exception ex) when (attempt == 0 &&
                                               !cancellationToken.IsCancellationRequested &&
                                               IsTransientMutationFailure(ex, timeoutCts.IsCancellationRequested))
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested &&
                                                IsTransientMutationFailure(ex, timeoutCts.IsCancellationRequested))
                    {
                        if (EnqueuePendingMutation(account, item, kind, value))
                            throw new MailMutationSyncQueuedException(ex);
                        throw;
                    }
                }
            }
            finally
            {
                mutationGate.Release();
            }
        }

        private async Task SetMailStateRemoteOnceAsync(MailAccount account, MailItem item, MailMutationKind kind, bool value, CancellationToken operationToken)
        {
            if (account.Kind == MailAccountKind.Outlook)
            {
                await EnsureOutlookMailWriteAuthorizedAsync(operationToken);
                if (_outlookClient != null)
                {
                    var patch = kind == MailMutationKind.SetReadState
                        ? new GraphMessage { IsRead = value }
                        : new GraphMessage { Flag = new FollowupFlag { FlagStatus = value ? FollowupFlagStatus.Flagged : FollowupFlagStatus.NotFlagged } };
                    await _outlookClient.Me.Messages[item.Id].PatchAsync(patch, cancellationToken: operationToken);
                }
            }
            else if (account.Kind == MailAccountKind.Google)
            {
                var gmail = await EnsureGoogleMailModifyAuthorizedAsync(operationToken);
                string label = kind == MailMutationKind.SetReadState ? "UNREAD" : "STARRED";
                bool addLabel = kind == MailMutationKind.SetReadState ? !value : value;
                await gmail.Users.Messages.Modify(new ModifyMessageRequest
                {
                    AddLabelIds = addLabel ? new List<string> { label } : null,
                    RemoveLabelIds = addLabel ? null : new List<string> { label }
                }, "me", item.Id).ExecuteAsync(operationToken);
            }
            else if (account.Kind == MailAccountKind.Imap)
            {
                using var client = new ImapClient();
                await ConnectImapAsync(client, account, GetImapPassword(account.Id), operationToken);
                var folder = await client.GetFolderAsync(item.FolderId, operationToken);
                await folder.OpenAsync(FolderAccess.ReadWrite, operationToken);
                if (!uint.TryParse(item.Id, out var uidValue) ||
                    !MailPaginationPolicy.IsValidImapMutation(item.ImapUidValidity, folder.UidValidity, uidValue))
                    throw new InvalidOperationException("IMAP message identity is no longer valid for this folder.");
                var flag = kind == MailMutationKind.SetReadState ? MessageFlags.Seen : MessageFlags.Flagged;
                if (value)
                    await folder.AddFlagsAsync(new UniqueId(uidValue), flag, true, operationToken);
                else
                    await folder.RemoveFlagsAsync(new UniqueId(uidValue), flag, true, operationToken);
                await client.DisconnectAsync(true, operationToken);
            }
        }

        private static bool IsTransientMutationFailure(Exception exception, bool operationTimedOut)
        {
            if (exception is OperationCanceledException)
                return operationTimedOut;
            if (exception is HttpRequestException httpException)
                return httpException.StatusCode == null || MailMutationRetryPolicy.IsTransientStatusCode((int)httpException.StatusCode.Value);
            if (exception is IOException)
                return true;
            if (exception is Microsoft.Kiota.Abstractions.ApiException kiotaException)
                return MailMutationRetryPolicy.IsTransientStatusCode(kiotaException.ResponseStatusCode);
            if (exception is Google.GoogleApiException googleException)
                return MailMutationRetryPolicy.IsTransientStatusCode((int)googleException.HttpStatusCode);

            return false;
        }

        private bool EnqueuePendingMutation(MailAccount account, MailItem item, MailMutationKind kind, bool value)
        {
            if (account.Kind == MailAccountKind.Imap && !item.ImapUidValidity.HasValue) return false;

            EnsurePersistentCacheLoaded();
            lock (_mailCacheLock)
            {
                if (_persistentCache == null) return false;
                MailPendingMutationPolicy.Upsert(_persistentCache.PendingMutations, new PendingMailMutation
                {
                    AccountId = account.Id,
                    FolderId = item.FolderId,
                    MessageId = item.Id,
                    ProviderKind = account.Kind,
                    ImapUidValidity = item.ImapUidValidity,
                    Kind = kind,
                    Value = value
                }, DateTimeOffset.UtcNow, maximumCount: 500);
            }
            SavePersistentCache();
            ScheduleNextPendingMutationRetry();
            return true;
        }

        public int RemoveAccountsForProvider(string providerName)
        {
            EnsureAccountsLoaded();
            providerName = ProviderAuthorizationLifecycle.NormalizeProviderName(providerName);
            var kind = providerName switch
            {
                "Google" => MailAccountKind.Google,
                "Microsoft" => MailAccountKind.Outlook,
                _ => (MailAccountKind?)null
            };
            if (!kind.HasValue) return 0;

            if (kind.Value == MailAccountKind.Outlook)
                _outlookClient = null;

            var accounts = _accounts.Where(account => account.Kind == kind.Value).ToList();
            foreach (var account in accounts)
            {
                DisconnectPollImapClient(account.Id);
                ClearAccountBackoff(account.Id);
                ClearAccountCache(account.Id);
                RemoveKnownUnreadForAccount(account.Id);
                _accounts.Remove(account);
            }

            if (accounts.Count > 0)
            {
                SaveAccounts();
                UpdateMailPollingSettings();
            }
            return accounts.Count;
        }

        public async Task FetchMessageBodyAsync(MailAccount account, MailItem item, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(item.BodyText) || !string.IsNullOrWhiteSpace(item.HtmlBody))
            {
                TouchVolatileMessageBody(item);
                PruneVolatileMessageBodies();
                return;
            }

            if (account.Kind == MailAccountKind.Outlook)
            {
                await EnsureOutlookMailReadAuthorizedAsync(cancellationToken);
                if (_outlookClient == null) return;

                var message = await _outlookClient.Me.Messages[item.Id].GetAsync(request =>
                {
                    request.QueryParameters.Select = new[] { "body" };
                }, cancellationToken);
                if (message?.Body?.Content != null)
                {
                    var content = message.Body.Content;
                    var isHtml = message.Body.ContentType == BodyType.Html || HasHtmlContentTags(content);
                    item.HtmlBody = isHtml ? content : "";
                    item.BodyText = CleanMailBody(isHtml ? StripHtml(content) : content);
                }
            }
            else if (account.Kind == MailAccountKind.Google)
            {
                var gmail = await EnsureGoogleMailReadAuthorizedAsync(cancellationToken);
                var get = gmail.Users.Messages.Get("me", item.Id);
                get.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
                var full = await get.ExecuteAsync(cancellationToken);
                if (full?.Payload != null)
                {
                    string body = ExtractGoogleBody(full.Payload);
                    string htmlBody = ExtractGoogleHtmlBody(full.Payload);
                    if (string.IsNullOrWhiteSpace(htmlBody) && HasHtmlContentTags(body))
                    {
                        htmlBody = body;
                        body = CleanMailBody(StripHtml(htmlBody));
                    }
                    item.BodyText = body;
                    item.HtmlBody = htmlBody;
                    if (string.IsNullOrWhiteSpace(item.Preview))
                        item.Preview = string.IsNullOrWhiteSpace(body) ? full.Snippet ?? "" : Truncate(body, 240);
                }
            }
            else if (account.Kind == MailAccountKind.Imap)
            {
                using var client = new ImapClient();
                await ConnectImapAsync(client, account, GetImapPassword(account.Id), cancellationToken);
                var folder = await client.GetFolderAsync(item.FolderId, cancellationToken);
                await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
                if (uint.TryParse(item.Id, out var uidValue))
                {
                    var message = await folder.GetMessageAsync(new UniqueId(uidValue), cancellationToken);
                    string rawText = message.TextBody ?? "";
                    string htmlBody = !string.IsNullOrWhiteSpace(message.HtmlBody)
                        ? message.HtmlBody
                        : HasHtmlContentTags(rawText) ? rawText : "";
                    string body = CleanMailBody(!string.IsNullOrWhiteSpace(htmlBody) ? StripHtml(htmlBody) : rawText);
                    item.BodyText = body;
                    item.HtmlBody = htmlBody;
                    if (string.IsNullOrWhiteSpace(item.Preview))
                        item.Preview = Truncate(body, 240);
                }
                await client.DisconnectAsync(true);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                item.BodyText = "";
                item.HtmlBody = "";
                cancellationToken.ThrowIfCancellationRequested();
            }

            LimitMailBody(item);
            TouchVolatileMessageBody(item);
            PruneVolatileMessageBodies();
            UpdatePersistentMessageBody(item);
        }

        public void ClearVolatileMessageBodies()
        {
            lock (_mailCacheLock)
            {
                foreach (var key in _messageCache.Keys.ToList())
                    _messageCache[key].Value = StripBodies(_messageCache[key].Value);
            }

            List<MailItem> items;
            lock (_bodyCacheLock)
            {
                items = _bodyCacheItems.Values
                    .Select(reference => reference.TryGetTarget(out var item) ? item : null)
                    .Where(item => item != null)
                    .Cast<MailItem>()
                    .Distinct()
                    .ToList();
                _bodyCacheItems.Clear();
                _bodyCacheMetadata.Clear();
            }
            ClearBodies(items);
        }

        public IReadOnlyList<CachedMailMetadata> GetCachedMetadataSnapshot()
        {
            EnsureAccountsLoaded();
            EnsurePersistentCacheLoaded();
            var visibleAccountIds = _accounts
                .Where(account => account.IsSetupComplete)
                .Select(account => account.Id)
                .ToHashSet(StringComparer.Ordinal);

            lock (_mailCacheLock)
            {
                var volatileMessages = _messageCache.Values.SelectMany(entry => entry.Value);
                var persistentMessages = _persistentCache?.Messages.Values.SelectMany(messages => messages)
                    ?? Enumerable.Empty<MailItem>();
                return volatileMessages.Concat(persistentMessages)
                    .Where(item => visibleAccountIds.Contains(item.AccountId))
                    .GroupBy(item => $"{item.AccountId}|{item.FolderId}|{item.Id}", StringComparer.Ordinal)
                    .Select(group => group
                        .OrderByDescending(item => item.RawReceivedTime)
                        .ThenBy(item => item.Id, StringComparer.Ordinal)
                        .First())
                    .Select(item => new CachedMailMetadata(
                        item.AccountId, item.FolderId, item.Id, item.Subject, item.Sender,
                        item.SenderAddress, item.Preview, item.ReceivedTime, item.RawReceivedTime))
                    .OrderByDescending(item => item.ReceivedAt)
                    .ThenBy(item => item.AccountId, StringComparer.Ordinal)
                    .ThenBy(item => item.FolderId, StringComparer.Ordinal)
                    .ThenBy(item => item.MessageId, StringComparer.Ordinal)
                    .ToList();
            }
        }

        public IReadOnlyList<MailBodyCacheAccountSize> GetVolatileBodyCacheSizes()
        {
            lock (_bodyCacheLock)
            {
                RemoveDeadBodyCacheEntriesLocked();
                return _bodyCacheMetadata.Values
                    .GroupBy(entry => entry.AccountId, StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => new MailBodyCacheAccountSize(
                        group.Key,
                        group.Count(),
                        group.Sum(entry => entry.RetainedBytes)))
                    .ToList();
            }
        }

        public (int MessageCount, long RetainedBytes) GetVolatileBodyCacheStats()
        {
            var sizes = GetVolatileBodyCacheSizes();
            return (sizes.Sum(size => size.MessageCount), sizes.Sum(size => size.RetainedBytes));
        }

        public void UpdateActiveBodyCacheContext(string? accountId, MailItem? activeItem = null)
        {
            lock (_bodyCacheLock)
            {
                _activeBodyCacheAccountId = accountId;
                _protectedBodyCacheKey = activeItem == null ? null : GetBodyCacheKey(activeItem);
            }
            PruneVolatileMessageBodies();
        }

        public void TrimVolatileMessageBodies() => PruneVolatileMessageBodies();

        public void ClearAccountVolatileMessageBodies(string accountId)
        {
            List<MailItem> items;
            lock (_bodyCacheLock)
            {
                var keys = _bodyCacheMetadata
                    .Where(pair => string.Equals(pair.Value.AccountId, accountId, StringComparison.Ordinal))
                    .Select(pair => pair.Key)
                    .ToList();
                items = RemoveBodyCacheEntriesLocked(keys);
                if (string.Equals(_activeBodyCacheAccountId, accountId, StringComparison.Ordinal))
                {
                    _activeBodyCacheAccountId = null;
                    _protectedBodyCacheKey = null;
                }
            }
            ClearBodies(items);
        }

        private void TouchVolatileMessageBody(MailItem item)
        {
            long retainedBytes = MailBodyCachePolicy.GetRetainedUtf16Bytes(item.BodyText, item.HtmlBody);
            if (retainedBytes == 0) return;

            var key = GetBodyCacheKey(item);
            lock (_bodyCacheLock)
            {
                _bodyCacheItems[key] = new WeakReference<MailItem>(item);
                _bodyCacheMetadata[key] = new BodyCacheMetadata(item.AccountId, retainedBytes, ++_bodyCacheAccessSequence);
            }
        }

        private void PruneVolatileMessageBodies()
        {
            List<MailItem> items;
            lock (_bodyCacheLock)
            {
                RemoveDeadBodyCacheEntriesLocked();
                var evictions = MailBodyCachePolicy.SelectEvictions(
                    _bodyCacheMetadata.Select(pair => new MailBodyCacheEntry(
                        pair.Key,
                        pair.Value.AccountId,
                        pair.Value.RetainedBytes,
                        pair.Value.AccessSequence)),
                    PerAccountMaxVolatileBodyCacheBytes,
                    PerAccountTargetVolatileBodyCacheBytes,
                    GlobalMaxVolatileBodyCacheBytes,
                    GlobalTargetVolatileBodyCacheBytes,
                    _activeBodyCacheAccountId,
                    _protectedBodyCacheKey);
                items = RemoveBodyCacheEntriesLocked(evictions);
            }
            ClearBodies(items);
        }

        private void RemoveDeadBodyCacheEntriesLocked()
        {
            foreach (var key in _bodyCacheItems.Keys.ToList())
            {
                if (_bodyCacheItems[key].TryGetTarget(out _) && _bodyCacheMetadata.ContainsKey(key)) continue;
                _bodyCacheItems.Remove(key);
                _bodyCacheMetadata.Remove(key);
            }
        }

        private List<MailItem> RemoveBodyCacheEntriesLocked(IEnumerable<string> keys)
        {
            var items = new List<MailItem>();
            foreach (var key in keys)
            {
                if (_bodyCacheItems.Remove(key, out var reference) && reference.TryGetTarget(out var item))
                    items.Add(item);
                _bodyCacheMetadata.Remove(key);
            }
            return items.Distinct().ToList();
        }

        private static void ClearBodies(IEnumerable<MailItem> items)
        {
            foreach (var item in items)
            {
                item.BodyText = "";
                item.HtmlBody = "";
            }
        }

        private static string GetBodyCacheKey(MailItem item)
            => $"{item.AccountId}|{item.FolderId}|{item.Id}";

        private async Task SendOutlookMailAsync(MailAccount account, string to, string subject, string body, IReadOnlyList<MailAttachmentData> attachments, IProgress<MailSendProgress>? progress, CancellationToken cancellationToken)
        {
            await EnsureOutlookMailAuthorizedAsync(requireWrite: true, requireSend: true, cancellationToken);
            if (_outlookClient == null) return;

            var message = new GraphMessage
            {
                Subject = subject,
                Body = new ItemBody { ContentType = BodyType.Text, Content = body },
                ToRecipients = ParseRecipients(to)
                    .Select(address => new Recipient { EmailAddress = new EmailAddress { Address = address } })
                    .ToList(),
                Attachments = attachments.Select(attachment => (Attachment)new FileAttachment
                {
                    Name = attachment.FileName,
                    ContentType = attachment.ContentType,
                    ContentBytes = attachment.Content
                }).ToList()
            };
            progress?.Report(new MailSendProgress(MailSendStage.UploadingAttachments, 0, attachments.Count));

            var draft = await _outlookClient.Me.Messages.PostAsync(message, request =>
                request.Headers.Add("Prefer", "IdType=\"ImmutableId\""), cancellationToken);
            if (string.IsNullOrWhiteSpace(draft?.Id))
                throw new InvalidOperationException("Microsoft Graph did not return a draft identifier.");
            progress?.Report(new MailSendProgress(MailSendStage.UploadingAttachments, attachments.Count, attachments.Count));

            try
            {
                progress?.Report(new MailSendProgress(MailSendStage.Sending, attachments.Count, attachments.Count));
                await _outlookClient.Me.Messages[draft.Id].Send.PostAsync(request =>
                    request.Headers.Add("Prefer", "IdType=\"ImmutableId\""), cancellationToken);
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                progress?.Report(new MailSendProgress(MailSendStage.Confirming, attachments.Count, attachments.Count));
                if (await TryConfirmOutlookSentMessageAsync(draft.Id))
                    return;
                throw new MailSendStatusUnknownException(ex);
            }
            catch
            {
                await TryMoveOutlookDraftToDeletedItemsAsync(draft.Id);
                throw;
            }
        }

        private async Task<bool> TryConfirmOutlookSentMessageAsync(string immutableMessageId)
        {
            if (_outlookClient == null || string.IsNullOrWhiteSpace(immutableMessageId)) return false;

            using var confirmationCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    if (attempt > 0)
                        await Task.Delay(TimeSpan.FromSeconds(2), confirmationCts.Token);

                    var message = await _outlookClient.Me.Messages[immutableMessageId].GetAsync(request =>
                    {
                        request.Headers.Add("Prefer", "IdType=\"ImmutableId\"");
                        request.QueryParameters.Select = new[] { "isDraft", "sentDateTime" };
                    }, confirmationCts.Token);
                    if (MailSendConfirmationPolicy.IsConfirmedOutlookSentItem(message?.IsDraft, message?.SentDateTime))
                        return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Outlook sent-message confirmation failed: {ex.Message}");
            }

            return false;
        }

        private async Task TryMoveOutlookDraftToDeletedItemsAsync(string draftId)
        {
            if (_outlookClient == null || string.IsNullOrWhiteSpace(draftId)) return;

            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                string? deletedItemsId = await ResolveOutlookWellKnownFolderIdAsync("deleteditems", cleanupCts.Token);
                if (string.IsNullOrWhiteSpace(deletedItemsId)) return;
                await _outlookClient.Me.Messages[draftId].Move.PostAsync(
                    new MovePostRequestBody { DestinationId = deletedItemsId },
                    requestConfiguration: null, cancellationToken: cleanupCts.Token);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Outlook draft cleanup failed: {ex.Message}");
            }
        }

        private async Task SendGoogleMailAsync(MailAccount account, string to, string subject, string body, IReadOnlyList<MailAttachmentData> attachments, IProgress<MailSendProgress>? progress, CancellationToken cancellationToken)
        {
            var gmail = await EnsureGoogleMailSendAuthorizedAsync(cancellationToken);
            var mime = CreateMimeMessage(account, to, subject, body, attachments);
            using var stream = new MemoryStream();
            await mime.WriteToAsync(stream, cancellationToken);

            try
            {
                progress?.Report(new MailSendProgress(MailSendStage.Sending, attachments.Count, attachments.Count));
                await gmail.Users.Messages.Send(new GmailMessage
                {
                    Raw = ToBase64Url(stream.ToArray())
                }, "me").ExecuteAsync(cancellationToken);
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                progress?.Report(new MailSendProgress(MailSendStage.Confirming, attachments.Count, attachments.Count));
                if (await TryConfirmGoogleSentMessageAsync(gmail, mime.MessageId))
                    return;
                throw new MailSendStatusUnknownException(ex);
            }
        }

        private async Task SendSmtpMailAsync(MailAccount account, string to, string subject, string body, IReadOnlyList<MailAttachmentData> attachments, IProgress<MailSendProgress>? progress, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(account.SmtpHost))
                throw new InvalidOperationException("SMTP server is required for IMAP mail sending.");

            var password = GetImapPassword(account.Id);
            var message = CreateMimeMessage(account, to, subject, body, attachments);

            using var client = new MailKit.Net.Smtp.SmtpClient { Timeout = MailNetworkTimeoutMs };
            var socketOptions = GetSmtpSocketOptions(account);
            await client.ConnectAsync(account.SmtpHost, account.SmtpPort, socketOptions, cancellationToken);
            await client.AuthenticateAsync(string.IsNullOrWhiteSpace(account.SmtpUserName) ? account.ImapUserName : account.SmtpUserName, password, cancellationToken);
            try
            {
                progress?.Report(new MailSendProgress(MailSendStage.Sending, attachments.Count, attachments.Count));
                await client.SendAsync(message, cancellationToken);
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                progress?.Report(new MailSendProgress(MailSendStage.Confirming, attachments.Count, attachments.Count));
                if (await TryConfirmImapSentMessageAsync(account, password, message.MessageId))
                    return;
                throw new MailSendStatusUnknownException(ex);
            }

            try { await client.DisconnectAsync(true, cancellationToken); }
            catch { }
        }

        private static async Task<bool> TryConfirmGoogleSentMessageAsync(GmailService gmail, string? messageId)
        {
            var query = MailSendConfirmationPolicy.BuildGmailQuery(messageId);
            if (query == null) return false;

            using var confirmationCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    if (attempt > 0)
                        await Task.Delay(TimeSpan.FromSeconds(2), confirmationCts.Token);

                    var request = gmail.Users.Messages.List("me");
                    request.LabelIds = "SENT";
                    request.Q = query;
                    request.MaxResults = 1;
                    var response = await request.ExecuteAsync(confirmationCts.Token);
                    if (response.Messages?.Count > 0)
                        return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Gmail sent-message confirmation failed: {ex.Message}");
            }

            return false;
        }

        private async Task<bool> TryConfirmImapSentMessageAsync(MailAccount account, string password, string? messageId)
        {
            var normalizedMessageId = MailSendConfirmationPolicy.NormalizeMessageId(messageId);
            if (normalizedMessageId == null) return false;

            using var confirmationCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var client = new ImapClient { Timeout = MailNetworkTimeoutMs };
            try
            {
                await ConnectImapAsync(client, account, password, confirmationCts.Token);
                var sentFolder = client.GetFolder(MailKit.SpecialFolder.Sent);
                if (sentFolder == null) return false;
                await sentFolder.OpenAsync(FolderAccess.ReadOnly, confirmationCts.Token);

                for (int attempt = 0; attempt < 2; attempt++)
                {
                    if (attempt > 0)
                        await Task.Delay(TimeSpan.FromSeconds(2), confirmationCts.Token);

                    var matches = await sentFolder.SearchAsync(
                        MailKit.Search.SearchQuery.HeaderContains("Message-Id", normalizedMessageId),
                        confirmationCts.Token);
                    if (matches.Count > 0)
                        return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"IMAP sent-message confirmation failed: {ex.Message}");
            }
            finally
            {
                try
                {
                    if (client.IsConnected)
                        await client.DisconnectAsync(true, CancellationToken.None);
                }
                catch { }
            }

            return false;
        }

        private async Task RetryPendingMutationsAsync(MailAccount account)
        {
            await _pendingMutationRetryGate.WaitAsync();
            try
            {
                List<PendingMailMutation> due;
                var now = DateTimeOffset.UtcNow;
                bool changed;
                lock (_mailCacheLock)
                {
                    if (_persistentCache == null) return;
                    var expired = MailPendingMutationPolicy.RemoveExpiredItems(_persistentCache.PendingMutations, now);
                    foreach (var mutation in expired)
                        InvalidatePendingMutationCacheLocked(mutation);
                    changed = expired.Count > 0;
                    due = MailPendingMutationPolicy.SelectDue(
                        _persistentCache.PendingMutations, account.Id, account.Kind, now, maximumCount: 3);
                }

                foreach (var mutation in due)
                {
                    string mutationKey = GetMutationKey(
                        mutation.ProviderKind,
                        mutation.AccountId,
                        mutation.FolderId,
                        mutation.MessageId,
                        mutation.ImapUidValidity,
                        mutation.Kind);
                    var mutationGate = _mutationGates.GetOrAdd(mutationKey, _ => new SemaphoreSlim(1, 1));
                    await mutationGate.WaitAsync();
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    try
                    {
                        lock (_mailCacheLock)
                        {
                            if (_persistentCache == null ||
                                _persistentCache.PendingMutations.FirstOrDefault(candidate => MailPendingMutationPolicy.IsSame(candidate, mutation)) is not { } current ||
                                !MailPendingMutationPolicy.IsCurrentIntent(current, mutation))
                                continue;
                        }
                        await SetMailStateRemoteOnceAsync(account, new MailItem
                        {
                            AccountId = mutation.AccountId,
                            FolderId = mutation.FolderId,
                            Id = mutation.MessageId,
                            ImapUidValidity = mutation.ImapUidValidity,
                        }, mutation.Kind, mutation.Value, timeoutCts.Token);
                        changed |= RemovePendingMutationIfCurrent(mutation);
                    }
                    catch (Exception ex)
                    {
                        if (IsTransientMutationFailure(ex, timeoutCts.IsCancellationRequested))
                            changed |= ReschedulePendingMutation(mutation);
                        else if (RemovePendingMutationIfCurrent(mutation))
                        {
                            lock (_mailCacheLock)
                                InvalidatePendingMutationCacheLocked(mutation);
                            changed = true;
                        }
                    }
                    finally
                    {
                        mutationGate.Release();
                    }
                }

                if (changed)
                    SavePersistentCache();
            }
            finally
            {
                _pendingMutationRetryGate.Release();
            }
        }

        private bool RemovePendingMutation(PendingMailMutation mutation)
        {
            lock (_mailCacheLock)
            {
                if (_persistentCache == null) return false;
                return MailPendingMutationPolicy.Remove(_persistentCache.PendingMutations, mutation);
            }
        }

        private bool RemovePendingMutationIfCurrent(PendingMailMutation mutation)
        {
            lock (_mailCacheLock)
            {
                if (_persistentCache == null) return false;
                return MailPendingMutationPolicy.RemoveIfCurrent(_persistentCache.PendingMutations, mutation);
            }
        }

        private bool RemovePendingMutationKind(PendingMailMutation mutation)
        {
            lock (_mailCacheLock)
            {
                if (_persistentCache == null) return false;
                return MailPendingMutationPolicy.RemoveKind(_persistentCache.PendingMutations, mutation);
            }
        }

        private bool ReschedulePendingMutation(PendingMailMutation mutation)
        {
            lock (_mailCacheLock)
            {
                if (_persistentCache == null) return false;
                var current = MailPendingMutationPolicy.Find(_persistentCache.PendingMutations, mutation);
                if (current == null || !MailPendingMutationPolicy.IsCurrentIntent(current, mutation)) return false;

                current.FailureCount = Math.Min(current.FailureCount + 1, 30);
                current.NextAttemptUtcTicks = (DateTimeOffset.UtcNow + MailMutationRetryPolicy.GetRetryDelay(current.FailureCount)).UtcTicks;
                return true;
            }
        }

        private static string GetMutationKey(
            MailAccountKind providerKind,
            string accountId,
            string folderId,
            string messageId,
            uint? imapUidValidity,
            MailMutationKind kind)
            => MailPendingMutationPolicy.MutationKey(new PendingMailMutation
            {
                AccountId = accountId,
                FolderId = folderId,
                MessageId = messageId,
                ProviderKind = providerKind,
                ImapUidValidity = imapUidValidity,
                Kind = kind
            });

        private async Task CheckNewMailAsync()
        {
            if (Interlocked.CompareExchange(ref _isPollingMail, 1, 0) != 0 || !MailPollingEnabled) return;
            _lastMailPollStartedUtc = DateTimeOffset.UtcNow;

            try
            {
                EnsureAccountsLoaded();
                LoadKnownUnreadIds();
                EnsurePersistentCacheLoaded();

                var currentUnreadIds = new HashSet<string>(StringComparer.Ordinal);
                var newItems = new List<(MailAccount Account, MailItem Item)>();

                var nowUtc = DateTimeOffset.UtcNow;
                foreach (var account in _accounts.Where(account => account.IsSetupComplete))
                {
                    if (IsAccountInBackoff(account.Id, nowUtc)) continue;

                    try
                    {
                        await RetryPendingMutationsAsync(account);
                        var folders = await FetchFoldersAsync(account, forceRefresh: true);
                        var inbox = folders.FirstOrDefault(folder => IsInboxName(folder.Id) || IsInboxName(folder.DisplayName))
                                    ?? folders.FirstOrDefault(folder => !folder.IsPlaceholder);
                        if (inbox == null)
                        {
                            ClearAccountBackoff(account.Id);
                            continue;
                        }

                        var unreadPage = await PollFetchInboxUnreadAsync(account, inbox);
                        var unreadItems = MergeMessagesIntoPersistentCache(account, inbox, unreadPage);

                        string inboxKey = $"{account.Id}|{inbox.Id}";
                        long previousSeenTicks = GetLastSeenInboxTicks(inboxKey);
                        long newestTicks = previousSeenTicks;
                        bool hasBaseline = previousSeenTicks > 0;

                        foreach (var item in unreadItems)
                        {
                            string key = GetMailNotificationKey(account.Kind, item);
                            currentUnreadIds.Add(key);
                            long itemTicks = GetMailReceivedTicks(item);
                            if (itemTicks > newestTicks)
                                newestTicks = itemTicks;

                            if (hasBaseline &&
                                itemTicks > previousSeenTicks &&
                                !_knownUnreadIds.ContainsKey(key))
                            {
                                newItems.Add((account, item));
                            }
                        }

                        if (newestTicks > previousSeenTicks)
                            SetLastSeenInboxTicks(inboxKey, newestTicks);

                        ClearAccountBackoff(account.Id);
                    }
                    catch (Exception ex)
                    {
                        RegisterAccountFailure(account.Id);
                        System.Diagnostics.Debug.WriteLine($"Mail polling failed for {account.DisplayTitle}: {ex.Message}");
                    }
                }

                foreach (var pair in newItems.Take(5))
                    SendNewMailNotification(pair.Account, pair.Item);

                _knownUnreadIds.Clear();
                foreach (var id in currentUnreadIds.Take(500))
                    _knownUnreadIds[id] = 0;
                SaveKnownUnreadIds();
                SavePersistentCache();
            }
            finally
            {
                _lastMailPollCompletedUtc = DateTimeOffset.UtcNow;
                Volatile.Write(ref _isPollingMail, 0);
            }
        }

        private bool IsAccountInBackoff(string accountId, DateTimeOffset nowUtc)
            => _pollBackoff.TryGetValue(accountId, out var state) && state.NextAttemptUtc > nowUtc;

        private void RegisterAccountFailure(string accountId)
        {
            if (!_pollBackoff.TryGetValue(accountId, out var state))
            {
                state = new PollBackoff();
                _pollBackoff[accountId] = state;
            }

            state.Failures = Math.Min(state.Failures + 1, 30);
            // Skip 2^(failures-1) poll cycles, capped — e.g. 1,2,4,8,16 intervals.
            int cycles = Math.Min(1 << Math.Min(state.Failures - 1, 4), MaxPollBackoffCycles);
            var interval = TimeSpan.FromMinutes(Math.Max(1, MailPollingIntervalMinutes));
            state.NextAttemptUtc = DateTimeOffset.UtcNow + TimeSpan.FromTicks(interval.Ticks * cycles);
        }

        private void ClearAccountBackoff(string accountId)
        {
            if (_pollBackoff.Count > 0)
                _pollBackoff.Remove(accountId);
        }

        // Returns a connected, live IMAP client for the poll path, reconnecting if the
        // cached connection went away. Never called concurrently (see _pollImapClients).
        private async Task<ImapClient> GetOrConnectPollImapClientAsync(MailAccount account)
        {
            if (_pollImapClients.TryGetValue(account.Id, out var existing))
            {
                if (existing.IsConnected && existing.IsAuthenticated)
                {
                    try
                    {
                        // Bound the liveness probe too: on a dropped network the cached
                        // connection's NoOp would otherwise block until MailKit's 2-min timeout.
                        using var noopCts = new CancellationTokenSource(MailNetworkTimeoutMs);
                        await existing.NoOpAsync(noopCts.Token);
                        return existing;
                    }
                    catch
                    {
                        // Stale/dropped connection — fall through to reconnect.
                    }
                }

                _pollImapClients.Remove(account.Id);
                try { existing.Dispose(); } catch { }
            }

            var client = new ImapClient();
            await ConnectImapAsync(client, account, GetImapPassword(account.Id));
            _pollImapClients[account.Id] = client;
            return client;
        }

        private void DisconnectPollImapClient(string accountId)
        {
            if (!_pollImapClients.TryGetValue(accountId, out var client)) return;
            _pollImapClients.Remove(accountId);
            try { if (client.IsConnected) client.Disconnect(true); } catch { }
            try { client.Dispose(); } catch { }
        }

        private void DisconnectAllPollImapClients()
        {
            foreach (var accountId in _pollImapClients.Keys.ToList())
                DisconnectPollImapClient(accountId);
        }

        // Fetch the inbox unread slice during a background poll. IMAP reuses the
        // persistent connection; other providers go through the normal fetch path.
        private async Task<MailProviderPage> PollFetchInboxUnreadAsync(MailAccount account, MailFolder inbox)
        {
            const int pollPageSize = 5;
            if (account.Kind != MailAccountKind.Imap)
                return await FetchMessagesFromProviderAsync(account, inbox, unreadOnly: true, pageSize: pollPageSize);

            var client = await GetOrConnectPollImapClientAsync(account);
            MailProviderPage page;
            try
            {
                page = await FetchImapMessagesWithClientAsync(client, account, inbox, unreadOnly: true, pageSize: pollPageSize);
            }
            catch
            {
                // Connection likely went bad mid-fetch — drop it so the next poll reconnects.
                DisconnectPollImapClient(account.Id);
                throw;
            }

            return new MailProviderPage
            {
                Items = CloneMailItems(page.Items, includeBodies: false),
                NextCursor = page.NextCursor
            };
        }

        private async Task<MailProviderPage> FetchMessagesFromProviderAsync(
            MailAccount account,
            MailFolder folder,
            bool unreadOnly,
            int pageSize,
            MailCursor? cursor = null,
            CancellationToken cancellationToken = default)
        {
            if (account.Kind == MailAccountKind.Google)
                return await FetchGoogleMessagesAsync(account, folder, unreadOnly, pageSize, cursor, cancellationToken);
            if (account.Kind == MailAccountKind.Imap)
                return await FetchImapMessagesAsync(account, folder, unreadOnly, pageSize, cursor, cancellationToken);
            if (account.Kind != MailAccountKind.Outlook || !account.IsSetupComplete || folder.IsPlaceholder)
                return new MailProviderPage();

            if (cursor != null && cursor.ProviderKind != MailAccountKind.Outlook)
                throw new InvalidOperationException("Mail continuation does not match the account provider.");

            await EnsureOutlookMailReadAuthorizedAsync(cancellationToken);
            if (_outlookClient == null) return new MailProviderPage();

            Microsoft.Graph.Models.MessageCollectionResponse? response;
            if (cursor != null)
            {
                if (!MailPaginationPolicy.IsAllowedGraphNextLink(cursor.Value))
                    throw new InvalidOperationException("Mail continuation URL is invalid.");
                response = await _outlookClient.Me.MailFolders[folder.Id].Messages
                    .WithUrl(cursor.Value)
                    .GetAsync(cancellationToken: cancellationToken);
            }
            else
            {
                response = await _outlookClient.Me.MailFolders[folder.Id].Messages.GetAsync(request =>
                {
                    request.QueryParameters.Top = Math.Clamp(pageSize, MinPageSize, MaxPageSize);
                    request.QueryParameters.Select = new[]
                    {
                        "id", "subject", "from", "toRecipients", "receivedDateTime", "isRead", "flag",
                        "bodyPreview", "webLink", "hasAttachments", "importance"
                    };
                    request.QueryParameters.Orderby = new[] { "receivedDateTime desc" };
                    if (unreadOnly)
                        request.QueryParameters.Filter = "isRead eq false";
                }, cancellationToken);
            }

            var items = response?.Value?
                .Where(message => message != null)
                .Select(message => ToOutlookMailItem(account.Id, folder.Id, message))
                .ToList() ?? new List<MailItem>();
            var nextLink = response?.OdataNextLink;
            return new MailProviderPage
            {
                Items = items,
                NextCursor = MailPaginationPolicy.IsAllowedGraphNextLink(nextLink)
                    ? new MailCursor { ProviderKind = MailAccountKind.Outlook, Value = nextLink! }
                    : null
            };
        }

        private async Task<GmailService> EnsureGoogleMailReadAuthorizedAsync(CancellationToken cancellationToken = default)
        {
            return await EnsureGoogleMailAuthorizedAsync(requireModify: false, requireSend: false, cancellationToken);
        }

        private async Task<GmailService> EnsureGoogleMailModifyAuthorizedAsync(CancellationToken cancellationToken = default)
        {
            return await EnsureGoogleMailAuthorizedAsync(requireModify: true, requireSend: false, cancellationToken);
        }

        private async Task<GmailService> EnsureGoogleMailSendAuthorizedAsync(CancellationToken cancellationToken = default)
        {
            return await EnsureGoogleMailAuthorizedAsync(requireModify: false, requireSend: true, cancellationToken);
        }

        private async Task<GmailService> EnsureGoogleMailAuthorizedAsync(bool requireModify, bool requireSend, CancellationToken cancellationToken = default)
        {
            EnsureAccountsLoaded();

            if (App.Current is App app &&
                app.SyncManager.GetProvider("Google") is GoogleSyncProvider googleProvider)
            {
                return await googleProvider.EnsureGmailAuthorizedAsync(requireModify, requireSend, cancellationToken);
            }

            throw new InvalidOperationException("Google provider is not available.");
        }

        private async Task TestImapConnectionAsync(MailAccount account, string password, CancellationToken cancellationToken)
        {
            using var client = new ImapClient();
            await ConnectImapAsync(client, account, password, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
        }

        private async Task<List<MailFolder>> FetchImapFoldersAsync(MailAccount account, CancellationToken cancellationToken)
        {
            using var client = new ImapClient();
            await ConnectImapAsync(client, account, GetImapPassword(account.Id), cancellationToken);

            var result = new List<MailFolder>();
            var folders = await client.GetFoldersAsync(client.PersonalNamespaces.FirstOrDefault() ?? client.PersonalNamespaces[0], cancellationToken: cancellationToken);
            foreach (var folder in folders.Where(folder => (folder.Attributes & FolderAttributes.NonExistent) == 0))
            {
                int? unreadCount = null;
                try
                {
                    await folder.StatusAsync(StatusItems.Unread, cancellationToken);
                    unreadCount = folder.Unread;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch { }

                result.Add(new MailFolder
                {
                    AccountId = account.Id,
                    Id = folder.FullName,
                    DisplayName = string.IsNullOrWhiteSpace(folder.Name) ? folder.FullName : folder.Name,
                    UnreadCount = unreadCount
                });
            }

            await client.DisconnectAsync(true, cancellationToken);

            return result
                .Where(folder => !IsNoisyImapFolder(folder.Id, folder.DisplayName))
                .GroupBy(folder => NormalizeFolderKey(folder.Id, folder.DisplayName), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(folder => IsInboxName(folder.Id) || IsInboxName(folder.DisplayName))
                .ThenBy(folder => folder.DisplayName)
                .ToList();
        }

        private async Task<MailProviderPage> FetchImapMessagesAsync(MailAccount account, MailFolder folder, bool unreadOnly, int? pageSize, MailCursor? cursor, CancellationToken cancellationToken)
        {
            if (!account.IsSetupComplete || folder.IsPlaceholder)
                return new MailProviderPage();

            using var client = new ImapClient();
            await ConnectImapAsync(client, account, GetImapPassword(account.Id), cancellationToken);
            try
            {
                return await FetchImapMessagesWithClientAsync(client, account, folder, unreadOnly, pageSize, cursor, cancellationToken);
            }
            finally
            {
                try
                {
                    if (!cancellationToken.IsCancellationRequested)
                        await client.DisconnectAsync(true, cancellationToken);
                }
                catch { }
            }
        }

        // Core fetch against an already-connected client; the caller owns the connection
        // lifetime (UI path opens/closes per call; the poll path reuses a persistent one).
        private async Task<MailProviderPage> FetchImapMessagesWithClientAsync(ImapClient client, MailAccount account, MailFolder folder, bool unreadOnly, int? pageSize, MailCursor? cursor = null, CancellationToken cancellationToken = default)
        {
            var mailFolder = await client.GetFolderAsync(folder.Id, cancellationToken);
            await mailFolder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

            if (cursor != null && (cursor.ProviderKind != MailAccountKind.Imap ||
                                   !MailPaginationPolicy.IsValidImapCursor(cursor.UidValidity, mailFolder.UidValidity, cursor.BeforeUid)))
                throw new InvalidOperationException("IMAP continuation is no longer valid for this folder.");

            var query = unreadOnly ? MailKit.Search.SearchQuery.NotSeen : MailKit.Search.SearchQuery.All;
            if (cursor?.BeforeUid is uint beforeUid)
                query = query.And(MailKit.Search.SearchQuery.Uids(new UniqueIdRange(new UniqueId(1), new UniqueId(beforeUid - 1))));
            var ids = await mailFolder.SearchAsync(query, cancellationToken);
            int top = Math.Clamp(pageSize ?? PageSize, MinPageSize, MaxPageSize);
            var selectedIds = ids.OrderByDescending(id => id.Id).Take(top).ToList();

            var summaryItems = MessageSummaryItems.Envelope | MessageSummaryItems.Flags | MessageSummaryItems.BodyStructure;
            IList<IMessageSummary>? summaries = null;
            try
            {
                summaries = await mailFolder.FetchAsync(selectedIds, summaryItems, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                // Some IMAP servers return malformed FETCH responses for summaries.
            }

            var items = new List<MailItem>();
            foreach (var id in selectedIds)
            {
                var summary = summaries?.FirstOrDefault(s => s.UniqueId == id);
                bool isRead = summary?.Flags?.HasFlag(MessageFlags.Seen) == true
                    || (unreadOnly ? false : await GetImapReadStateAsync(mailFolder, id, new Dictionary<uint, MessageFlags?>(), cancellationToken));
                try
                {
                    var item = await BuildImapMailItemAsync(mailFolder, account, folder, id, summary, isRead, cancellationToken);
                    item.IsFlagged = summary?.Flags?.HasFlag(MessageFlags.Flagged) == true;
                    items.Add(item);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch
                {
                    items.Add(new MailItem
                    {
                        AccountId = account.Id,
                        FolderId = folder.Id,
                        Id = id.Id.ToString(),
                        ImapUidValidity = mailFolder.UidValidity,
                        Subject = _loader.GetStringOrDefault("TextMailReadError") ?? "Unable to read this email",
                        Sender = account.DisplayTitle,
                        Preview = _loader.GetStringOrDefault("TextMailFetchError") ?? "IMAP server returned an invalid FETCH response.",
                        IsRead = isRead
                    });
                }
            }

            uint? nextBeforeUid = ids.Count > top ? selectedIds.Min(id => id.Id) : null;
            return new MailProviderPage
            {
                Items = items.OrderByDescending(item => item.RawReceivedTime).ToList(),
                NextCursor = nextBeforeUid is > 1
                    ? new MailCursor
                    {
                        ProviderKind = MailAccountKind.Imap,
                        UidValidity = mailFolder.UidValidity,
                        BeforeUid = nextBeforeUid
                    }
                    : null
            };
        }

        private async Task<MailItem> BuildImapMailItemAsync(
            IMailFolder mailFolder,
            MailAccount account,
            MailFolder folder,
            UniqueId id,
            IMessageSummary? summary,
            bool isRead,
            CancellationToken cancellationToken)
        {
            string preview = await TryGetImapPreviewAsync(mailFolder, id, summary, cancellationToken);
            if (HasUsefulImapEnvelope(summary))
            {
                var received = summary?.Envelope?.Date;
                return new MailItem
                {
                    AccountId = account.Id,
                    FolderId = folder.Id,
                    Id = id.Id.ToString(),
                    ImapUidValidity = mailFolder.UidValidity,
                    Subject = string.IsNullOrWhiteSpace(summary?.Envelope?.Subject) ? "(No subject)" : summary.Envelope.Subject,
                    Sender = FormatInternetAddressList(summary?.Envelope?.From),
                    SenderAddress = summary?.Envelope?.From?.Mailboxes?.FirstOrDefault()?.Address ?? "",
                    Recipient = FormatInternetAddressList(summary?.Envelope?.To),
                    Preview = preview,
                    RawReceivedTime = received,
                    ReceivedTime = FormatReceivedTime(received),
                    IsRead = isRead,
                    HasAttachments = summary?.Attachments?.Any() == true
                };
            }

            try
            {
                var message = await mailFolder.GetMessageAsync(id, cancellationToken);
                var item = ToImapMailItem(account.Id, folder.Id, id.Id.ToString(), mailFolder.UidValidity, message, isRead);
                if (string.IsNullOrWhiteSpace(item.Preview))
                    item.Preview = preview;
                return item;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"IMAP full message fallback failed for {id}: {ex.Message}");
                return new MailItem
                {
                    AccountId = account.Id,
                    FolderId = folder.Id,
                    Id = id.Id.ToString(),
                    ImapUidValidity = mailFolder.UidValidity,
                    Subject = "(No subject)",
                    Sender = account.DisplayTitle,
                    Preview = preview,
                    RawReceivedTime = summary?.Envelope?.Date,
                    ReceivedTime = FormatReceivedTime(summary?.Envelope?.Date),
                    IsRead = isRead,
                    HasAttachments = summary?.Attachments?.Any() == true
                };
            }
        }

        private static bool HasUsefulImapEnvelope(IMessageSummary? summary)
            => !string.IsNullOrWhiteSpace(summary?.Envelope?.Subject) ||
               summary?.Envelope?.From?.Mailboxes?.Any() == true ||
               summary?.Envelope?.To?.Mailboxes?.Any() == true;

        private static async Task<string> TryGetImapPreviewAsync(IMailFolder mailFolder, UniqueId id, IMessageSummary? summary, CancellationToken cancellationToken)
        {
            if (summary?.TextBody is not BodyPartText textPart)
                return "";

            try
            {
                var entity = await mailFolder.GetBodyPartAsync(id, textPart, cancellationToken);
                return entity is TextPart textContent
                    ? Truncate(CleanMailBody(textContent.Text ?? ""), 240).Trim()
                    : "";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                return "";
            }
        }

        // MailKit's default Timeout is 2 minutes and ConnectAsync/AuthenticateAsync take no
        // cancellation by default — so with no network the startup poll's connect (and DNS
        // resolution) hangs for minutes, which looks like the app freezing on launch offline.
        // Cap connect/auth with a short timeout + token so an offline attempt fails fast and
        // hands off to the per-account poll backoff.
        private const int MailNetworkTimeoutMs = 10000;

        private static async Task ConnectImapAsync(ImapClient client, MailAccount account, string password, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(account.ImapHost))
                throw new InvalidOperationException("IMAP server is required.");
            if (string.IsNullOrWhiteSpace(account.ImapUserName))
                throw new InvalidOperationException("IMAP user name is required.");
            if (string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("IMAP password is required.");

            client.Timeout = MailNetworkTimeoutMs;
            using var timeoutCts = new CancellationTokenSource(MailNetworkTimeoutMs);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            var socketOptions = account.ImapUseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
            await client.ConnectAsync(account.ImapHost, account.ImapPort, socketOptions, cts.Token);
            await client.AuthenticateAsync(account.ImapUserName, password, cts.Token);
        }

        private static async Task<bool> GetImapReadStateAsync(IMailFolder folder, UniqueId id, Dictionary<uint, MessageFlags?> flagsById, CancellationToken cancellationToken = default)
        {
            if (flagsById.TryGetValue(id.Id, out var cachedFlags))
                return cachedFlags?.HasFlag(MessageFlags.Seen) == true;

            try
            {
                var summaries = await folder.FetchAsync(new[] { id }, MessageSummaryItems.Flags, cancellationToken);
                var flags = summaries.FirstOrDefault()?.Flags;
                return flags?.HasFlag(MessageFlags.Seen) == true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                // If the server refuses flag fetches, prefer not to show a false unread dot.
                return true;
            }
        }

        private async Task<List<MailFolder>> FetchGoogleFoldersAsync(MailAccount account, CancellationToken cancellationToken)
        {
            var gmail = await EnsureGoogleMailReadAuthorizedAsync(cancellationToken);
            var labels = await gmail.Users.Labels.List("me").ExecuteAsync(cancellationToken);

            return labels?.Labels?
                .Where(label => label != null && !string.IsNullOrWhiteSpace(label.Id))
                .Where(IsVisibleGoogleLabel)
                .Select(label => new MailFolder
                {
                    AccountId = account.Id,
                    Id = label.Id ?? "",
                    DisplayName = label.Name ?? label.Id ?? "",
                    UnreadCount = label.MessagesUnread
                    ,IsUserLabel = string.Equals(label.Type, "user", StringComparison.OrdinalIgnoreCase)
                })
                .OrderByDescending(folder => folder.Id == "INBOX")
                .ThenBy(folder => folder.DisplayName)
                .ToList() ?? new List<MailFolder>();
        }

        private async Task<MailProviderPage> FetchGoogleMessagesAsync(MailAccount account, MailFolder folder, bool unreadOnly, int? pageSize, MailCursor? cursor, CancellationToken cancellationToken)
        {
            if (!account.IsSetupComplete || folder.IsPlaceholder)
                return new MailProviderPage();
            if (cursor != null && cursor.ProviderKind != MailAccountKind.Google)
                throw new InvalidOperationException("Mail continuation does not match the account provider.");

            var gmail = await EnsureGoogleMailReadAuthorizedAsync(cancellationToken);
            int top = Math.Clamp(pageSize ?? PageSize, MinPageSize, MaxPageSize);

            var listRequest = gmail.Users.Messages.List("me");
            listRequest.LabelIds = folder.Id;
            listRequest.MaxResults = top;
            listRequest.PageToken = cursor?.Value;
            if (unreadOnly)
                listRequest.Q = "is:unread";

            var list = await listRequest.ExecuteAsync(cancellationToken);
            if (list?.Messages == null || list.Messages.Count == 0)
                return new MailProviderPage();

            var messageRefs = list.Messages
                .Where(message => !string.IsNullOrWhiteSpace(message.Id))
                .ToList();

            var messages = await FetchGoogleMessageMetadataBatchAsync(gmail, account.Id, folder.Id, messageRefs, cancellationToken);
            if (messages.Count != messageRefs.Count)
                throw new InvalidOperationException("One or more Gmail messages could not be loaded; the continuation was not advanced.");
            return new MailProviderPage
            {
                Items = messages.OrderByDescending(message => message.RawReceivedTime).ToList(),
                NextCursor = string.IsNullOrWhiteSpace(list.NextPageToken)
                    ? null
                    : new MailCursor { ProviderKind = MailAccountKind.Google, Value = list.NextPageToken }
            };
        }

        private async Task<List<MailItem>> FetchGoogleMessageMetadataBatchAsync(
            GmailService gmail,
            string accountId,
            string folderId,
            IReadOnlyList<GmailMessage> messageRefs,
            CancellationToken cancellationToken)
        {
            if (messageRefs.Count == 0) return new List<MailItem>();

            try
            {
                var batch = new BatchRequest(gmail);
                var results = new ConcurrentDictionary<string, MailItem>(StringComparer.Ordinal);

                foreach (var messageRef in messageRefs)
                {
                    var id = messageRef.Id;
                    if (string.IsNullOrWhiteSpace(id)) continue;

                    var get = gmail.Users.Messages.Get("me", id);
                    get.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
                    get.MetadataHeaders = new[] { "From", "To", "Subject", "Date" };

                    batch.Queue<GmailMessage>(get, (content, error, _, _) =>
                    {
                        if (error != null || content == null || string.IsNullOrWhiteSpace(content.Id)) return;
                        results[content.Id] = ToGoogleMailItem(accountId, folderId, content);
                    });
                }

                await batch.ExecuteAsync(cancellationToken);

                var missingRefs = messageRefs
                    .Where(message => !string.IsNullOrWhiteSpace(message.Id) && !results.ContainsKey(message.Id))
                    .ToList();
                if (missingRefs.Count > 0)
                {
                    var fallbackItems = await FetchGoogleMessageMetadataIndividuallyAsync(gmail, accountId, folderId, missingRefs, cancellationToken);
                    foreach (var item in fallbackItems)
                        results[item.Id] = item;
                }

                if (results.Count > 0)
                    return messageRefs
                        .Select(message => message.Id)
                        .Where(id => !string.IsNullOrWhiteSpace(id) && results.ContainsKey(id))
                        .Select(id => results[id!])
                        .ToList();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Gmail metadata batch failed, falling back to individual requests: {ex.Message}");
            }

            return await FetchGoogleMessageMetadataIndividuallyAsync(gmail, accountId, folderId, messageRefs, cancellationToken);
        }

        private async Task<List<MailItem>> FetchGoogleMessageMetadataIndividuallyAsync(
            GmailService gmail,
            string accountId,
            string folderId,
            IReadOnlyList<GmailMessage> messageRefs,
            CancellationToken cancellationToken)
        {
            using var metadataGate = new SemaphoreSlim(MaxConcurrentGoogleMessageMetadataRequests);
            var tasks = messageRefs
                .Where(message => !string.IsNullOrWhiteSpace(message.Id))
                .Select(async message =>
                {
                    await metadataGate.WaitAsync(cancellationToken);
                    try
                    {
                        var get = gmail.Users.Messages.Get("me", message.Id);
                        get.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
                        get.MetadataHeaders = new[] { "From", "To", "Subject", "Date" };
                        return ToGoogleMailItem(accountId, folderId, await get.ExecuteAsync(cancellationToken));
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Gmail metadata request failed for {message.Id}: {ex.Message}");
                        return null;
                    }
                    finally
                    {
                        metadataGate.Release();
                    }
                });

            return (await Task.WhenAll(tasks))
                .Where(item => item != null)
                .Cast<MailItem>()
                .ToList();
        }

        private async Task EnsureOutlookMailReadAuthorizedAsync(CancellationToken cancellationToken = default)
        {
            await EnsureOutlookMailAuthorizedAsync(requireWrite: false, requireSend: false, cancellationToken);
        }

        private async Task EnsureOutlookMailWriteAuthorizedAsync(CancellationToken cancellationToken = default)
        {
            await EnsureOutlookMailAuthorizedAsync(requireWrite: true, requireSend: false, cancellationToken);
        }

        private async Task EnsureOutlookMailSendAuthorizedAsync(CancellationToken cancellationToken = default)
        {
            await EnsureOutlookMailAuthorizedAsync(requireWrite: false, requireSend: true, cancellationToken);
        }

        private async Task EnsureOutlookMailAuthorizedAsync(bool requireWrite, bool requireSend, CancellationToken cancellationToken = default)
        {
            if (App.Current is App app &&
                app.SyncManager.GetProvider("Microsoft") is MicrosoftSyncProvider microsoftProvider)
            {
                _outlookClient = await microsoftProvider.EnsureMailAuthorizedAsync(requireWrite, requireSend, cancellationToken);
            }

            if (_outlookClient == null)
                throw new InvalidOperationException("Microsoft provider is not available.");
        }

        private async Task EnsureProviderAgendaAccountAsync(string providerName)
        {
            if (App.Current is not App app) return;

            var accountManager = app.SyncManager.AccountManager;
            if (accountManager.IsConnected(providerName)) return;

            if (app.SyncManager.GetProvider(providerName) is not ISyncProvider provider) return;

            var connected = new ConnectedAccountInfo { ProviderName = providerName };
            try
            {
                var calendars = await provider.FetchCalendarListAsync();
                foreach (var calendar in calendars)
                    connected.Calendars.Add(calendar);
            }
            catch { }

            accountManager.AddAccount(connected);
        }

        private void EnsureAccountsLoaded()
        {
            if (_accountsLoaded) return;
            _accountsLoaded = true;

            try
            {
                string? json = LocalSqliteStore.ReadProtectedText("mail", "accounts");
                _accounts = JsonFallbackPolicy.DeserializeOrDefault(
                    json,
                    value => JsonSerializer.Deserialize(value, AppJsonContext.Default.ListMailAccount),
                    () => new List<MailAccount>());
            }
            catch
            {
                _accounts = new List<MailAccount>();
            }
        }

        private void SaveAccounts()
        {
            // An account was added / edited / removed — reset poll backoff so a freshly
            // re-authorised or reconfigured account is retried on the next poll instead
            // of waiting out its previous failure window, and drop persistent IMAP
            // connections so changed credentials / servers force a clean reconnect.
            _pollBackoff.Clear();
            DisconnectAllPollImapClients();
            string json = JsonSerializer.Serialize(_accounts, AppJsonContext.Default.ListMailAccount);
            QueueAccountStoreWrite(json);
        }

        private void QueueAccountStoreWrite(string json)
        {
            lock (_accountSaveQueueLock)
            {
                _accountSaveQueue = _accountSaveQueue.ContinueWith(
                    _ =>
                    {
                        try
                        {
                            LocalSqliteStore.WriteProtectedText("mail", "accounts", json);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Mail account save failed: {ex.Message}");
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);
            }
        }

        public async Task FlushPendingSavesAsync()
        {
            Task accountSaveTask;
            lock (_accountSaveQueueLock)
                accountSaveTask = _accountSaveQueue;

            await accountSaveTask;
            await FlushPersistentCacheAsync();
        }

        private async Task FlushPersistentCacheAsync()
        {
            CancellationTokenSource? pendingSave;
            string? json;
            long version;

            lock (_persistentCacheSaveLock)
            {
                pendingSave = _persistentCacheSaveCts;
                if (pendingSave == null)
                    return;

                _persistentCacheSaveCts = null;
                version = ++_persistentCacheVersion;
            }

            json = SerializePersistentCache("Serialize mail cache during flush failed");

            try
            {
                pendingSave.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            if (!string.IsNullOrWhiteSpace(json))
                await WritePersistentCacheSnapshotAsync(json, version);
        }

        private static string GetAppDataPath()
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskFlyout");

        private static MailItem ToOutlookMailItem(string accountId, string folderId, GraphMessage message)
        {
            var received = message.ReceivedDateTime;
            return new MailItem
            {
                AccountId = accountId,
                FolderId = folderId,
                Id = message.Id ?? "",
                Subject = string.IsNullOrWhiteSpace(message.Subject) ? "(No subject)" : message.Subject,
                Sender = message.From?.EmailAddress?.Name
                    ?? message.From?.EmailAddress?.Address
                    ?? "",
                SenderAddress = message.From?.EmailAddress?.Address ?? "",
                Recipient = FormatGraphRecipients(message.ToRecipients),
                Preview = message.BodyPreview ?? "",
                BodyText = "",
                HtmlBody = "",
                RawReceivedTime = received,
                ReceivedTime = FormatReceivedTime(received),
                IsRead = message.IsRead == true,
                IsFlagged = message.Flag?.FlagStatus == FollowupFlagStatus.Flagged,
                HasAttachments = message.HasAttachments == true,
                Importance = message.Importance?.ToString() ?? "",
                WebLink = message.WebLink ?? ""
            };
        }

        private static MimeMessage CreateMimeMessage(MailAccount account, string to, string subject, string body, IReadOnlyList<MailAttachmentData> attachments)
        {
            var message = new MimeMessage();
            message.MessageId = MimeKit.Utils.MimeUtils.GenerateMessageId();
            message.From.Add(MailboxAddress.Parse(string.IsNullOrWhiteSpace(account.Address) ? account.ImapUserName : account.Address));
            foreach (var address in ParseRecipients(to))
                message.To.Add(MailboxAddress.Parse(address));

            message.Subject = subject;
            var builder = new BodyBuilder { TextBody = body };
            foreach (var attachment in attachments)
                builder.Attachments.Add(attachment.FileName, attachment.Content, MimeKit.ContentType.Parse(attachment.ContentType));
            message.Body = builder.ToMessageBody();
            return message;
        }

        private static SecureSocketOptions GetSmtpSocketOptions(MailAccount account)
        {
            if (account.SmtpPort == 465)
                return SecureSocketOptions.SslOnConnect;

            if (account.SmtpPort == 587)
                return SecureSocketOptions.StartTls;

            return SecureSocketOptions.StartTls;
        }

        private static List<string> ParseRecipients(string value)
        {
            return value
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(address => address.Trim())
                .Where(address => !string.IsNullOrWhiteSpace(address))
                .ToList();
        }

        private static string ToBase64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static MailItem ToGoogleMailItem(string accountId, string folderId, GmailMessage message)
        {
            string subject = GetGoogleHeader(message, "Subject");
            string sender = GetGoogleHeader(message, "From");
            string senderAddress = ExtractEmailAddress(sender);
            string recipient = GetGoogleHeader(message, "To");
            string preview = message.Snippet ?? "";
            DateTimeOffset? received = null;
            if (message.InternalDate.HasValue)
                received = DateTimeOffset.FromUnixTimeMilliseconds(message.InternalDate.Value);

            return new MailItem
            {
                AccountId = accountId,
                FolderId = folderId,
                Id = message.Id ?? "",
                Subject = string.IsNullOrWhiteSpace(subject) ? "(No subject)" : subject,
                Sender = sender,
                SenderAddress = senderAddress,
                Recipient = recipient,
                Preview = preview,
                BodyText = "",
                HtmlBody = "",
                RawReceivedTime = received,
                ReceivedTime = FormatReceivedTime(received),
                IsRead = message.LabelIds?.Contains("UNREAD") != true,
                IsFlagged = message.LabelIds?.Contains("STARRED") == true,
                HasAttachments = HasGoogleAttachments(message.Payload),
                WebLink = string.IsNullOrWhiteSpace(message.Id) ? "" : $"https://mail.google.com/mail/u/0/#all/{message.Id}"
            };
        }

        private static MailItem ToImapMailItem(string accountId, string folderId, string id, uint uidValidity, MimeMessage message, bool isRead)
        {
            string rawText = message.TextBody ?? "";
            string htmlBody = !string.IsNullOrWhiteSpace(message.HtmlBody)
                ? message.HtmlBody
                : HasHtmlContentTags(rawText) ? rawText : "";
            string body = CleanMailBody(!string.IsNullOrWhiteSpace(htmlBody) ? StripHtml(htmlBody) : rawText);
            string preview = Truncate(body, 240);

            return new MailItem
            {
                AccountId = accountId,
                FolderId = folderId,
                Id = id,
                ImapUidValidity = uidValidity,
                Subject = string.IsNullOrWhiteSpace(message.Subject) ? "(No subject)" : message.Subject,
                Sender = FormatInternetAddressList(message.From),
                SenderAddress = message.From?.Mailboxes?.FirstOrDefault()?.Address ?? "",
                Recipient = FormatInternetAddressList(message.To),
                Preview = preview.Trim(),
                BodyText = "",
                HtmlBody = "",
                RawReceivedTime = message.Date,
                ReceivedTime = FormatReceivedTime(message.Date),
                IsRead = isRead,
                HasAttachments = message.Attachments.Any()
            };
        }

        private static string StripHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return "";
            var value = RemoveNonContentHtmlBlocks(html);
            return WebUtility.HtmlDecode(Regex.Replace(value, "<.*?>", " ").Replace("&nbsp;", " "));
        }

        private static string CleanMailBody(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            value = RemoveNonContentHtmlBlocks(value);
            value = RemoveCssNoise(value);
            return Regex.Replace(WebUtility.HtmlDecode(value), @"[ \t]{2,}", " ").Trim();
        }

        private static bool HasHtmlContentTags(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            return Regex.IsMatch(value, @"<\s*(html|body|table|tr|td|div|span|p|br|img|a|h[1-6]|ul|ol|li)\b", RegexOptions.IgnoreCase);
        }

        private static string RemoveNonContentHtmlBlocks(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";

            value = Regex.Replace(value, @"<\s*(head|script|noscript|svg)\b[^>]*>.*?<\s*/\s*\1\s*>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            value = Regex.Replace(value, @"<\s*(meta|link)\b[^>]*/?\s*>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return value;
        }

        private static string RemoveCssNoise(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";

            var lines = value
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n');

            var kept = new List<string>();
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;
                if (IsCssNoiseLine(trimmed)) continue;
                kept.Add(line);
            }

            return string.Join("\n", kept);
        }

        private static bool IsCssNoiseLine(string line)
        {
            if (line == "{" || line == "}") return true;
            if (line.StartsWith("@media", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("@font-face", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("@-moz", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("@supports", StringComparison.OrdinalIgnoreCase))
                return true;
            if (Regex.IsMatch(line, @"^[.#][\w\-#.:\s,>+~\[\]=""']+\{?$")) return true;
            if (Regex.IsMatch(line, @"^[a-zA-Z\-]+\s*:\s*[^。！？；，、]*;?$")) return true;
            if (Regex.IsMatch(line, @"^[a-zA-Z][\w\-#.\s,>+~\[\]=""']+\s*\{")) return true;
            return false;
        }

        private bool TryGetCachedFolders(string key, out List<MailFolder> folders)
        {
            lock (_mailCacheLock)
            {
                var now = DateTimeOffset.UtcNow;
                if (_folderCache.TryGetValue(key, out var entry) && now - entry.CreatedAt < CacheLifetime)
                {
                    folders = ApplyFolderOrder(key, entry.Value);
                    return true;
                }

                EnsurePersistentCacheLoaded();
                if (_persistentCache?.Folders.TryGetValue(key, out var persistentFolders) == true &&
                    _persistentCache.FolderFetchedUtcTicks.TryGetValue(key, out var fetchedUtcTicks) &&
                    MailPersistentCachePolicy.IsFresh(fetchedUtcTicks, now, CacheLifetime))
                {
                    folders = ApplyFolderOrder(key, persistentFolders);
                    _folderCache[key] = new CacheEntry<List<MailFolder>>
                    {
                        CreatedAt = new DateTimeOffset(fetchedUtcTicks, TimeSpan.Zero),
                        Value = folders
                    };
                    return true;
                }
            }

            folders = new List<MailFolder>();
            return false;
        }

        internal IReadOnlyList<MailFolder> GetCachedFolderSnapshot(string accountId)
        {
            EnsurePersistentCacheLoaded();
            lock (_mailCacheLock)
            {
                IEnumerable<MailFolder>? folders = null;
                if (_persistentCache?.Folders.TryGetValue(accountId, out var persistentFolders) == true)
                    folders = persistentFolders;
                else if (_folderCache.TryGetValue(accountId, out var entry))
                    folders = entry.Value;

                return folders == null
                    ? Array.Empty<MailFolder>()
                    : ApplyFolderOrder(accountId, folders).Select(CloneMailFolder).ToArray();
            }
        }

        private bool TryGetCachedMessages(string key, out MailMessageWindow window)
        {
            lock (_mailCacheLock)
            {
                var now = DateTimeOffset.UtcNow;
                if (_messageCache.TryGetValue(key, out var entry) && now - entry.CreatedAt < CacheLifetime)
                {
                    EnsurePersistentCacheLoaded();
                    if (_persistentCache?.MessageHasMore.ContainsKey(key) != true)
                    {
                        window = new MailMessageWindow();
                        return false;
                    }
                    window = new MailMessageWindow
                    {
                        Items = CloneMailItems(entry.Value, includeBodies: false),
                        HasMore = _persistentCache?.MessageHasMore.TryGetValue(key, out var hasMore) == true && hasMore
                    };
                    return true;
                }

                EnsurePersistentCacheLoaded();
                if (_persistentCache?.Messages.TryGetValue(key, out var persistentMessages) == true &&
                    _persistentCache.MessageFetchedUtcTicks.TryGetValue(key, out var fetchedUtcTicks) &&
                    MailPersistentCachePolicy.IsFresh(fetchedUtcTicks, now, CacheLifetime))
                {
                    if (!_persistentCache.MessageHasMore.ContainsKey(key))
                    {
                        window = new MailMessageWindow();
                        return false;
                    }
                    var cachedMessages = StripBodies(persistentMessages);
                    _persistentCache.Messages[key] = cachedMessages;
                    _messageCache[key] = new CacheEntry<List<MailItem>>
                    {
                        CreatedAt = new DateTimeOffset(fetchedUtcTicks, TimeSpan.Zero),
                        Value = cachedMessages
                    };
                    window = new MailMessageWindow
                    {
                        Items = CloneMailItems(cachedMessages, includeBodies: false),
                        HasMore = _persistentCache.MessageHasMore.TryGetValue(key, out var hasMore) && hasMore
                    };
                    return true;
                }
            }

            window = new MailMessageWindow();
            return false;
        }

        internal bool TryGetCachedMessageWindowSnapshot(
            string accountId,
            string folderId,
            bool unreadOnly,
            out MailMessageWindow window)
        {
            EnsurePersistentCacheLoaded();
            string key = GetMessageCacheKey(accountId, folderId, unreadOnly);
            lock (_mailCacheLock)
            {
                var cache = _persistentCache;
                if (cache == null ||
                    !cache.Messages.TryGetValue(key, out var messages) ||
                    messages == null ||
                    !cache.MessageHasMore.TryGetValue(key, out var hasMore))
                {
                    window = new MailMessageWindow();
                    return false;
                }

                window = new MailMessageWindow
                {
                    Items = CloneMailItems(messages, includeBodies: false),
                    HasMore = hasMore
                };
                return true;
            }
        }

        private void ClearAccountCache(string accountId)
        {
            ClearAccountVolatileMessageBodies(accountId);
            EnsurePersistentCacheLoaded();
            lock (_mailCacheLock)
            {
                _folderCache.Remove(accountId);
                foreach (var key in _messageCache.Keys.Where(key => key.StartsWith(accountId + "|", StringComparison.Ordinal)).ToList())
                    _messageCache.Remove(key);
                if (_persistentCache == null) return;
                _persistentCache.Folders.Remove(accountId);
                _persistentCache.FolderFetchedUtcTicks.Remove(accountId);
                _persistentCache.AccountOrder.RemoveAll(id => string.Equals(id, accountId, StringComparison.Ordinal));
                _persistentCache.FolderOrder.Remove(accountId);
                foreach (var key in _persistentCache.Messages.Keys.Where(key => key.StartsWith(accountId + "|", StringComparison.Ordinal)).ToList())
                    _persistentCache.Messages.Remove(key);
                foreach (var key in _persistentCache.MessageFetchedUtcTicks.Keys.Where(key => key.StartsWith(accountId + "|", StringComparison.Ordinal)).ToList())
                    _persistentCache.MessageFetchedUtcTicks.Remove(key);
                foreach (var key in _persistentCache.MessageCursors.Keys.Where(key => key.StartsWith(accountId + "|", StringComparison.Ordinal)).ToList())
                    _persistentCache.MessageCursors.Remove(key);
                foreach (var key in _persistentCache.MessageHasMore.Keys.Where(key => key.StartsWith(accountId + "|", StringComparison.Ordinal)).ToList())
                    _persistentCache.MessageHasMore.Remove(key);
                MailPendingMutationPolicy.RemoveAccount(_persistentCache.PendingMutations, accountId);
                foreach (var key in _persistentCache.LastSeenInboxTicks.Keys.Where(key => key.StartsWith(accountId + "|", StringComparison.Ordinal)).ToList())
                    _persistentCache.LastSeenInboxTicks.Remove(key);
            }
            SavePersistentCache();
        }

        private void UpdateCachedMutation(
            MailAccountKind providerKind,
            MailItem item,
            MailMutationKind kind,
            bool value,
            IReadOnlyCollection<string> directlyChangedFolderIds)
        {
            EnsurePersistentCacheLoaded();
            lock (_mailCacheLock)
            {
                var changedFolderIds = new HashSet<string>(directlyChangedFolderIds, StringComparer.Ordinal);
                foreach (var pair in _messageCache.ToList())
                {
                    var result = MailMutationCachePolicy.Apply(pair.Value.Value, providerKind, item, kind, value);
                    changedFolderIds.UnionWith(result.ReadStateChangedFolderIds);
                }

                if (_persistentCache != null)
                {
                    foreach (var pair in _persistentCache.Messages.ToList())
                    {
                        var result = MailMutationCachePolicy.Apply(pair.Value, providerKind, item, kind, value);
                        changedFolderIds.UnionWith(result.ReadStateChangedFolderIds);
                    }
                }

                if (kind == MailMutationKind.SetReadState && changedFolderIds.Count > 0)
                {
                    foreach (var folderId in changedFolderIds)
                    {
                        var unreadKey = GetMessageCacheKey(item.AccountId, folderId, true);
                        _messageCache.Remove(unreadKey);
                        _persistentCache?.Messages.Remove(unreadKey);
                        _persistentCache?.MessageFetchedUtcTicks.Remove(unreadKey);
                        _persistentCache?.MessageCursors.Remove(unreadKey);
                        _persistentCache?.MessageHasMore.Remove(unreadKey);
                    }

                    var cachedFolders = new HashSet<MailFolder>();
                    if (_folderCache.TryGetValue(item.AccountId, out var folderEntry))
                        cachedFolders.UnionWith(folderEntry.Value);
                    if (_persistentCache?.Folders.TryGetValue(item.AccountId, out var persistentFolders) == true)
                        cachedFolders.UnionWith(persistentFolders);

                    foreach (var folder in cachedFolders.Where(folder => changedFolderIds.Contains(folder.Id)))
                        folder.UnreadCount = MailMutationCachePolicy.AdjustUnreadCount(folder.UnreadCount, !value, value);
                }
            }
            SavePersistentCache();
            if (kind == MailMutationKind.SetReadState && value)
                QueueMailNotificationRemoval(providerKind, new[] { item });
            PublishCacheUpdate(
                item.AccountId,
                item.FolderId,
                kind == MailMutationKind.SetReadState
                    ? MailCacheRefreshKind.Folders | MailCacheRefreshKind.Messages
                    : MailCacheRefreshKind.Messages);
        }

        private static string GetMessageCacheKey(string accountId, string folderId, bool unreadOnly)
            => MailCacheKeyPolicy.Build(accountId, folderId, unreadOnly);

        private void EnsurePersistentCacheLoaded()
        {
            lock (_mailCacheLock)
            {
                if (_persistentCacheLoaded) return;
                _persistentCacheLoaded = true;

                try
                {
                    var json = _cacheRepository.Load();
                    _persistentCache = JsonFallbackPolicy.DeserializeOrDefault(
                        json,
                        value => JsonSerializer.Deserialize(value, AppJsonContext.Default.MailPersistentCache),
                        () => new MailPersistentCache());
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Mail cache load failed: {ex.Message}");
                }

                _persistentCache ??= new MailPersistentCache();
                var now = DateTimeOffset.UtcNow;
                bool dirty = MailPersistentCachePolicy.Normalize(
                    _persistentCache,
                    now,
                    MaxPageSize,
                    removeExpiredMutations: false);
                var expired = MailPendingMutationPolicy.RemoveExpiredItems(_persistentCache.PendingMutations, now);
                foreach (var mutation in expired)
                    InvalidatePendingMutationCacheLocked(mutation);
                if (dirty || expired.Count > 0)
                    SavePersistentCache();
            }
        }

        private void InvalidatePendingMutationCacheLocked(PendingMailMutation mutation)
        {
            if (_persistentCache == null) return;

            var target = new MailItem
            {
                AccountId = mutation.AccountId,
                FolderId = mutation.FolderId,
                Id = mutation.MessageId,
                ImapUidValidity = mutation.ImapUidValidity
            };
            var affectedFolderIds = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(mutation.FolderId))
                affectedFolderIds.Add(mutation.FolderId);
            var affectedKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var pair in _messageCache)
            {
                var matches = pair.Value.Value
                    .Where(item => MailMutationCachePolicy.IsSameProviderIdentity(item, target, mutation.ProviderKind))
                    .ToList();
                if (matches.Count == 0) continue;
                affectedKeys.Add(pair.Key);
                affectedFolderIds.UnionWith(matches.Select(item => item.FolderId));
            }

            foreach (var pair in _persistentCache.Messages)
            {
                var matches = pair.Value
                    .Where(item => MailMutationCachePolicy.IsSameProviderIdentity(item, target, mutation.ProviderKind))
                    .ToList();
                if (matches.Count == 0) continue;
                affectedKeys.Add(pair.Key);
                affectedFolderIds.UnionWith(matches.Select(item => item.FolderId));
            }

            foreach (var folderId in affectedFolderIds.Where(id => !string.IsNullOrWhiteSpace(id)))
            {
                affectedKeys.Add(GetMessageCacheKey(mutation.AccountId, folderId, unreadOnly: false));
                affectedKeys.Add(GetMessageCacheKey(mutation.AccountId, folderId, unreadOnly: true));
            }

            foreach (var key in affectedKeys)
            {
                _messageCache.Remove(key);
                _persistentCache.Messages.Remove(key);
                _persistentCache.MessageFetchedUtcTicks.Remove(key);
                _persistentCache.MessageCursors.Remove(key);
                _persistentCache.MessageHasMore.Remove(key);
            }

            _folderCache.Remove(mutation.AccountId);
            _persistentCache.Folders.Remove(mutation.AccountId);
            _persistentCache.FolderFetchedUtcTicks.Remove(mutation.AccountId);
        }

        private void SavePersistentCache()
        {
            EnsurePersistentCacheLoaded();

            CancellationTokenSource cts;
            long version;
            lock (_persistentCacheSaveLock)
            {
                _persistentCacheSaveCts?.Cancel();
                _persistentCacheSaveCts = new CancellationTokenSource();
                cts = _persistentCacheSaveCts;
                version = ++_persistentCacheVersion;
            }

            _ = SavePersistentCacheAfterDelayAsync(cts, version);
        }

        private async Task SavePersistentCacheAfterDelayAsync(CancellationTokenSource cts, long version)
        {
            try
            {
                await Task.Delay(PersistentCacheSaveDebounceMs, cts.Token);

                string? json;
                lock (_persistentCacheSaveLock)
                {
                    if (!ReferenceEquals(cts, _persistentCacheSaveCts))
                        return;
                }

                json = SerializePersistentCache("Serialize mail cache failed");

                if (!string.IsNullOrWhiteSpace(json))
                    await WritePersistentCacheSnapshotAsync(json, version);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Save mail cache failed: {ex.Message}");
            }
            finally
            {
                lock (_persistentCacheSaveLock)
                {
                    if (ReferenceEquals(cts, _persistentCacheSaveCts))
                        _persistentCacheSaveCts = null;
                }

                cts.Dispose();
            }
        }

        private async Task WritePersistentCacheSnapshotAsync(string json, long version)
        {
            await _persistentCacheWriteGate.WaitAsync();
            try
            {
                if (version <= _lastPersistedCacheVersion) return;
                await _cacheRepository.SaveAsync(json);
                _lastPersistedCacheVersion = version;
            }
            finally
            {
                _persistentCacheWriteGate.Release();
            }
        }

        private string? SerializePersistentCache(string errorPrefix)
        {
            lock (_mailCacheLock)
            {
                if (!_persistentCacheLoaded || _persistentCache == null) return null;
                try
                {
                    return JsonSerializer.Serialize(_persistentCache, AppJsonContext.Default.MailPersistentCache);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"{errorPrefix}: {ex.Message}");
                    return null;
                }
            }
        }

        private void UpdateFolderWindow(string key, List<MailFolder> folders)
        {
            EnsurePersistentCacheLoaded();
            var fetchedAt = DateTimeOffset.UtcNow;
            lock (_mailCacheLock)
            {
                if (_persistentCache == null) return;
                var orderedFolders = ApplyFolderOrder(key, folders);
                _persistentCache.Folders[key] = orderedFolders;
                _persistentCache.FolderFetchedUtcTicks[key] = fetchedAt.UtcTicks;
                _folderCache[key] = new CacheEntry<List<MailFolder>> { CreatedAt = fetchedAt, Value = orderedFolders };
            }

            SavePersistentCache();
            PublishCacheUpdate(key, null, MailCacheRefreshKind.Folders);
        }

        private List<MailAccount> ApplyAccountOrder(IEnumerable<MailAccount> accounts)
        {
            EnsurePersistentCacheLoaded();
            var accountList = accounts.ToList();
            lock (_mailCacheLock)
                return PersistedOrderPolicy.Apply(accountList, _persistentCache?.AccountOrder, account => account.Id);
        }

        private List<MailFolder> ApplyFolderOrder(string accountId, IEnumerable<MailFolder> folders)
        {
            EnsurePersistentCacheLoaded();
            var folderList = folders.ToList();
            lock (_mailCacheLock)
            {
                if (_persistentCache == null ||
                    !_persistentCache.FolderOrder.TryGetValue(accountId, out var order) ||
                    order == null ||
                    order.Count == 0)
                    return folderList;

                return PersistedOrderPolicy.Apply(folderList, order, folder => folder.Id);
            }
        }

        private MailMessageWindow CommitMessagePage(
            MailAccount account,
            MailFolder folder,
            bool unreadOnly,
            string key,
            MailProviderPage page,
            bool append)
        {
            EnsurePersistentCacheLoaded();
            List<MailItem> windowItems;
            IReadOnlyCollection<MailItem> removedNotifications = Array.Empty<MailItem>();
            bool hasMore;
            var fetchedAt = DateTimeOffset.UtcNow;
            lock (_mailCacheLock)
            {
                if (_persistentCache == null) return new MailMessageWindow();
                var currentItems = _persistentCache.Messages.TryGetValue(key, out var current)
                    ? current
                    : new List<MailItem>();
                var pending = _persistentCache.PendingMutations
                    .Where(mutation => mutation.AccountId == account.Id && mutation.ProviderKind == account.Kind)
                    .ToList();

                if (unreadOnly)
                {
                    var existingUnreadItems = EnumerateCachedMessagesLocked()
                        .SelectMany(messages => messages)
                        .Where(item => string.Equals(item.AccountId, account.Id, StringComparison.Ordinal) &&
                                       string.Equals(item.FolderId, folder.Id, StringComparison.Ordinal) &&
                                       !item.IsRead)
                        .ToList();
                    var providerItems = append ? currentItems.Concat(page.Items) : page.Items;
                    var reconciliation = MailUnreadSnapshotPolicy.Reconcile(
                        existingUnreadItems,
                        providerItems,
                        isComplete: !append && !page.HasMore,
                        account.Kind,
                        account.Id,
                        folder.Id,
                        pending,
                        MaxPageSize);
                    windowItems = reconciliation.Items;
                    removedNotifications = reconciliation.RemovedItems;
                }
                else
                {
                    windowItems = (append ? currentItems.Concat(page.Items) : page.Items)
                        .GroupBy(item => item.Id, StringComparer.Ordinal)
                        .Select(group => group.First())
                        .OrderByDescending(item => item.RawReceivedTime)
                        .Take(MaxPageSize)
                        .ToList();
                    MailUnreadSnapshotPolicy.ApplyPendingMutations(windowItems, account.Kind, account.Id, pending);
                }
                hasMore = page.HasMore && windowItems.Count < MaxPageSize;

                _persistentCache.Messages[key] = StripBodies(windowItems);
                _persistentCache.MessageFetchedUtcTicks[key] = fetchedAt.UtcTicks;
                if (!hasMore || page.NextCursor == null)
                    _persistentCache.MessageCursors.Remove(key);
                else
                    _persistentCache.MessageCursors[key] = page.NextCursor;
                _persistentCache.MessageHasMore[key] = hasMore;
                _messageCache[key] = new CacheEntry<List<MailItem>> { CreatedAt = fetchedAt, Value = StripBodies(windowItems) };
            }
            SavePersistentCache();
            QueueMailNotificationRemoval(account.Kind, removedNotifications);
            PublishCacheUpdate(account.Id, folder.Id, MailCacheRefreshKind.Messages);
            return new MailMessageWindow
            {
                Items = CloneMailItems(windowItems, includeBodies: false),
                HasMore = hasMore
            };
        }

        private void InvalidateMessageCursor(string key)
        {
            EnsurePersistentCacheLoaded();
            lock (_mailCacheLock)
            {
                if (_persistentCache == null) return;
                _persistentCache.MessageCursors.Remove(key);
                _persistentCache.MessageHasMore[key] = false;
            }
            SavePersistentCache();
        }

        private static List<MailItem> StripBodies(List<MailItem> messages)
            => CloneMailItems(messages, includeBodies: false);

        private void UpdatePersistentMessageBody(MailItem item)
        {
            EnsurePersistentCacheLoaded();
            bool changed = false;
            lock (_mailCacheLock)
            {
                if (_persistentCache == null) return;
                foreach (var messages in _persistentCache.Messages.Values)
                {
                    var existing = messages.FirstOrDefault(m => m.Id == item.Id && m.AccountId == item.AccountId);
                    if (existing != null && (!string.IsNullOrEmpty(existing.BodyText) || !string.IsNullOrEmpty(existing.HtmlBody)))
                    {
                        existing.BodyText = "";
                        existing.HtmlBody = "";
                        changed = true;
                    }
                }
            }

            if (changed)
                SavePersistentCache();
        }

        private static void LimitMailBody(MailItem item)
        {
            item.BodyText = Truncate(item.BodyText ?? "", MaxBodyTextChars);
            item.HtmlBody = Truncate(item.HtmlBody ?? "", MaxHtmlBodyChars);
        }

        private List<MailItem> MergeMessagesIntoPersistentCache(MailAccount account, MailFolder folder, MailProviderPage page)
        {
            EnsurePersistentCacheLoaded();
            var strippedProviderItems = StripBodies(page.Items);
            var currentProviderItems = new List<MailItem>();
            var removedNotifications = new List<MailItem>();
            bool changed = false;
            lock (_mailCacheLock)
            {
                if (_persistentCache == null) return currentProviderItems;
                var pending = _persistentCache.PendingMutations
                    .Where(mutation => mutation.AccountId == account.Id && mutation.ProviderKind == account.Kind)
                    .ToList();
                MailUnreadSnapshotPolicy.ApplyPendingMutations(
                    strippedProviderItems,
                    account.Kind,
                    account.Id,
                    pending);
                currentProviderItems = strippedProviderItems.Where(item => !item.IsRead).ToList();

                foreach (bool unreadOnly in new[] { true, false })
                {
                    var key = GetMessageCacheKey(account.Id, folder.Id, unreadOnly);
                    List<MailItem> existing;
                    if (_persistentCache.Messages.TryGetValue(key, out var cached))
                    {
                        existing = StripBodies(cached);
                    }
                    else if (_messageCache.TryGetValue(key, out var memoryEntry))
                    {
                        existing = StripBodies(memoryEntry.Value);
                    }
                    else if (unreadOnly)
                    {
                        // Keep a bounded unread slice for notification targets even
                        // when Mail has never been opened. Do not create a partial
                        // all-mail window: that would hide older messages on a later
                        // normal folder visit.
                        existing = new List<MailItem>();
                    }
                    else
                        continue;

                    List<MailItem> merged;
                    if (unreadOnly)
                    {
                        var existingUnreadItems = EnumerateCachedMessagesLocked()
                            .SelectMany(messages => messages)
                            .Where(item => string.Equals(item.AccountId, account.Id, StringComparison.Ordinal) &&
                                           string.Equals(item.FolderId, folder.Id, StringComparison.Ordinal) &&
                                           !item.IsRead)
                            .ToList();
                        var reconciliation = MailUnreadSnapshotPolicy.Reconcile(
                            existingUnreadItems,
                            currentProviderItems,
                            isComplete: !page.HasMore,
                            account.Kind,
                            account.Id,
                            folder.Id,
                            pending,
                            MaxPageSize);
                        merged = reconciliation.Items;
                        removedNotifications.AddRange(reconciliation.RemovedItems);
                    }
                    else
                    {
                        merged = MailNotificationNavigationPolicy.MergePolledMessages(
                            existing,
                            currentProviderItems,
                            unreadOnly: false,
                            MaxPageSize);
                        MailUnreadSnapshotPolicy.ApplyPendingMutations(merged, account.Kind, account.Id, pending);
                    }

                    _persistentCache.Messages[key] = StripBodies(merged);
                    DateTimeOffset fetchedAt = _messageCache.TryGetValue(key, out var existingEntry)
                        ? existingEntry.CreatedAt
                        : _persistentCache.MessageFetchedUtcTicks.TryGetValue(key, out var fetchedUtcTicks)
                            ? new DateTimeOffset(fetchedUtcTicks, TimeSpan.Zero)
                            : DateTimeOffset.MinValue;
                    if (unreadOnly && !page.HasMore)
                    {
                        fetchedAt = DateTimeOffset.UtcNow;
                        _persistentCache.MessageFetchedUtcTicks[key] = fetchedAt.UtcTicks;
                        _persistentCache.MessageCursors.Remove(key);
                        _persistentCache.MessageHasMore[key] = false;
                    }
                    _messageCache[key] = new CacheEntry<List<MailItem>>
                    {
                        CreatedAt = fetchedAt,
                        Value = StripBodies(_persistentCache.Messages[key])
                    };
                    changed = true;
                }
            }

            if (changed)
            {
                SavePersistentCache();
                QueueMailNotificationRemoval(account.Kind, removedNotifications);
                PublishCacheUpdate(account.Id, folder.Id, MailCacheRefreshKind.Messages);
            }
            return currentProviderItems;
        }

        public MailItem? TryGetCachedMessage(string accountId, string folderId, string messageId)
        {
            EnsurePersistentCacheLoaded();
            lock (_mailCacheLock)
            {

                foreach (var messages in EnumerateCachedMessagesLocked())
                {
                    var item = messages.FirstOrDefault(message =>
                        MailNotificationNavigationPolicy.MessageMatches(
                            message, accountId, folderId, messageId));
                    if (item != null) return CloneMailItem(item, includeBodies: false);
                }
            }

            return null;
        }

        /// <summary>
        /// Finds a cached Gmail/Outlook message by its stable provider identity
        /// when the folder/label in the toast is stale (for example, after a move).
        /// IMAP UIDs are deliberately excluded because they are scoped to a folder.
        /// </summary>
        public MailItem? TryGetCachedMessageById(string accountId, string messageId)
        {
            if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(messageId))
                return null;

            EnsureAccountsLoaded();
            var account = _accounts.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, accountId, StringComparison.Ordinal));
            if (account == null ||
                !MailNotificationNavigationPolicy.CanUseCrossFolderIdentity(account.Kind))
                return null;

            EnsurePersistentCacheLoaded();
            lock (_mailCacheLock)
            {
                var item = EnumerateCachedMessagesLocked()
                    .SelectMany(messages => messages)
                    .Where(message => string.Equals(message.AccountId, accountId, StringComparison.Ordinal)
                                   && string.Equals(message.Id, messageId, StringComparison.Ordinal))
                    .OrderByDescending(message => message.RawReceivedTime)
                    .FirstOrDefault();
                return item == null ? null : CloneMailItem(item, includeBodies: false);
            }
        }

        private IEnumerable<List<MailItem>> EnumerateCachedMessagesLocked()
        {
            foreach (var entry in _messageCache.Values)
                yield return entry.Value;

            if (_persistentCache == null) yield break;
            foreach (var entry in _persistentCache.Messages.Values)
                yield return entry;
        }

        private static List<MailItem> CloneMailItems(IEnumerable<MailItem> messages, bool includeBodies)
            => messages.Select(item => CloneMailItem(item, includeBodies)).ToList();

        private static MailFolder CloneMailFolder(MailFolder folder)
            => new()
            {
                AccountId = folder.AccountId,
                Id = folder.Id,
                DisplayName = folder.DisplayName,
                UnreadCount = folder.UnreadCount,
                IsPlaceholder = folder.IsPlaceholder,
                IsUserLabel = folder.IsUserLabel
            };

        private static MailItem CloneMailItem(MailItem item, bool includeBodies)
            => new()
            {
                AccountId = item.AccountId,
                FolderId = item.FolderId,
                Id = item.Id,
                ImapUidValidity = item.ImapUidValidity,
                Subject = item.Subject,
                Sender = item.Sender,
                SenderAddress = item.SenderAddress,
                Recipient = item.Recipient,
                Preview = item.Preview,
                BodyText = includeBodies ? item.BodyText : "",
                HtmlBody = includeBodies ? item.HtmlBody : "",
                ReceivedTime = item.ReceivedTime,
                RawReceivedTime = item.RawReceivedTime,
                IsRead = item.IsRead,
                IsFlagged = item.IsFlagged,
                HasAttachments = item.HasAttachments,
                Importance = item.Importance,
                WebLink = item.WebLink
            };

        private void PublishCacheUpdate(string accountId, string? folderId, MailCacheRefreshKind kind)
        {
            var handlers = CachePublished;
            if (handlers == null) return;

            long version = Interlocked.Increment(ref _publishedCacheVersion);
            var args = new MailCachePublishedEventArgs(
                version,
                new MailCacheRefreshScope(accountId, folderId, kind));
            _ = Task.Run(() =>
            {
                foreach (EventHandler<MailCachePublishedEventArgs> handler in handlers.GetInvocationList())
                {
                    try { handler(this, args); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Mail cache subscriber failed: {ex.Message}"); }
                }
            });
        }

        private void LoadKnownUnreadIds()
        {
            if (Interlocked.CompareExchange(ref _knownUnreadLoaded, 1, 0) != 0) return;

            var raw = ApplicationData.Current.LocalSettings.Values["MailKnownUnreadIds"] as string ?? "";
            foreach (var id in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                _knownUnreadIds[id] = 0;
        }

        private void SaveKnownUnreadIds()
        {
            ApplicationData.Current.LocalSettings.Values["MailKnownUnreadIds"] = string.Join('\n', _knownUnreadIds.Keys);
        }

        private void RemoveKnownUnreadForAccount(string accountId)
        {
            LoadKnownUnreadIds();
            var prefix = accountId + "|";
            foreach (var key in _knownUnreadIds.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToList())
                _knownUnreadIds.TryRemove(key, out _);
            SaveKnownUnreadIds();
        }

        private long GetLastSeenInboxTicks(string inboxKey)
        {
            EnsurePersistentCacheLoaded();
            lock (_mailCacheLock)
                return _persistentCache?.LastSeenInboxTicks.TryGetValue(inboxKey, out var ticks) == true ? ticks : 0;
        }

        private void SetLastSeenInboxTicks(string inboxKey, long ticks)
        {
            EnsurePersistentCacheLoaded();
            lock (_mailCacheLock)
            {
                if (_persistentCache == null) return;
                _persistentCache.LastSeenInboxTicks[inboxKey] = ticks;
            }
        }

        private static long GetMailReceivedTicks(MailItem item)
            => item.RawReceivedTime?.UtcTicks ?? 0;

        private static string GetMailNotificationKey(MailAccountKind providerKind, MailItem item)
            => $"{item.AccountId}|{MailNotificationIdentityPolicy.BuildTag(providerKind, item)}";

        private static void QueueMailNotificationRemoval(
            MailAccountKind providerKind,
            IEnumerable<MailItem> items)
        {
            foreach (string tag in items
                         .Select(item => MailNotificationIdentityPolicy.BuildTag(providerKind, item))
                         .Distinct(StringComparer.Ordinal))
            {
                _ = RemoveMailNotificationAsync(tag);
            }
        }

        private static async Task RemoveMailNotificationAsync(string tag)
        {
            try
            {
                await AppNotificationManager.Default.RemoveByTagAndGroupAsync(
                    tag,
                    MailNotificationIdentityPolicy.Group);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Remove mail notification failed: {ex.Message}");
            }
        }

        private void SendNewMailNotification(MailAccount account, MailItem item)
        {
            var sender = string.IsNullOrWhiteSpace(item.Sender) ? account.DisplayTitle : item.Sender;
            var subject = string.IsNullOrWhiteSpace(item.Subject) ? "(No subject)" : item.Subject;
            var hideContent = ApplicationData.Current.LocalSettings.Values["HideNotificationContent"] as bool? ?? true;

            try
            {
                var builder = new AppNotificationBuilder()
                    .AddText(hideContent
                        ? (_loader.GetStringOrDefault("TextNewMail") ?? "New Mail")
                        : $"{(_loader.GetStringOrDefault("TextNewMail") ?? "New Mail")} · {account.DisplayTitle}")
                    .AddArgument("action", "openMail")
                    .AddArgument("accountId", item.AccountId)
                    .AddArgument("folderId", item.FolderId)
                    .AddArgument("messageId", item.Id);

                if (!hideContent)
                {
                    builder.AddText(subject)
                        .AddText(sender);
                }

                // Verification-code mail: offer a one-tap "copy code" button on the toast,
                // showing the detected code right on the button.
                if (VerificationCodeDetector.TryExtract(item.Subject, item.Preview, out var code))
                {
                    var copyLabel = _loader.GetStringOrDefault("MailCopyCode") ?? "Copy code";
                    var codeToken = VerificationCodeStore.Store(code);
                    builder.AddButton(new AppNotificationButton(copyLabel)
                        .AddArgument("action", "copyCode")
                        .AddArgument("codeToken", codeToken));
                }

                var notification = builder.BuildNotification();
                notification.Tag = MailNotificationIdentityPolicy.BuildTag(account.Kind, item);
                notification.Group = MailNotificationIdentityPolicy.Group;
                AppNotificationManager.Default.Show(notification);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"New mail notification failed: {ex.Message}");
            }

            NewMailArrived?.Invoke(this, new NewMailNotificationEventArgs
            {
                Account = account,
                Item = item
            });
        }

        private static bool IsVisibleGoogleLabel(Label label)
        {
            string id = label.Id ?? "";
            string name = label.Name ?? id;
            if (string.Equals(label.Type, "user", StringComparison.OrdinalIgnoreCase))
                return !IsNoisyGmailName(name);

            return id is "INBOX" or "SENT" or "DRAFT" or "SPAM" or "TRASH" or "STARRED";
        }

        private static bool IsNoisyGmailName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;

            return name.StartsWith("CATEGORY_", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("[Imap]/", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("/", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("同步问题", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("同步問題", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNoisyImapFolder(string id, string displayName)
        {
            return string.IsNullOrWhiteSpace(id) ||
                   id.Contains("同步问题", StringComparison.OrdinalIgnoreCase) ||
                   displayName.Contains("同步问题", StringComparison.OrdinalIgnoreCase) ||
                   id.Contains("同步問題", StringComparison.OrdinalIgnoreCase) ||
                   displayName.Contains("同步問題", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeFolderKey(string id, string displayName)
        {
            var key = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
            return key.Trim().Trim('/').Trim('\\');
        }

        private static string ExtractGoogleBody(GmailMessagePart? part)
        {
            if (part == null) return "";

            if (part.Body?.Data != null &&
                string.Equals(part.MimeType, "text/plain", StringComparison.OrdinalIgnoreCase))
            {
                var text = DecodeBase64Url(part.Body.Data);
                return CleanMailBody(text);
            }

            if (part.Parts == null) return "";

            var plain = part.Parts
                .Select(ExtractGoogleBody)
                .FirstOrDefault(body => !string.IsNullOrWhiteSpace(body));

            if (!string.IsNullOrWhiteSpace(plain)) return plain;

            var html = ExtractGoogleHtmlBody(part);
            return string.IsNullOrWhiteSpace(html) ? "" : CleanMailBody(StripHtml(html));
        }

        private static string ExtractGoogleHtmlBody(GmailMessagePart? part)
        {
            if (part == null) return "";

            if (part.Body?.Data != null &&
                string.Equals(part.MimeType, "text/html", StringComparison.OrdinalIgnoreCase))
            {
                return DecodeBase64Url(part.Body.Data);
            }

            if (part.Parts == null) return "";

            return part.Parts
                .Select(ExtractGoogleHtmlBody)
                .FirstOrDefault(body => !string.IsNullOrWhiteSpace(body)) ?? "";
        }

        private static bool HasGoogleAttachments(GmailMessagePart? part)
        {
            if (part == null) return false;
            if (!string.IsNullOrWhiteSpace(part.Filename)) return true;
            return part.Parts?.Any(HasGoogleAttachments) == true;
        }

        private static string DecodeBase64Url(string value)
        {
            string normalized = value.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '=');
            return Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            value = Regex.Replace(value, @"\s+", " ").Trim();
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }

        private static string GetGoogleHeader(GmailMessage message, string name)
            => message.Payload?.Headers?
                .FirstOrDefault(header => string.Equals(header.Name, name, StringComparison.OrdinalIgnoreCase))
                ?.Value ?? "";

        private static string FormatGraphRecipients(IEnumerable<Recipient>? recipients)
            => recipients == null
                ? ""
                : string.Join(", ", recipients
                    .Select(recipient => recipient.EmailAddress)
                    .Where(address => address != null)
                    .Select(address => string.IsNullOrWhiteSpace(address!.Name)
                        ? address.Address ?? ""
                        : $"{address.Name} <{address.Address}>")
                    .Where(value => !string.IsNullOrWhiteSpace(value)));

        private static string FormatInternetAddressList(InternetAddressList? addresses)
        {
            if (addresses == null || addresses.Count == 0) return "";

            var formatted = addresses
                .Mailboxes
                .Select(mailbox => string.IsNullOrWhiteSpace(mailbox.Name)
                    ? mailbox.Address
                    : $"{mailbox.Name} <{mailbox.Address}>")
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();

            return formatted.Count > 0 ? string.Join(", ", formatted) : addresses.ToString();
        }

        private static string ExtractEmailAddress(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";

            try
            {
                return MailboxAddress.Parse(value).Address;
            }
            catch
            {
                var match = Regex.Match(value, @"[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase);
                return match.Success ? match.Value : value;
            }
        }

        private static string FormatReceivedTime(DateTimeOffset? received)
        {
            if (received == null) return "";

            var local = received.Value.ToLocalTime();
            var now = DateTimeOffset.Now;
            if (local.Date == now.Date)
                return local.ToString("t", LocalizationHelper.AppCulture);
            if (local.Date == now.Date.AddDays(-1))
                return _formatLoader.GetStringOrDefault("TextYesterday") ?? "Yesterday";
            return local.ToString("d", LocalizationHelper.AppCulture);
        }

        private static bool IsInboxName(string value)
            => string.Equals(value, "INBOX", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "Inbox", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "收件箱", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "收件匣", StringComparison.OrdinalIgnoreCase);

        private static string GetImapPassword(string accountId)
        {
            try
            {
                var vault = new PasswordVault();
                var credential = vault.Retrieve("TaskFlyout.IMAP", accountId);
                credential.RetrievePassword();
                return credential.Password;
            }
            catch
            {
                return "";
            }
        }

        private static void SaveImapPassword(string accountId, string password)
        {
            var vault = new PasswordVault();
            RemoveImapPassword(accountId);

            vault.Add(new Windows.Security.Credentials.PasswordCredential("TaskFlyout.IMAP", accountId, password));
        }

        private static void RemoveImapPassword(string accountId)
        {
            var vault = new PasswordVault();
            try
            {
                var existing = vault.Retrieve("TaskFlyout.IMAP", accountId);
                vault.Remove(existing);
            }
            catch { }
        }
    }
}
