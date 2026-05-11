using Azure.Core;
using Azure.Identity;
using Google.Apis.Gmail.v1;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;
using GmailMessage = Google.Apis.Gmail.v1.Data.Message;
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
        public string Preview { get; set; } = "";
        public string ReceivedTime { get; set; } = "";
        public DateTimeOffset? RawReceivedTime { get; set; }
        public bool IsRead { get; set; }
        public bool HasAttachments { get; set; }
        public string Importance { get; set; } = "";
        public string WebLink { get; set; } = "";
        public string ReadMarker => IsRead ? "" : "●";
        public string AttachmentMarker => HasAttachments ? "📎" : "";
    }

    public class MailService
    {
        private GraphServiceClient? _outlookClient;
        private List<MailAccount> _accounts = new();
        private bool _accountsLoaded;
        private readonly string[] _outlookScopes = new[] { "Mail.Read", "User.Read" };

        public int PageSize
        {
            get => ApplicationData.Current.LocalSettings.Values["MailPageSize"] as int? ?? 25;
            set => ApplicationData.Current.LocalSettings.Values["MailPageSize"] = value;
        }

        public IReadOnlyList<MailAccount> GetAccounts()
        {
            EnsureAccountsLoaded();
            return _accounts;
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

        public async Task<List<MailFolder>> FetchFoldersAsync(MailAccount account)
        {
            if (account.Kind == MailAccountKind.Google && account.IsSetupComplete)
                return await FetchGoogleFoldersAsync(account);

            if (account.Kind != MailAccountKind.Outlook || !account.IsSetupComplete)
            {
                return new List<MailFolder>
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

            await EnsureOutlookAuthorizedAsync();
            if (_outlookClient == null) return new List<MailFolder>();

            var response = await _outlookClient.Me.MailFolders.GetAsync(request =>
            {
                request.QueryParameters.Top = 50;
                request.QueryParameters.Select = new[] { "id", "displayName", "unreadItemCount" };
            });

            var folders = response?.Value?
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

            return folders;
        }

        public async Task<List<MailItem>> FetchMessagesAsync(MailAccount account, MailFolder folder, bool unreadOnly, int? pageSize = null)
        {
            if (account.Kind == MailAccountKind.Google)
                return await FetchGoogleMessagesAsync(account, folder, unreadOnly, pageSize);

            if (account.Kind != MailAccountKind.Outlook || !account.IsSetupComplete || folder.IsPlaceholder)
                return new List<MailItem>();

            await EnsureOutlookAuthorizedAsync();
            if (_outlookClient == null) return new List<MailItem>();

            int top = Math.Clamp(pageSize ?? PageSize, 10, 50);
            var response = await _outlookClient.Me.MailFolders[folder.Id].Messages.GetAsync(request =>
            {
                request.QueryParameters.Top = top;
                request.QueryParameters.Select = new[]
                {
                    "id", "subject", "from", "receivedDateTime", "isRead",
                    "bodyPreview", "webLink", "hasAttachments", "importance"
                };
                request.QueryParameters.Orderby = new[] { "receivedDateTime desc" };
                if (unreadOnly)
                    request.QueryParameters.Filter = "isRead eq false";
            });

            return response?.Value?
                .Where(message => message != null)
                .Select(message => ToOutlookMailItem(account.Id, folder.Id, message))
                .ToList() ?? new List<MailItem>();
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

        private async Task<List<MailFolder>> FetchGoogleFoldersAsync(MailAccount account)
        {
            var gmail = await EnsureGoogleMailAuthorizedAsync();
            var labels = await gmail.Users.Labels.List("me").ExecuteAsync();

            return labels?.Labels?
                .Where(label => label != null && !string.IsNullOrWhiteSpace(label.Id))
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
                    get.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
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

            string appDataPath = GetAppDataPath();
            Directory.CreateDirectory(appDataPath);
            string authRecordPath = Path.Combine(appDataPath, "ms_mail_auth_record.bin");

            AuthenticationRecord? authRecord = null;
            try
            {
                if (File.Exists(authRecordPath))
                {
                    using var stream = File.OpenRead(authRecordPath);
                    authRecord = await AuthenticationRecord.DeserializeAsync(stream);
                }
            }
            catch { }

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
            var tokenContext = new TokenRequestContext(_outlookScopes);

            try
            {
                if (authRecord == null)
                {
                    authRecord = await credential.AuthenticateAsync(tokenContext);
                    using var stream = File.Create(authRecordPath);
                    await authRecord.SerializeAsync(stream);
                }
                else
                {
                    await credential.GetTokenAsync(tokenContext);
                }
            }
            catch
            {
                if (File.Exists(authRecordPath)) File.Delete(authRecordPath);
                options.AuthenticationRecord = null;

                authRecord = await credential.AuthenticateAsync(tokenContext);
                using var stream = File.Create(authRecordPath);
                await authRecord.SerializeAsync(stream);
            }

            _outlookClient = new GraphServiceClient(credential, _outlookScopes);
        }

        private void EnsureAccountsLoaded()
        {
            if (_accountsLoaded) return;
            _accountsLoaded = true;

            string path = GetAccountsPath();
            if (!File.Exists(path)) return;

            try
            {
                string json = File.ReadAllText(path);
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
            File.WriteAllText(GetAccountsPath(), json);
        }

        private static string GetAppDataPath()
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskFlyout");

        private static string GetAccountsPath()
            => Path.Combine(GetAppDataPath(), "mail_accounts.json");

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
                Preview = message.BodyPreview ?? "",
                RawReceivedTime = received,
                ReceivedTime = FormatReceivedTime(received),
                IsRead = message.IsRead == true,
                HasAttachments = message.HasAttachments == true,
                Importance = message.Importance?.ToString() ?? "",
                WebLink = message.WebLink ?? ""
            };
        }

        private static MailItem ToGoogleMailItem(string accountId, string folderId, GmailMessage message)
        {
            string subject = GetGoogleHeader(message, "Subject");
            string sender = GetGoogleHeader(message, "From");
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
                Preview = message.Snippet ?? "",
                RawReceivedTime = received,
                ReceivedTime = FormatReceivedTime(received),
                IsRead = message.LabelIds?.Contains("UNREAD") != true,
                HasAttachments = false,
                WebLink = string.IsNullOrWhiteSpace(message.Id) ? "" : $"https://mail.google.com/mail/u/0/#all/{message.Id}"
            };
        }

        private static string GetGoogleHeader(GmailMessage message, string name)
            => message.Payload?.Headers?
                .FirstOrDefault(header => string.Equals(header.Name, name, StringComparison.OrdinalIgnoreCase))
                ?.Value ?? "";

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
    }
}
