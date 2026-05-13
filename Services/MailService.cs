using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using MimeKit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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
        public string SetupText => IsSetupComplete ? "" : "待配置";
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

    public class MailItem
    {
        public string AccountId { get; set; } = "";
        public string FolderId { get; set; } = "";
        public string Id { get; set; } = "";
        public string Subject { get; set; } = "";
        public string Sender { get; set; } = "";
        public string SenderAddress { get; set; } = "";
        public string Preview { get; set; } = "";
        public string BodyText { get; set; } = "";
        public string HtmlBody { get; set; } = "";
        public string ReceivedTime { get; set; } = "";
        public DateTimeOffset? RawReceivedTime { get; set; }
        public bool IsRead { get; set; }
        public bool HasAttachments { get; set; }
        public string Importance { get; set; } = "";
        public string WebLink { get; set; } = "";
        public string ReadMarker => IsRead ? "" : "●";
        public string AttachmentMarker => HasAttachments ? "📎" : "";
    }

    public class MailPersistentCache
    {
        public Dictionary<string, List<MailFolder>> Folders { get; set; } = new();
        public Dictionary<string, List<MailItem>> Messages { get; set; } = new();
        public Dictionary<string, long> LastSeenInboxTicks { get; set; } = new();
    }

    public class MailService
    {
        private GraphServiceClient? _outlookClient;
        private List<MailAccount> _accounts = new();
        private bool _accountsLoaded;
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);
        private readonly Dictionary<string, CacheEntry<List<MailFolder>>> _folderCache = new();
        private readonly Dictionary<string, CacheEntry<List<MailItem>>> _messageCache = new();
        private readonly HashSet<string> _knownUnreadIds = new(StringComparer.Ordinal);
        private DispatcherTimer? _pollTimer;
        private bool _isPollingMail;
        private bool _knownUnreadLoaded;
        private MailPersistentCache? _persistentCache;
        private bool _persistentCacheLoaded;

        private sealed class CacheEntry<T>
        {
            public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
            public T Value { get; set; } = default!;
        }

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

        public IReadOnlyList<MailAccount> GetAccounts()
        {
            EnsureAccountsLoaded();
            return _accounts;
        }

        public bool RemoveAccount(string accountId)
        {
            EnsureAccountsLoaded();

            var account = _accounts.FirstOrDefault(a => a.Id == accountId);
            if (account == null) return false;

            _accounts.Remove(account);
            if (account.Kind == MailAccountKind.Imap)
                RemoveImapPassword(account.Id);

            ClearAccountCache(account.Id);
            SaveAccounts();
            return true;
        }

        public void StartMailPolling()
        {
            StopMailPolling();
            if (!MailPollingEnabled) return;

            _pollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(Math.Max(1, MailPollingIntervalMinutes))
            };
            _pollTimer.Tick += async (_, _) => await CheckNewMailAsync();
            _pollTimer.Start();
            _ = CheckNewMailAsync();
        }

        public void StopMailPolling()
        {
            if (_pollTimer != null)
            {
                _pollTimer.Stop();
                _pollTimer = null;
            }
        }

        public void UpdateMailPollingSettings()
        {
            if (MailPollingEnabled)
                StartMailPolling();
            else
                StopMailPolling();
        }

        public async Task<MailAccount> AddOutlookAccountAsync()
        {
            await EnsureOutlookAuthorizedAsync();
            if (_outlookClient == null)
                throw new InvalidOperationException("Outlook authorization failed.");

            var me = await _outlookClient.Me.GetAsync(request =>
            {
                request.QueryParameters.Select = new[] { "displayName", "mail", "userPrincipalName" };
            });

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
            string smtpUserName)
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

            await TestImapConnectionAsync(account, password);

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
                return existing;
            }

            _accounts.Add(account);
            SaveImapPassword(account.Id, password);
            SaveAccounts();
            return account;
        }

        public async Task<MailAccount> AddGoogleAccountAsync()
        {
            var gmail = await EnsureGoogleMailAuthorizedAsync();
            var profile = await gmail.Users.GetProfile("me").ExecuteAsync();
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
            return account;
        }

        public async Task<List<MailFolder>> FetchFoldersAsync(MailAccount account, bool forceRefresh = false)
        {
            string cacheKey = account.Id;
            if (!forceRefresh && TryGetCachedFolders(cacheKey, out var cachedFolders))
                return cachedFolders;

            List<MailFolder> folders;
            if (account.Kind == MailAccountKind.Google && account.IsSetupComplete)
            {
                folders = await FetchGoogleFoldersAsync(account);
            }
            else if (account.Kind == MailAccountKind.Imap && account.IsSetupComplete)
            {
                folders = await FetchImapFoldersAsync(account);
            }
            else if (account.Kind != MailAccountKind.Outlook || !account.IsSetupComplete)
            {
                folders = new List<MailFolder>
                {
                    new MailFolder
                    {
                        AccountId = account.Id,
                        Id = "setup",
                        DisplayName = "完成配置后显示文件夹",
                        IsPlaceholder = true
                    }
                };
            }
            else
            {
                await EnsureOutlookAuthorizedAsync();
                if (_outlookClient == null) return new List<MailFolder>();

                var response = await _outlookClient.Me.MailFolders.GetAsync(request =>
                {
                    request.QueryParameters.Top = 50;
                    request.QueryParameters.Select = new[] { "id", "displayName", "unreadItemCount" };
                });

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

            _folderCache[cacheKey] = new CacheEntry<List<MailFolder>> { Value = folders };
            UpdatePersistentFolders(cacheKey, folders);
            return folders;
        }

        public async Task<List<MailItem>> FetchMessagesAsync(MailAccount account, MailFolder folder, bool unreadOnly, int? pageSize = null, bool forceRefresh = false)
        {
            string cacheKey = GetMessageCacheKey(account.Id, folder.Id, unreadOnly, pageSize ?? PageSize);
            if (!forceRefresh && TryGetCachedMessages(cacheKey, out var cachedMessages))
                return cachedMessages;

            List<MailItem> messages;
            if (account.Kind == MailAccountKind.Google)
            {
                messages = await FetchGoogleMessagesAsync(account, folder, unreadOnly, pageSize);
            }
            else if (account.Kind == MailAccountKind.Imap)
            {
                messages = await FetchImapMessagesAsync(account, folder, unreadOnly, pageSize);
            }
            else if (account.Kind != MailAccountKind.Outlook || !account.IsSetupComplete || folder.IsPlaceholder)
            {
                messages = new List<MailItem>();
            }
            else
            {
                await EnsureOutlookAuthorizedAsync();
                if (_outlookClient == null) return new List<MailItem>();

                int top = Math.Clamp(pageSize ?? PageSize, 10, 50);
                var response = await _outlookClient.Me.MailFolders[folder.Id].Messages.GetAsync(request =>
                {
                    request.QueryParameters.Top = top;
                    request.QueryParameters.Select = new[]
                    {
                        "id", "subject", "from", "receivedDateTime", "isRead",
                        "bodyPreview", "body", "webLink", "hasAttachments", "importance"
                    };
                    request.QueryParameters.Orderby = new[] { "receivedDateTime desc" };
                    if (unreadOnly)
                        request.QueryParameters.Filter = "isRead eq false";
                });

                messages = response?.Value?
                    .Where(message => message != null)
                    .Select(message => ToOutlookMailItem(account.Id, folder.Id, message))
                    .ToList() ?? new List<MailItem>();
            }

            _messageCache[cacheKey] = new CacheEntry<List<MailItem>> { Value = messages };
            UpdatePersistentMessages(cacheKey, messages);
            return messages;
        }

        public async Task SendMailAsync(MailAccount account, string to, string subject, string body)
        {
            if (account.Kind == MailAccountKind.Outlook)
            {
                await SendOutlookMailAsync(account, to, subject, body);
                return;
            }

            if (account.Kind == MailAccountKind.Google)
            {
                await SendGoogleMailAsync(account, to, subject, body);
                return;
            }

            if (account.Kind == MailAccountKind.Imap)
            {
                await SendSmtpMailAsync(account, to, subject, body);
                return;
            }

            throw new InvalidOperationException("Unsupported mail account.");
        }

        public async Task MarkAsReadAsync(MailAccount account, MailItem item)
        {
            if (item.IsRead) return;

            if (account.Kind == MailAccountKind.Outlook)
            {
                await EnsureOutlookAuthorizedAsync();
                if (_outlookClient != null)
                    await _outlookClient.Me.Messages[item.Id].PatchAsync(new GraphMessage { IsRead = true });
            }
            else if (account.Kind == MailAccountKind.Google)
            {
                var gmail = await EnsureGoogleMailAuthorizedAsync();
                await gmail.Users.Messages.Modify(new ModifyMessageRequest
                {
                    RemoveLabelIds = new List<string> { "UNREAD" }
                }, "me", item.Id).ExecuteAsync();
            }
            else if (account.Kind == MailAccountKind.Imap)
            {
                using var client = new ImapClient();
                await ConnectImapAsync(client, account, GetImapPassword(account.Id));
                var folder = await client.GetFolderAsync(item.FolderId);
                await folder.OpenAsync(FolderAccess.ReadWrite);
                if (uint.TryParse(item.Id, out var uidValue))
                    await folder.AddFlagsAsync(new UniqueId(uidValue), MessageFlags.Seen, true);
                await client.DisconnectAsync(true);
            }

            item.IsRead = true;
            UpdateCachedReadState(item);
        }

        private async Task SendOutlookMailAsync(MailAccount account, string to, string subject, string body)
        {
            await EnsureOutlookAuthorizedAsync();
            if (_outlookClient == null) return;

            var message = new GraphMessage
            {
                Subject = subject,
                Body = new ItemBody { ContentType = BodyType.Text, Content = body },
                ToRecipients = ParseRecipients(to)
                    .Select(address => new Recipient { EmailAddress = new EmailAddress { Address = address } })
                    .ToList()
            };

            await _outlookClient.Me.SendMail.PostAsync(new Microsoft.Graph.Me.SendMail.SendMailPostRequestBody
            {
                Message = message,
                SaveToSentItems = true
            });
        }

        private async Task SendGoogleMailAsync(MailAccount account, string to, string subject, string body)
        {
            var gmail = await EnsureGoogleMailAuthorizedAsync();
            var mime = CreateMimeMessage(account, to, subject, body);
            using var stream = new MemoryStream();
            await mime.WriteToAsync(stream);

            await gmail.Users.Messages.Send(new GmailMessage
            {
                Raw = ToBase64Url(stream.ToArray())
            }, "me").ExecuteAsync();
        }

        private async Task SendSmtpMailAsync(MailAccount account, string to, string subject, string body)
        {
            if (string.IsNullOrWhiteSpace(account.SmtpHost))
                throw new InvalidOperationException("SMTP server is required for IMAP mail sending.");

            var password = GetImapPassword(account.Id);
            var message = CreateMimeMessage(account, to, subject, body);

            using var client = new MailKit.Net.Smtp.SmtpClient();
            var socketOptions = GetSmtpSocketOptions(account);
            await client.ConnectAsync(account.SmtpHost, account.SmtpPort, socketOptions);
            await client.AuthenticateAsync(string.IsNullOrWhiteSpace(account.SmtpUserName) ? account.ImapUserName : account.SmtpUserName, password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }

        private async Task CheckNewMailAsync()
        {
            if (_isPollingMail || !MailPollingEnabled) return;

            _isPollingMail = true;
            try
            {
                EnsureAccountsLoaded();
                LoadKnownUnreadIds();
                EnsurePersistentCacheLoaded();

                var currentUnreadIds = new HashSet<string>(StringComparer.Ordinal);
                var newItems = new List<(MailAccount Account, MailItem Item)>();

                foreach (var account in _accounts.Where(account => account.IsSetupComplete))
                {
                    try
                    {
                        var folders = await FetchFoldersAsync(account, forceRefresh: false);
                        var inbox = folders.FirstOrDefault(folder => IsInboxName(folder.Id) || IsInboxName(folder.DisplayName))
                                    ?? folders.FirstOrDefault(folder => !folder.IsPlaceholder);
                        if (inbox == null) continue;

                        var unreadItems = await FetchMessagesAsync(account, inbox, unreadOnly: true, pageSize: 5, forceRefresh: true);
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
                                !_knownUnreadIds.Contains(key))
                            {
                                newItems.Add((account, item));
                            }
                        }

                        if (newestTicks > previousSeenTicks)
                            SetLastSeenInboxTicks(inboxKey, newestTicks);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Mail polling failed for {account.DisplayTitle}: {ex.Message}");
                    }
                }

                foreach (var pair in newItems.Take(5))
                    SendNewMailNotification(pair.Account, pair.Item);

                _knownUnreadIds.Clear();
                foreach (var id in currentUnreadIds.Take(500))
                    _knownUnreadIds.Add(id);
                SaveKnownUnreadIds();
                SavePersistentCache();
            }
            finally
            {
                _isPollingMail = false;
            }
        }

        private async Task<GmailService> EnsureGoogleMailAuthorizedAsync()
        {
            EnsureAccountsLoaded();

            if (App.Current is App app &&
                app.SyncManager.GetProvider("Google") is GoogleSyncProvider googleProvider)
            {
                await googleProvider.EnsureAuthorizedAsync();
                if (googleProvider.GmailSvc != null)
                    return googleProvider.GmailSvc;
            }

            throw new InvalidOperationException("Google provider is not available.");
        }

        private async Task TestImapConnectionAsync(MailAccount account, string password)
        {
            using var client = new ImapClient();
            await ConnectImapAsync(client, account, password);
            await client.DisconnectAsync(true);
        }

        private async Task<List<MailFolder>> FetchImapFoldersAsync(MailAccount account)
        {
            using var client = new ImapClient();
            await ConnectImapAsync(client, account, GetImapPassword(account.Id));

            var result = new List<MailFolder>();
            var folders = await client.GetFoldersAsync(client.PersonalNamespaces.FirstOrDefault() ?? client.PersonalNamespaces[0]);
            foreach (var folder in folders.Where(folder => (folder.Attributes & FolderAttributes.NonExistent) == 0))
            {
                int? unreadCount = null;
                try
                {
                    await folder.StatusAsync(StatusItems.Unread);
                    unreadCount = folder.Unread;
                }
                catch { }

                result.Add(new MailFolder
                {
                    AccountId = account.Id,
                    Id = folder.FullName,
                    DisplayName = string.IsNullOrWhiteSpace(folder.Name) ? folder.FullName : folder.Name,
                    UnreadCount = unreadCount
                });
            }

            await client.DisconnectAsync(true);

            return result
                .Where(folder => !IsNoisyImapFolder(folder.Id, folder.DisplayName))
                .GroupBy(folder => NormalizeFolderKey(folder.Id, folder.DisplayName), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(folder => IsInboxName(folder.Id) || IsInboxName(folder.DisplayName))
                .ThenBy(folder => folder.DisplayName)
                .ToList();
        }

        private async Task<List<MailItem>> FetchImapMessagesAsync(MailAccount account, MailFolder folder, bool unreadOnly, int? pageSize)
        {
            if (!account.IsSetupComplete || folder.IsPlaceholder)
                return new List<MailItem>();

            using var client = new ImapClient();
            await ConnectImapAsync(client, account, GetImapPassword(account.Id));

            var mailFolder = await client.GetFolderAsync(folder.Id);
            await mailFolder.OpenAsync(FolderAccess.ReadOnly);

            var query = unreadOnly ? MailKit.Search.SearchQuery.NotSeen : MailKit.Search.SearchQuery.All;
            var ids = await mailFolder.SearchAsync(query);
            int top = Math.Clamp(pageSize ?? PageSize, 10, 50);
            var selectedIds = ids.Reverse().Take(top).ToList();
            var flagsById = new Dictionary<uint, MessageFlags?>();
            try
            {
                var summaries = await mailFolder.FetchAsync(selectedIds, MessageSummaryItems.Flags);
                flagsById = summaries
                    .Where(summary => summary.UniqueId.IsValid)
                    .ToDictionary(summary => summary.UniqueId.Id, summary => summary.Flags);
            }
            catch
            {
                // Some IMAP servers return malformed FETCH responses for flags.
                // Keep loading messages and fall back to per-message flag fetches.
            }

            var items = new List<MailItem>();
            foreach (var id in selectedIds)
            {
                bool isRead = unreadOnly ? false : await GetImapReadStateAsync(mailFolder, id, flagsById);
                try
                {
                    var message = await mailFolder.GetMessageAsync(id);
                    items.Add(ToImapMailItem(account.Id, folder.Id, id.Id.ToString(), message, isRead));
                }
                catch (Exception ex)
                {
                    items.Add(new MailItem
                    {
                        AccountId = account.Id,
                        FolderId = folder.Id,
                        Id = id.Id.ToString(),
                        Subject = "无法读取此邮件",
                        Sender = account.DisplayTitle,
                        Preview = "IMAP 服务器返回了无效的 FETCH 响应。",
                        BodyText = "无法加载此邮件的正文内容。",
                        IsRead = isRead
                    });
                }
            }

            await client.DisconnectAsync(true);
            return items.OrderByDescending(item => item.RawReceivedTime).ToList();
        }

        private static async Task ConnectImapAsync(ImapClient client, MailAccount account, string password)
        {
            if (string.IsNullOrWhiteSpace(account.ImapHost))
                throw new InvalidOperationException("IMAP server is required.");
            if (string.IsNullOrWhiteSpace(account.ImapUserName))
                throw new InvalidOperationException("IMAP user name is required.");
            if (string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("IMAP password is required.");

            var socketOptions = account.ImapUseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
            await client.ConnectAsync(account.ImapHost, account.ImapPort, socketOptions);
            await client.AuthenticateAsync(account.ImapUserName, password);
        }

        private static async Task<bool> GetImapReadStateAsync(IMailFolder folder, UniqueId id, Dictionary<uint, MessageFlags?> flagsById)
        {
            if (flagsById.TryGetValue(id.Id, out var cachedFlags))
                return cachedFlags?.HasFlag(MessageFlags.Seen) == true;

            try
            {
                var summaries = await folder.FetchAsync(new[] { id }, MessageSummaryItems.Flags);
                var flags = summaries.FirstOrDefault()?.Flags;
                return flags?.HasFlag(MessageFlags.Seen) == true;
            }
            catch
            {
                // If the server refuses flag fetches, prefer not to show a false unread dot.
                return true;
            }
        }

        private async Task<List<MailFolder>> FetchGoogleFoldersAsync(MailAccount account)
        {
            var gmail = await EnsureGoogleMailAuthorizedAsync();
            var labels = await gmail.Users.Labels.List("me").ExecuteAsync();

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

        private async Task<List<MailItem>> FetchGoogleMessagesAsync(MailAccount account, MailFolder folder, bool unreadOnly, int? pageSize)
        {
            if (!account.IsSetupComplete || folder.IsPlaceholder)
                return new List<MailItem>();

            var gmail = await EnsureGoogleMailAuthorizedAsync();
            int top = Math.Clamp(pageSize ?? PageSize, 10, 50);

            var listRequest = gmail.Users.Messages.List("me");
            listRequest.LabelIds = folder.Id;
            listRequest.MaxResults = top;
            if (unreadOnly)
                listRequest.Q = "is:unread";

            var list = await listRequest.ExecuteAsync();
            if (list?.Messages == null || list.Messages.Count == 0)
                return new List<MailItem>();

            var tasks = list.Messages
                .Where(message => !string.IsNullOrWhiteSpace(message.Id))
                .Select(async message =>
                {
                    var get = gmail.Users.Messages.Get("me", message.Id);
                    get.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
                    get.MetadataHeaders = new[] { "From", "Subject", "Date" };
                    return ToGoogleMailItem(account.Id, folder.Id, await get.ExecuteAsync());
                });

            var messages = await Task.WhenAll(tasks);
            return messages
                .OrderByDescending(message => message.RawReceivedTime)
                .ToList();
        }

        private async Task EnsureOutlookAuthorizedAsync()
        {
            if (_outlookClient != null) return;
            if (App.Current is App app &&
                app.SyncManager.GetProvider("Microsoft") is MicrosoftSyncProvider microsoftProvider)
            {
                await microsoftProvider.EnsureAuthorizedAsync();
                _outlookClient = microsoftProvider.GraphClient;
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

            string path = GetAccountsPath();
            if (!File.Exists(path)) return;

            try
            {
                string json = ProtectedLocalStore.ReadText(path);
                if (!string.IsNullOrWhiteSpace(json))
                    _accounts = JsonSerializer.Deserialize(json, AppJsonContext.Default.ListMailAccount) ?? new List<MailAccount>();
            }
            catch
            {
                _accounts = new List<MailAccount>();
            }
        }

        private void SaveAccounts()
        {
            Directory.CreateDirectory(GetAppDataPath());
            string json = JsonSerializer.Serialize(_accounts, AppJsonContext.Default.ListMailAccount);
            ProtectedLocalStore.WriteText(GetAccountsPath(), json);
        }

        private static string GetAppDataPath()
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskFlyout");

        private static string GetAccountsPath()
            => Path.Combine(GetAppDataPath(), "mail_accounts.json");

        private static string GetMailCachePath()
            => Path.Combine(GetAppDataPath(), "mail_cache.json");

        private static MailItem ToOutlookMailItem(string accountId, string folderId, GraphMessage message)
        {
            var received = message.ReceivedDateTime;
            var content = message.Body?.Content ?? "";
            var isHtml = message.Body?.ContentType == BodyType.Html || HasHtmlContentTags(content);
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
                Preview = message.BodyPreview ?? "",
                BodyText = CleanMailBody(isHtml ? StripHtml(content) : content),
                HtmlBody = isHtml ? content : "",
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

            return account.SmtpUseSsl
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.StartTlsWhenAvailable;
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
            string body = ExtractGoogleBody(message.Payload);
            string htmlBody = ExtractGoogleHtmlBody(message.Payload);
            if (string.IsNullOrWhiteSpace(htmlBody) && HasHtmlContentTags(body))
            {
                htmlBody = body;
                body = CleanMailBody(StripHtml(htmlBody));
            }
            string preview = string.IsNullOrWhiteSpace(body) ? message.Snippet ?? "" : Truncate(body, 240);
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
                Preview = preview,
                BodyText = body,
                HtmlBody = htmlBody,
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
                Sender = message.From?.ToString() ?? "",
                SenderAddress = message.From?.Mailboxes?.FirstOrDefault()?.Address ?? "",
                Preview = preview.Trim(),
                BodyText = body,
                HtmlBody = htmlBody,
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
            if (_folderCache.TryGetValue(key, out var entry) && DateTimeOffset.Now - entry.CreatedAt < CacheLifetime)
            {
                folders = entry.Value;
                return true;
            }

            EnsurePersistentCacheLoaded();
            if (_persistentCache?.Folders.TryGetValue(key, out var persistentFolders) == true)
            {
                folders = persistentFolders;
                _folderCache[key] = new CacheEntry<List<MailFolder>> { Value = folders };
                return true;
            }

            folders = new List<MailFolder>();
            return false;
        }

        private bool TryGetCachedMessages(string key, out List<MailItem> messages)
        {
            if (_messageCache.TryGetValue(key, out var entry) && DateTimeOffset.Now - entry.CreatedAt < CacheLifetime)
            {
                messages = entry.Value;
                return true;
            }

            EnsurePersistentCacheLoaded();
            if (_persistentCache?.Messages.TryGetValue(key, out var persistentMessages) == true)
            {
                messages = persistentMessages;
                _messageCache[key] = new CacheEntry<List<MailItem>> { Value = messages };
                return true;
            }

            messages = new List<MailItem>();
            return false;
        }

        private void ClearAccountCache(string accountId)
        {
            _folderCache.Remove(accountId);

            foreach (var key in _messageCache.Keys.Where(key => key.StartsWith(accountId + "|", StringComparison.Ordinal)).ToList())
                _messageCache.Remove(key);

            EnsurePersistentCacheLoaded();
            if (_persistentCache != null)
            {
                _persistentCache.Folders.Remove(accountId);
                foreach (var key in _persistentCache.Messages.Keys.Where(key => key.StartsWith(accountId + "|", StringComparison.Ordinal)).ToList())
                    _persistentCache.Messages.Remove(key);
                SavePersistentCache();
            }
        }

        private void UpdateCachedReadState(MailItem item)
        {
            foreach (var pair in _messageCache.ToList())
            {
                var cached = pair.Value.Value.FirstOrDefault(message =>
                    message.AccountId == item.AccountId &&
                    message.FolderId == item.FolderId &&
                    message.Id == item.Id);

                if (cached != null)
                    cached.IsRead = true;

                if (pair.Key.StartsWith($"{item.AccountId}|{item.FolderId}|True|", StringComparison.Ordinal))
                    pair.Value.Value.RemoveAll(message => message.Id == item.Id);
            }

            EnsurePersistentCacheLoaded();
            if (_persistentCache == null) return;

            foreach (var pair in _persistentCache.Messages.ToList())
            {
                var cached = pair.Value.FirstOrDefault(message =>
                    message.AccountId == item.AccountId &&
                    message.FolderId == item.FolderId &&
                    message.Id == item.Id);

                if (cached != null)
                    cached.IsRead = true;

                if (pair.Key.StartsWith($"{item.AccountId}|{item.FolderId}|True|", StringComparison.Ordinal))
                    pair.Value.RemoveAll(message => message.Id == item.Id);
            }
            SavePersistentCache();
        }

        private static string GetMessageCacheKey(string accountId, string folderId, bool unreadOnly, int pageSize)
            => $"{accountId}|{folderId}|{unreadOnly}|{pageSize}";

        private void EnsurePersistentCacheLoaded()
        {
            if (_persistentCacheLoaded) return;
            _persistentCacheLoaded = true;

            try
            {
                var path = GetMailCachePath();
                if (File.Exists(path))
                {
                    var json = ProtectedLocalStore.ReadText(path);
                    if (!string.IsNullOrWhiteSpace(json))
                        _persistentCache = JsonSerializer.Deserialize(json, AppJsonContext.Default.MailPersistentCache);
                }
            }
            catch { }

            _persistentCache ??= new MailPersistentCache();
        }

        private void SavePersistentCache()
        {
            try
            {
                EnsurePersistentCacheLoaded();
                Directory.CreateDirectory(GetAppDataPath());
                var json = JsonSerializer.Serialize(_persistentCache, AppJsonContext.Default.MailPersistentCache);
                ProtectedLocalStore.WriteText(GetMailCachePath(), json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Save mail cache failed: {ex.Message}");
            }
        }

        private void UpdatePersistentFolders(string key, List<MailFolder> folders)
        {
            EnsurePersistentCacheLoaded();
            if (_persistentCache == null) return;

            _persistentCache.Folders[key] = folders;
            SavePersistentCache();
        }

        private void UpdatePersistentMessages(string key, List<MailItem> messages)
        {
            EnsurePersistentCacheLoaded();
            if (_persistentCache == null) return;

            _persistentCache.Messages[key] = messages;
            SavePersistentCache();
        }

        private void MergeMessagesIntoPersistentCache(string accountId, string folderId, List<MailItem> newMessages)
        {
            EnsurePersistentCacheLoaded();
            if (_persistentCache == null || newMessages.Count == 0) return;

            int pageSize = PageSize;
            foreach (bool unreadOnly in new[] { true, false })
            {
                var key = GetMessageCacheKey(accountId, folderId, unreadOnly, pageSize);
                var existing = _persistentCache.Messages.TryGetValue(key, out var cached)
                    ? cached
                    : new List<MailItem>();

                foreach (var message in newMessages)
                {
                    if (unreadOnly && message.IsRead) continue;
                    existing.RemoveAll(item => item.Id == message.Id);
                    existing.Add(message);
                }

                _persistentCache.Messages[key] = existing
                    .OrderByDescending(item => item.RawReceivedTime)
                    .Take(Math.Clamp(pageSize, 10, 50))
                    .ToList();

                _messageCache[key] = new CacheEntry<List<MailItem>> { Value = _persistentCache.Messages[key] };
            }

            SavePersistentCache();
        }

        public MailItem? TryGetCachedMessage(string accountId, string folderId, string messageId)
        {
            EnsurePersistentCacheLoaded();

            foreach (var pair in _messageCache.Concat(_persistentCache?.Messages.ToDictionary(
                         entry => entry.Key,
                         entry => new CacheEntry<List<MailItem>> { Value = entry.Value }) ?? new Dictionary<string, CacheEntry<List<MailItem>>>()))
            {
                var item = pair.Value.Value.FirstOrDefault(message =>
                    message.AccountId == accountId &&
                    message.FolderId == folderId &&
                    message.Id == messageId);
                if (item != null) return item;
            }

            return null;
        }

        private void LoadKnownUnreadIds()
        {
            if (_knownUnreadLoaded) return;
            _knownUnreadLoaded = true;

            var raw = ApplicationData.Current.LocalSettings.Values["MailKnownUnreadIds"] as string ?? "";
            foreach (var id in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                _knownUnreadIds.Add(id);
        }

        private void SaveKnownUnreadIds()
        {
            ApplicationData.Current.LocalSettings.Values["MailKnownUnreadIds"] = string.Join('\n', _knownUnreadIds);
        }

        private long GetLastSeenInboxTicks(string inboxKey)
        {
            EnsurePersistentCacheLoaded();
            return _persistentCache?.LastSeenInboxTicks.TryGetValue(inboxKey, out var ticks) == true ? ticks : 0;
        }

        private void SetLastSeenInboxTicks(string inboxKey, long ticks)
        {
            EnsurePersistentCacheLoaded();
            if (_persistentCache == null) return;
            _persistentCache.LastSeenInboxTicks[inboxKey] = ticks;
        }

        private static long GetMailReceivedTicks(MailItem item)
            => item.RawReceivedTime?.UtcTicks ?? 0;

        private static string GetMailNotificationKey(MailItem item)
            => $"{item.AccountId}|{item.FolderId}|{item.Id}";

        private static void SendNewMailNotification(MailAccount account, MailItem item)
        {
            try
            {
                var sender = string.IsNullOrWhiteSpace(item.Sender) ? account.DisplayTitle : item.Sender;
                var subject = string.IsNullOrWhiteSpace(item.Subject) ? "(No subject)" : item.Subject;

                var notification = new AppNotificationBuilder()
                    .AddText($"新邮件 · {account.DisplayTitle}")
                    .AddText(subject)
                    .AddText(sender)
                    .AddArgument("action", "openMail")
                    .AddArgument("accountId", item.AccountId)
                    .AddArgument("folderId", item.FolderId)
                    .AddArgument("messageId", item.Id)
                    .BuildNotification();

                AppNotificationManager.Default.Show(notification);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"New mail notification failed: {ex.Message}");
            }
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
                return "昨天";
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
