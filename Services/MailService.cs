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
    public enum MailAccountKind
    {
        Outlook,
        Google,
        Imap
    }

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

    public class MailFolder
    {
        public string AccountId { get; set; } = "";
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public int? UnreadCount { get; set; }
        public bool IsPlaceholder { get; set; }
        public string CountText => UnreadCount.HasValue && UnreadCount.Value > 0 ? UnreadCount.Value.ToString() : "";
    }

    public class MailItem : INotifyPropertyChanged
    {
        private bool _isRead;

        public string AccountId { get; set; } = "";
        public string FolderId { get; set; } = "";
        public string Id { get; set; } = "";
        public string Subject { get; set; } = "";
        public string Sender { get; set; } = "";
        public string SenderAddress { get; set; } = "";
        public string Recipient { get; set; } = "";
        public string Preview { get; set; } = "";
        public string BodyText { get; set; } = "";
        public string HtmlBody { get; set; } = "";
        public string ReceivedTime { get; set; } = "";
        public DateTimeOffset? RawReceivedTime { get; set; }
        public bool IsRead
        {
            get => _isRead;
            set
            {
                if (_isRead == value) return;
                _isRead = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ReadMarker));
            }
        }
        public bool HasAttachments { get; set; }
        public string Importance { get; set; } = "";
        public string WebLink { get; set; } = "";
        public string ReadMarker => IsRead ? "" : "●";
        public string AttachmentMarker => HasAttachments ? "📎" : "";

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class MailPersistentCache
    {
        public Dictionary<string, List<MailFolder>> Folders { get; set; } = new();
        public Dictionary<string, List<MailItem>> Messages { get; set; } = new();
        public Dictionary<string, MailCursor> MessageCursors { get; set; } = new();
        public Dictionary<string, bool> MessageHasMore { get; set; } = new();
        public List<PendingMailMutation> PendingMutations { get; set; } = new();
        public Dictionary<string, long> LastSeenInboxTicks { get; set; } = new();
        public List<string> AccountOrder { get; set; } = new();
        public Dictionary<string, List<string>> FolderOrder { get; set; } = new();
    }

    public sealed class PendingMailMutation
    {
        public string AccountId { get; set; } = "";
        public string FolderId { get; set; } = "";
        public string MessageId { get; set; } = "";
        public MailAccountKind ProviderKind { get; set; }
        public int FailureCount { get; set; }
        public long CreatedUtcTicks { get; set; }
        public long NextAttemptUtcTicks { get; set; }
    }

    public sealed class MailCursor
    {
        public MailAccountKind ProviderKind { get; set; }
        public string Value { get; set; } = "";
        public uint? UidValidity { get; set; }
        public uint? BeforeUid { get; set; }
    }

    public sealed class MailMessageWindow
    {
        public List<MailItem> Items { get; set; } = new();
        public bool HasMore { get; set; }
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

    public sealed class MailReadSyncQueuedException : Exception
    {
        public MailReadSyncQueuedException(Exception innerException)
            : base("The read-state update was queued for a later retry.", innerException)
        {
        }
    }

    public class MailService
    {
        private readonly ResourceLoader _loader = new();
        private GraphServiceClient? _outlookClient;
        private List<MailAccount> _accounts = new();
        private bool _accountsLoaded;
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);
        private readonly Dictionary<string, CacheEntry<List<MailFolder>>> _folderCache = new();
        private readonly Dictionary<string, CacheEntry<List<MailItem>>> _messageCache = new();
        private readonly ConcurrentDictionary<string, byte> _knownUnreadIds = new(StringComparer.Ordinal);
        private DispatcherTimer? _pollTimer;
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
        private CancellationTokenSource? _persistentCacheSaveCts;
        private long _persistentCacheVersion;
        private long _lastPersistedCacheVersion;
        private readonly object _accountSaveQueueLock = new();
        private Task _accountSaveQueue = Task.CompletedTask;
        private readonly object _bodyCacheLock = new();
        private readonly Dictionary<string, WeakReference<MailItem>> _bodyCacheItems = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _bodyCacheAccessTicks = new(StringComparer.Ordinal);
        private const int MaxBodyTextChars = 80_000;
        private const int MaxHtmlBodyChars = 160_000;
        private const int MaxVolatileBodyCacheChars = 1_000_000;
        private const int TargetVolatileBodyCacheChars = 750_000;
        private const int MaxConcurrentGoogleMessageMetadataRequests = 6;
        private const int PersistentCacheSaveDebounceMs = 1500;
        public event EventHandler<NewMailNotificationEventArgs>? NewMailArrived;

        private sealed class CacheEntry<T>
        {
            public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
            public T Value { get; set; } = default!;
        }

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
                    _folderCache[accountId] = new CacheEntry<List<MailFolder>> { Value = orderedFolders };
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
            if (!MailPollingEnabled || !HasSetupCompleteAccounts())
            {
                StopMailPolling();
                return;
            }

            var interval = TimeSpan.FromMinutes(Math.Max(1, MailPollingIntervalMinutes));
            if (_pollTimer != null)
            {
                if (_pollTimer.Interval != interval)
                    _pollTimer.Interval = interval;

                if (!_pollTimer.IsEnabled)
                    _pollTimer.Start();

                QueueInitialMailCheckIfDue(interval);
                return;
            }

            _pollTimer = new DispatcherTimer
            {
                Interval = interval
            };
            _pollTimer.Tick += async (_, _) =>
            {
                try { await CheckNewMailAsync(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Mail poll tick failed: {ex.Message}"); }
            };
            _pollTimer.Start();
            QueueInitialMailCheckIfDue(interval);
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
            if (_pollTimer != null)
            {
                _pollTimer.Stop();
                _pollTimer = null;
            }
            DisconnectAllPollImapClients();
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
                return existing;

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
            await EnsureMicrosoftAgendaAccountAsync();
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
                return existing;

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
                return cachedWindow;

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

            return CommitMessagePage(cacheKey, page, append: loadMore);
        }

        public async Task SendMailAsync(MailAccount account, string to, string subject, string body, CancellationToken cancellationToken = default)
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            var operationToken = operationCts.Token;

            if (account.Kind == MailAccountKind.Outlook)
            {
                await SendOutlookMailAsync(account, to, subject, body, operationToken);
                return;
            }

            if (account.Kind == MailAccountKind.Google)
            {
                await SendGoogleMailAsync(account, to, subject, body, operationToken);
                return;
            }

            if (account.Kind == MailAccountKind.Imap)
            {
                await SendSmtpMailAsync(account, to, subject, body, operationToken);
                return;
            }

            throw new InvalidOperationException("Unsupported mail account.");
        }

        public void MarkCachedRead(MailItem item)
        {
            item.IsRead = true;
            UpdateCachedReadState(item);
        }

        public async Task MarkAsReadAsync(MailAccount account, MailItem item, bool forceRemoteSync = false, CancellationToken cancellationToken = default)
        {
            if (item.IsRead && !forceRemoteSync) return;

            for (int attempt = 0; ; attempt++)
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                try
                {
                    await MarkAsReadRemoteOnceAsync(account, item, operationCts.Token);
                    break;
                }
                catch (Exception ex) when (attempt == 0 &&
                                           !cancellationToken.IsCancellationRequested &&
                                           IsTransientReadStateFailure(ex, timeoutCts.IsCancellationRequested))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested &&
                                           IsTransientReadStateFailure(ex, timeoutCts.IsCancellationRequested))
                {
                    if (EnqueuePendingReadMutation(account, item))
                        throw new MailReadSyncQueuedException(ex);
                    throw;
                }
            }

            if (RemovePendingMutation(new PendingMailMutation
                {
                    AccountId = account.Id,
                    FolderId = item.FolderId,
                    MessageId = item.Id
                }))
                SavePersistentCache();
            item.IsRead = true;
            UpdateCachedReadState(item);
        }

        private async Task MarkAsReadRemoteOnceAsync(MailAccount account, MailItem item, CancellationToken operationToken)
        {
            if (account.Kind == MailAccountKind.Outlook)
            {
                await EnsureOutlookMailWriteAuthorizedAsync(operationToken);
                if (_outlookClient != null)
                    await _outlookClient.Me.Messages[item.Id].PatchAsync(new GraphMessage { IsRead = true }, cancellationToken: operationToken);
            }
            else if (account.Kind == MailAccountKind.Google)
            {
                var gmail = await EnsureGoogleMailModifyAuthorizedAsync(operationToken);
                await gmail.Users.Messages.Modify(new ModifyMessageRequest
                {
                    RemoveLabelIds = new List<string> { "UNREAD" }
                }, "me", item.Id).ExecuteAsync(operationToken);
            }
            else if (account.Kind == MailAccountKind.Imap)
            {
                using var client = new ImapClient();
                await ConnectImapAsync(client, account, GetImapPassword(account.Id), operationToken);
                var folder = await client.GetFolderAsync(item.FolderId, operationToken);
                await folder.OpenAsync(FolderAccess.ReadWrite, operationToken);
                if (uint.TryParse(item.Id, out var uidValue))
                    await folder.AddFlagsAsync(new UniqueId(uidValue), MessageFlags.Seen, true, operationToken);
                await client.DisconnectAsync(true, operationToken);
            }
        }

        private static bool IsTransientReadStateFailure(Exception exception, bool operationTimedOut)
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

        private bool EnqueuePendingReadMutation(MailAccount account, MailItem item)
        {
            // IMAP UIDs are unsafe to replay after reconnect unless the original UIDVALIDITY
            // is also known. The current list item does not carry it, so never persist that write.
            if (account.Kind == MailAccountKind.Imap) return false;

            EnsurePersistentCacheLoaded();
            lock (_mailCacheLock)
            {
                if (_persistentCache == null) return false;
                var pending = _persistentCache.PendingMutations.FirstOrDefault(mutation =>
                    mutation.AccountId == account.Id &&
                    mutation.FolderId == item.FolderId &&
                    mutation.MessageId == item.Id);
                if (pending == null)
                {
                    pending = new PendingMailMutation
                    {
                        AccountId = account.Id,
                        FolderId = item.FolderId,
                        MessageId = item.Id,
                        ProviderKind = account.Kind,
                        CreatedUtcTicks = DateTimeOffset.UtcNow.UtcTicks
                    };
                    _persistentCache.PendingMutations.Add(pending);
                }

                pending.FailureCount = Math.Max(1, pending.FailureCount + 1);
                pending.NextAttemptUtcTicks = (DateTimeOffset.UtcNow + MailMutationRetryPolicy.GetRetryDelay(pending.FailureCount)).UtcTicks;
                if (_persistentCache.PendingMutations.Count > 500)
                {
                    _persistentCache.PendingMutations = _persistentCache.PendingMutations
                        .OrderByDescending(mutation => mutation.CreatedUtcTicks)
                        .Take(500)
                        .ToList();
                }
            }
            SavePersistentCache();
            return true;
        }

        public async Task FetchMessageBodyAsync(MailAccount account, MailItem item, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(item.BodyText) || !string.IsNullOrWhiteSpace(item.HtmlBody))
            {
                TouchVolatileMessageBody(item);
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

            LimitMailBody(item);
            TouchVolatileMessageBody(item);
            PruneVolatileMessageBodies(GetBodyCacheKey(item));
            UpdatePersistentMessageBody(item);
        }

        public void ClearVolatileMessageBodies()
        {
            lock (_mailCacheLock)
            {
                foreach (var key in _messageCache.Keys.ToList())
                    _messageCache[key].Value = StripBodies(_messageCache[key].Value);
            }

            lock (_bodyCacheLock)
            {
                foreach (var reference in _bodyCacheItems.Values)
                {
                    if (reference.TryGetTarget(out var item))
                    {
                        item.BodyText = "";
                        item.HtmlBody = "";
                    }
                }

                _bodyCacheItems.Clear();
                _bodyCacheAccessTicks.Clear();
            }
        }

        public (int MessageCount, int CharacterCount) GetVolatileBodyCacheStats()
        {
            lock (_bodyCacheLock)
            {
                int count = 0;
                int chars = 0;
                foreach (var key in _bodyCacheItems.Keys.ToList())
                {
                    if (!_bodyCacheItems[key].TryGetTarget(out var item) || GetBodyCharCount(item) == 0)
                    {
                        _bodyCacheItems.Remove(key);
                        _bodyCacheAccessTicks.Remove(key);
                        continue;
                    }

                    count++;
                    chars += GetBodyCharCount(item);
                }

                return (count, chars);
            }
        }

        private void TouchVolatileMessageBody(MailItem item)
        {
            if (GetBodyCharCount(item) == 0) return;

            var key = GetBodyCacheKey(item);
            lock (_bodyCacheLock)
            {
                _bodyCacheItems[key] = new WeakReference<MailItem>(item);
                _bodyCacheAccessTicks[key] = DateTimeOffset.UtcNow.UtcTicks;
            }
        }

        private void PruneVolatileMessageBodies(string currentKey)
        {
            lock (_bodyCacheLock)
            {
                var live = new List<(string Key, MailItem Item, int Chars, long AccessTicks)>();
                int totalChars = 0;

                foreach (var key in _bodyCacheItems.Keys.ToList())
                {
                    if (!_bodyCacheItems[key].TryGetTarget(out var item))
                    {
                        _bodyCacheItems.Remove(key);
                        _bodyCacheAccessTicks.Remove(key);
                        continue;
                    }

                    int chars = GetBodyCharCount(item);
                    if (chars == 0)
                    {
                        _bodyCacheItems.Remove(key);
                        _bodyCacheAccessTicks.Remove(key);
                        continue;
                    }

                    totalChars += chars;
                    live.Add((key, item, chars, _bodyCacheAccessTicks.TryGetValue(key, out var ticks) ? ticks : 0));
                }

                if (totalChars <= MaxVolatileBodyCacheChars) return;

                var pruneKeys = CachePrunePolicy.SelectLeastRecentlyUsedUntilTarget(
                    live.Select(entry => new SizedCacheEntry(entry.Key, entry.Chars, entry.AccessTicks)),
                    MaxVolatileBodyCacheChars,
                    TargetVolatileBodyCacheChars,
                    currentKey).ToHashSet(StringComparer.Ordinal);

                foreach (var entry in live)
                {
                    if (!pruneKeys.Contains(entry.Key)) continue;

                    entry.Item.BodyText = "";
                    entry.Item.HtmlBody = "";
                    _bodyCacheItems.Remove(entry.Key);
                    _bodyCacheAccessTicks.Remove(entry.Key);
                    totalChars -= entry.Chars;
                    if (totalChars <= TargetVolatileBodyCacheChars) break;
                }
            }
        }

        private static string GetBodyCacheKey(MailItem item)
            => $"{item.AccountId}|{item.FolderId}|{item.Id}";

        private static int GetBodyCharCount(MailItem item)
            => (item.BodyText?.Length ?? 0) + (item.HtmlBody?.Length ?? 0);

        private async Task SendOutlookMailAsync(MailAccount account, string to, string subject, string body, CancellationToken cancellationToken)
        {
            await EnsureOutlookMailSendAuthorizedAsync(cancellationToken);
            if (_outlookClient == null) return;

            var message = new GraphMessage
            {
                Subject = subject,
                Body = new ItemBody { ContentType = BodyType.Text, Content = body },
                ToRecipients = ParseRecipients(to)
                    .Select(address => new Recipient { EmailAddress = new EmailAddress { Address = address } })
                    .ToList()
            };

            try
            {
                await _outlookClient.Me.SendMail.PostAsync(new Microsoft.Graph.Me.SendMail.SendMailPostRequestBody
                {
                    Message = message,
                    SaveToSentItems = true
                }, cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                throw new MailSendStatusUnknownException(ex);
            }
        }

        private async Task SendGoogleMailAsync(MailAccount account, string to, string subject, string body, CancellationToken cancellationToken)
        {
            var gmail = await EnsureGoogleMailSendAuthorizedAsync(cancellationToken);
            var mime = CreateMimeMessage(account, to, subject, body);
            using var stream = new MemoryStream();
            await mime.WriteToAsync(stream, cancellationToken);

            try
            {
                await gmail.Users.Messages.Send(new GmailMessage
                {
                    Raw = ToBase64Url(stream.ToArray())
                }, "me").ExecuteAsync(cancellationToken);
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                throw new MailSendStatusUnknownException(ex);
            }
        }

        private async Task SendSmtpMailAsync(MailAccount account, string to, string subject, string body, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(account.SmtpHost))
                throw new InvalidOperationException("SMTP server is required for IMAP mail sending.");

            var password = GetImapPassword(account.Id);
            var message = CreateMimeMessage(account, to, subject, body);

            using var client = new MailKit.Net.Smtp.SmtpClient { Timeout = MailNetworkTimeoutMs };
            var socketOptions = GetSmtpSocketOptions(account);
            await client.ConnectAsync(account.SmtpHost, account.SmtpPort, socketOptions, cancellationToken);
            await client.AuthenticateAsync(string.IsNullOrWhiteSpace(account.SmtpUserName) ? account.ImapUserName : account.SmtpUserName, password, cancellationToken);
            try
            {
                await client.SendAsync(message, cancellationToken);
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                throw new MailSendStatusUnknownException(ex);
            }

            try { await client.DisconnectAsync(true, cancellationToken); }
            catch { }
        }

        private async Task RetryPendingReadMutationsAsync(MailAccount account)
        {
            List<PendingMailMutation> due;
            var now = DateTimeOffset.UtcNow;
            lock (_mailCacheLock)
            {
                if (_persistentCache == null) return;
                due = _persistentCache.PendingMutations
                    .Where(mutation => mutation.AccountId == account.Id &&
                                       mutation.ProviderKind == account.Kind &&
                                       mutation.NextAttemptUtcTicks <= now.UtcTicks)
                    .OrderBy(mutation => mutation.NextAttemptUtcTicks)
                    .Take(3)
                    .Select(ClonePendingMutation)
                    .ToList();
            }

            bool changed = false;
            foreach (var mutation in due)
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                try
                {
                    await MarkAsReadRemoteOnceAsync(account, new MailItem
                    {
                        AccountId = mutation.AccountId,
                        FolderId = mutation.FolderId,
                        Id = mutation.MessageId,
                        IsRead = true
                    }, timeoutCts.Token);
                    changed |= RemovePendingMutation(mutation);
                }
                catch (Exception ex)
                {
                    if (IsTransientReadStateFailure(ex, timeoutCts.IsCancellationRequested))
                        changed |= ReschedulePendingMutation(mutation);
                    else
                        changed |= RemovePendingMutation(mutation);
                }
            }

            if (changed)
                SavePersistentCache();
        }

        private bool RemovePendingMutation(PendingMailMutation mutation)
        {
            lock (_mailCacheLock)
            {
                if (_persistentCache == null) return false;
                return _persistentCache.PendingMutations.RemoveAll(candidate => IsSameMutation(candidate, mutation)) > 0;
            }
        }

        private bool ReschedulePendingMutation(PendingMailMutation mutation)
        {
            lock (_mailCacheLock)
            {
                if (_persistentCache == null) return false;
                var current = _persistentCache.PendingMutations.FirstOrDefault(candidate => IsSameMutation(candidate, mutation));
                if (current == null) return false;

                current.FailureCount = Math.Min(current.FailureCount + 1, 30);
                current.NextAttemptUtcTicks = (DateTimeOffset.UtcNow + MailMutationRetryPolicy.GetRetryDelay(current.FailureCount)).UtcTicks;
                return true;
            }
        }

        private static bool IsSameMutation(PendingMailMutation left, PendingMailMutation right)
            => left.AccountId == right.AccountId && left.FolderId == right.FolderId && left.MessageId == right.MessageId;

        private static PendingMailMutation ClonePendingMutation(PendingMailMutation mutation)
            => new()
            {
                AccountId = mutation.AccountId,
                FolderId = mutation.FolderId,
                MessageId = mutation.MessageId,
                ProviderKind = mutation.ProviderKind,
                FailureCount = mutation.FailureCount,
                CreatedUtcTicks = mutation.CreatedUtcTicks,
                NextAttemptUtcTicks = mutation.NextAttemptUtcTicks
            };

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
                        await RetryPendingReadMutationsAsync(account);
                        var folders = await FetchFoldersAsync(account, forceRefresh: false);
                        var inbox = folders.FirstOrDefault(folder => IsInboxName(folder.Id) || IsInboxName(folder.DisplayName))
                                    ?? folders.FirstOrDefault(folder => !folder.IsPlaceholder);
                        if (inbox == null)
                        {
                            ClearAccountBackoff(account.Id);
                            continue;
                        }

                        var unreadItems = await PollFetchInboxUnreadAsync(account, inbox);
                        MergeMessagesIntoPersistentCache(account.Id, inbox.Id, unreadItems);

                        string inboxKey = $"{account.Id}|{inbox.Id}";
                        long previousSeenTicks = GetLastSeenInboxTicks(inboxKey);
                        long newestTicks = previousSeenTicks;
                        bool hasBaseline = previousSeenTicks > 0;

                        foreach (var item in unreadItems)
                        {
                            string key = GetMailNotificationKey(item);
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
        private async Task<List<MailItem>> PollFetchInboxUnreadAsync(MailAccount account, MailFolder inbox)
        {
            const int pollPageSize = 5;
            if (account.Kind != MailAccountKind.Imap)
                return (await FetchMessagesFromProviderAsync(account, inbox, unreadOnly: true, pageSize: pollPageSize)).Items;

            var client = await GetOrConnectPollImapClientAsync(account);
            List<MailItem> messages;
            try
            {
                messages = (await FetchImapMessagesWithClientAsync(client, account, inbox, unreadOnly: true, pageSize: pollPageSize)).Items;
            }
            catch
            {
                // Connection likely went bad mid-fetch — drop it so the next poll reconnects.
                DisconnectPollImapClient(account.Id);
                throw;
            }

            return CloneMailItems(messages, includeBodies: false);
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
                        "id", "subject", "from", "toRecipients", "receivedDateTime", "isRead",
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
                    items.Add(await BuildImapMailItemAsync(mailFolder, account, folder, id, summary, isRead, cancellationToken));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch
                {
                    items.Add(new MailItem
                    {
                        AccountId = account.Id,
                        FolderId = folder.Id,
                        Id = id.Id.ToString(),
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
                var item = ToImapMailItem(account.Id, folder.Id, id.Id.ToString(), message, isRead);
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

        private async Task EnsureMicrosoftAgendaAccountAsync()
        {
            if (App.Current is not App app) return;

            var accountManager = app.SyncManager.AccountManager;
            if (accountManager.IsConnected("Microsoft")) return;

            if (app.SyncManager.GetProvider("Microsoft") is not MicrosoftSyncProvider provider) return;

            var connected = new ConnectedAccountInfo { ProviderName = "Microsoft" };
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
                HasAttachments = message.HasAttachments == true,
                Importance = message.Importance?.ToString() ?? "",
                WebLink = message.WebLink ?? ""
            };
        }

        private static MimeMessage CreateMimeMessage(MailAccount account, string to, string subject, string body)
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(string.IsNullOrWhiteSpace(account.Address) ? account.ImapUserName : account.Address));
            foreach (var address in ParseRecipients(to))
                message.To.Add(MailboxAddress.Parse(address));

            message.Subject = subject;
            message.Body = new TextPart("plain") { Text = body };
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
                HasAttachments = HasGoogleAttachments(message.Payload),
                WebLink = string.IsNullOrWhiteSpace(message.Id) ? "" : $"https://mail.google.com/mail/u/0/#all/{message.Id}"
            };
        }

        private static MailItem ToImapMailItem(string accountId, string folderId, string id, MimeMessage message, bool isRead)
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
                if (_folderCache.TryGetValue(key, out var entry) && DateTimeOffset.Now - entry.CreatedAt < CacheLifetime)
                {
                    folders = ApplyFolderOrder(key, entry.Value);
                    return true;
                }

                EnsurePersistentCacheLoaded();
                if (_persistentCache?.Folders.TryGetValue(key, out var persistentFolders) == true)
                {
                    folders = ApplyFolderOrder(key, persistentFolders);
                    _folderCache[key] = new CacheEntry<List<MailFolder>> { Value = folders };
                    return true;
                }
            }

            folders = new List<MailFolder>();
            return false;
        }

        private bool TryGetCachedMessages(string key, out MailMessageWindow window)
        {
            lock (_mailCacheLock)
            {
                if (_messageCache.TryGetValue(key, out var entry) && DateTimeOffset.Now - entry.CreatedAt < CacheLifetime)
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
                if (_persistentCache?.Messages.TryGetValue(key, out var persistentMessages) == true)
                {
                    if (!_persistentCache.MessageHasMore.ContainsKey(key))
                    {
                        window = new MailMessageWindow();
                        return false;
                    }
                    var cachedMessages = StripBodies(persistentMessages);
                    _persistentCache.Messages[key] = cachedMessages;
                    _messageCache[key] = new CacheEntry<List<MailItem>> { Value = cachedMessages };
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

        private void ClearAccountCache(string accountId)
        {
            EnsurePersistentCacheLoaded();
            lock (_mailCacheLock)
            {
                _folderCache.Remove(accountId);
                foreach (var key in _messageCache.Keys.Where(key => key.StartsWith(accountId + "|", StringComparison.Ordinal)).ToList())
                    _messageCache.Remove(key);
                if (_persistentCache == null) return;
                _persistentCache.Folders.Remove(accountId);
                _persistentCache.AccountOrder.RemoveAll(id => string.Equals(id, accountId, StringComparison.Ordinal));
                _persistentCache.FolderOrder.Remove(accountId);
                foreach (var key in _persistentCache.Messages.Keys.Where(key => key.StartsWith(accountId + "|", StringComparison.Ordinal)).ToList())
                    _persistentCache.Messages.Remove(key);
                foreach (var key in _persistentCache.MessageCursors.Keys.Where(key => key.StartsWith(accountId + "|", StringComparison.Ordinal)).ToList())
                    _persistentCache.MessageCursors.Remove(key);
                foreach (var key in _persistentCache.MessageHasMore.Keys.Where(key => key.StartsWith(accountId + "|", StringComparison.Ordinal)).ToList())
                    _persistentCache.MessageHasMore.Remove(key);
                _persistentCache.PendingMutations.RemoveAll(mutation => mutation.AccountId == accountId);
                foreach (var key in _persistentCache.LastSeenInboxTicks.Keys.Where(key => key.StartsWith(accountId + "|", StringComparison.Ordinal)).ToList())
                    _persistentCache.LastSeenInboxTicks.Remove(key);
            }
            SavePersistentCache();
        }

        private void UpdateCachedReadState(MailItem item)
        {
            EnsurePersistentCacheLoaded();
            lock (_mailCacheLock)
            {
                foreach (var pair in _messageCache.ToList())
                {
                    var cached = pair.Value.Value.FirstOrDefault(message =>
                        message.AccountId == item.AccountId &&
                        message.FolderId == item.FolderId &&
                        message.Id == item.Id);

                    if (cached != null)
                        cached.IsRead = true;

                    if (string.Equals(pair.Key, GetMessageCacheKey(item.AccountId, item.FolderId, true), StringComparison.Ordinal))
                        pair.Value.Value.RemoveAll(message => message.Id == item.Id);
                }

                if (_persistentCache == null) return;

                foreach (var pair in _persistentCache.Messages.ToList())
                {
                    var cached = pair.Value.FirstOrDefault(message =>
                        message.AccountId == item.AccountId &&
                        message.FolderId == item.FolderId &&
                        message.Id == item.Id);

                    if (cached != null)
                        cached.IsRead = true;

                    if (string.Equals(pair.Key, GetMessageCacheKey(item.AccountId, item.FolderId, true), StringComparison.Ordinal))
                        pair.Value.RemoveAll(message => message.Id == item.Id);
                }

                var unreadKey = GetMessageCacheKey(item.AccountId, item.FolderId, true);
                _messageCache.Remove(unreadKey);
                _persistentCache.MessageCursors.Remove(unreadKey);
                _persistentCache.MessageHasMore.Remove(unreadKey);
            }
            SavePersistentCache();
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
                    var json = LocalSqliteStore.ReadProtectedText("mail", "cache");
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
                _persistentCache.Folders ??= new Dictionary<string, List<MailFolder>>();
                _persistentCache.Messages ??= new Dictionary<string, List<MailItem>>();
                _persistentCache.MessageCursors ??= new Dictionary<string, MailCursor>();
                _persistentCache.MessageHasMore ??= new Dictionary<string, bool>();
                _persistentCache.PendingMutations ??= new List<PendingMailMutation>();
                bool removedExpiredMutations = _persistentCache.PendingMutations.RemoveAll(
                    mutation => MailMutationRetryPolicy.IsExpired(mutation.CreatedUtcTicks, DateTimeOffset.UtcNow)) > 0;
                _persistentCache.LastSeenInboxTicks ??= new Dictionary<string, long>();
                _persistentCache.AccountOrder ??= new List<string>();
                _persistentCache.FolderOrder ??= new Dictionary<string, List<string>>();
                MigrateLegacyMessageCacheKeys();
                TrimPersistentCacheForMemory();
                if (removedExpiredMutations)
                    SavePersistentCache();
            }
        }

        private void MigrateLegacyMessageCacheKeys()
        {
            if (_persistentCache == null) return;

            bool changed = false;
            foreach (var key in _persistentCache.Messages.Keys.ToList())
            {
                if (!MailCacheKeyPolicy.TryCanonicalizeLegacy(key, out var canonicalKey)) continue;

                var combined = _persistentCache.Messages.TryGetValue(canonicalKey, out var existing)
                    ? existing.Concat(_persistentCache.Messages[key])
                    : _persistentCache.Messages[key];
                _persistentCache.Messages[canonicalKey] = combined
                    .GroupBy(item => item.Id, StringComparer.Ordinal)
                    .Select(group => group.OrderByDescending(item => item.RawReceivedTime).First())
                    .OrderByDescending(item => item.RawReceivedTime)
                    .Take(MaxPageSize)
                    .ToList();
                _persistentCache.Messages.Remove(key);
                changed = true;
            }

            if (changed)
                SavePersistentCache();
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
                await LocalSqliteStore.WriteProtectedTextAsync("mail", "cache", json);
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
            lock (_mailCacheLock)
            {
                if (_persistentCache == null) return;
                var orderedFolders = ApplyFolderOrder(key, folders);
                _persistentCache.Folders[key] = orderedFolders;
                _folderCache[key] = new CacheEntry<List<MailFolder>> { Value = orderedFolders };
            }

            SavePersistentCache();
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

        private MailMessageWindow CommitMessagePage(string key, MailProviderPage page, bool append)
        {
            EnsurePersistentCacheLoaded();
            List<MailItem> windowItems;
            bool hasMore;
            lock (_mailCacheLock)
            {
                if (_persistentCache == null) return new MailMessageWindow();
                var currentItems = append && _persistentCache.Messages.TryGetValue(key, out var current)
                    ? current
                    : Enumerable.Empty<MailItem>();
                windowItems = currentItems.Concat(page.Items)
                    .GroupBy(item => item.Id, StringComparer.Ordinal)
                    .Select(group => group.First())
                    .OrderByDescending(item => item.RawReceivedTime)
                    .Take(MaxPageSize)
                    .ToList();
                hasMore = page.HasMore && windowItems.Count < MaxPageSize;

                _persistentCache.Messages[key] = StripBodies(windowItems);
                if (!hasMore || page.NextCursor == null)
                    _persistentCache.MessageCursors.Remove(key);
                else
                    _persistentCache.MessageCursors[key] = page.NextCursor;
                _persistentCache.MessageHasMore[key] = hasMore;
                _messageCache[key] = new CacheEntry<List<MailItem>> { Value = StripBodies(windowItems) };
            }
            SavePersistentCache();
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

        private void TrimPersistentCacheForMemory()
        {
            if (_persistentCache == null) return;

            foreach (var key in _persistentCache.Messages.Keys.ToList())
            {
                var messages = StripBodies(_persistentCache.Messages[key]);
                _persistentCache.Messages[key] = messages
                    .OrderByDescending(item => item.RawReceivedTime)
                    .Take(MaxPageSize)
                    .ToList();
            }
        }

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

        private void MergeMessagesIntoPersistentCache(string accountId, string folderId, List<MailItem> newMessages)
        {
            EnsurePersistentCacheLoaded();
            if (newMessages.Count == 0) return;

            var strippedNewMessages = StripBodies(newMessages);
            bool changed = false;
            lock (_mailCacheLock)
            {
                if (_persistentCache == null) return;
                foreach (bool unreadOnly in new[] { true, false })
                {
                    var key = GetMessageCacheKey(accountId, folderId, unreadOnly);
                    List<MailItem> existing;
                    if (_persistentCache.Messages.TryGetValue(key, out var cached))
                        existing = StripBodies(cached);
                    else if (_messageCache.TryGetValue(key, out var memoryEntry))
                        existing = StripBodies(memoryEntry.Value);
                    else
                        continue;

                    int retainedCount = existing.Count;

                    foreach (var message in strippedNewMessages)
                    {
                        if (unreadOnly && message.IsRead) continue;
                        bool isNew = existing.All(item => item.Id != message.Id);
                        existing.RemoveAll(item => item.Id == message.Id);
                        existing.Add(CloneMailItem(message, includeBodies: false));
                        if (isNew)
                            retainedCount = Math.Min(retainedCount + 1, MaxPageSize);
                    }

                    _persistentCache.Messages[key] = existing
                        .OrderByDescending(item => item.RawReceivedTime)
                        .Take(retainedCount)
                        .ToList();

                    _messageCache[key] = new CacheEntry<List<MailItem>> { Value = StripBodies(_persistentCache.Messages[key]) };
                    changed = true;
                }
            }

            if (changed)
                SavePersistentCache();
        }

        public MailItem? TryGetCachedMessage(string accountId, string folderId, string messageId)
        {
            EnsurePersistentCacheLoaded();
            lock (_mailCacheLock)
            {

            foreach (var pair in _messageCache.Concat(_persistentCache?.Messages.ToDictionary(
                         entry => entry.Key,
                         entry => new CacheEntry<List<MailItem>> { Value = entry.Value }) ?? new Dictionary<string, CacheEntry<List<MailItem>>>()))
            {
                var item = pair.Value.Value.FirstOrDefault(message =>
                    message.AccountId == accountId &&
                    message.FolderId == folderId &&
                    message.Id == messageId);
                if (item != null) return CloneMailItem(item, includeBodies: false);
            }
            }

            return null;
        }

        private static List<MailItem> CloneMailItems(IEnumerable<MailItem> messages, bool includeBodies)
            => messages.Select(item => CloneMailItem(item, includeBodies)).ToList();

        private static MailItem CloneMailItem(MailItem item, bool includeBodies)
            => new()
            {
                AccountId = item.AccountId,
                FolderId = item.FolderId,
                Id = item.Id,
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
                HasAttachments = item.HasAttachments,
                Importance = item.Importance,
                WebLink = item.WebLink
            };

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

        private static string GetMailNotificationKey(MailItem item)
            => $"{item.AccountId}|{item.FolderId}|{item.Id}";

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

                AppNotificationManager.Default.Show(builder.BuildNotification());
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
                   name.Contains("同步问题", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNoisyImapFolder(string id, string displayName)
        {
            return string.IsNullOrWhiteSpace(id) ||
                   id.Contains("同步问题", StringComparison.OrdinalIgnoreCase) ||
                   displayName.Contains("同步问题", StringComparison.OrdinalIgnoreCase);
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
                return local.ToString("HH:mm");
            if (local.Date == now.Date.AddDays(-1))
                return "Yesterday";
            return local.ToString("MM/dd");
        }

        private static bool IsInboxName(string value)
            => string.Equals(value, "INBOX", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "Inbox", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "收件箱", StringComparison.OrdinalIgnoreCase);

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
