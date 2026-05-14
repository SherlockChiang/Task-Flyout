using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Task_Flyout.Services;
using Windows.System;

namespace Task_Flyout.Views
{
    public sealed partial class MailPage : Page
    {
        private readonly ObservableCollection<MailItem> _items = new();
        private readonly Dictionary<string, MailAccount> _accountsById = new();
        private readonly Dictionary<TreeViewNode, MailAccount> _accountNodes = new();
        private readonly Dictionary<TreeViewNode, (MailAccount Account, MailFolder Folder)> _folderNodes = new();
        private MailService? _mailService;
        private MailAccount? _selectedAccount;
        private MailFolder? _selectedFolder;
        private MailItem? _selectedItem;
        private MailAccount? _selectedAccountForRemoval;
        private readonly MailTrustStore _mailTrustStore = new();
        private bool _isInitializing = true;
        private bool _isLoadingMessages;

        public MailPage()
        {
            InitializeComponent();
            Loaded += MailPage_Loaded;
            ActualThemeChanged += MailPage_ActualThemeChanged;
        }

        private async void MailPage_Loaded(object sender, RoutedEventArgs e)
        {
            _mailService = (App.Current as App)?.MailService;
            MailListView.ItemsSource = _items;
            _isInitializing = false;
            await RefreshAccountsAsync();
        }

        private async void MailPage_ActualThemeChanged(FrameworkElement sender, object args)
        {
            if (_selectedItem != null && DetailPanel.Visibility == Visibility.Visible)
                await RenderMailBodyAsync(_selectedItem);
        }

        private async Task RefreshAccountsAsync(bool autoSelect = true)
        {
            if (_mailService == null) return;

            AccountTree.RootNodes.Clear();
            _accountsById.Clear();
            _accountNodes.Clear();
            _folderNodes.Clear();

            var accounts = _mailService.GetAccounts().ToList();
            foreach (var account in accounts)
            {
                _accountsById[account.Id] = account;
                var node = new TreeViewNode
                {
                    Content = FormatAccountContent(account),
                    IsExpanded = false,
                    HasUnrealizedChildren = true
                };
                AccountTree.RootNodes.Add(node);
                _accountNodes[node] = account;
            }

            bool hasAccounts = accounts.Count > 0;
            AddAccountPanel.Visibility = hasAccounts ? Visibility.Collapsed : Visibility.Visible;
            EmptyDetailPanel.Visibility = hasAccounts ? EmptyDetailPanel.Visibility : Visibility.Collapsed;
            MessageListSubtitle.Text = hasAccounts ? "请选择左侧文件夹" : "先添加一个邮箱账户";
            if (!hasAccounts)
            {
                _items.Clear();
                _selectedAccount = null;
                _selectedFolder = null;
                _selectedAccountForRemoval = null;
                RemoveMailButton.IsEnabled = false;
                ClearDetail();
                return;
            }

            if (autoSelect)
                await SelectFirstAvailableFolderAsync();
        }

        private static string FormatAccountContent(MailAccount account)
        {
            string suffix = account.SetupText.Length > 0 ? $" · {account.SetupText}" : "";
            return $"{account.ProviderName} - {account.Subtitle}{suffix}";
        }

        private static string FormatFolderContent(MailFolder folder)
        {
            return string.IsNullOrWhiteSpace(folder.CountText)
                ? folder.DisplayName
                : $"{folder.DisplayName} ({folder.CountText})";
        }

        private async void AccountTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
        {
            await LoadFoldersForNodeAsync(args.Node);
        }

        private void AccountTree_DragItemsCompleted(TreeView sender, TreeViewDragItemsCompletedEventArgs args)
        {
            SaveAccountTreeOrder();
        }

        private void SaveAccountTreeOrder()
        {
            if (_mailService == null) return;

            var accountOrder = AccountTree.RootNodes
                .Where(node => _accountNodes.ContainsKey(node))
                .Select(node => _accountNodes[node].Id)
                .ToList();
            _mailService.SaveMailAccountOrder(accountOrder);

            foreach (var accountNode in AccountTree.RootNodes)
            {
                if (!_accountNodes.TryGetValue(accountNode, out var account)) continue;

                var folderOrder = accountNode.Children
                    .Where(node =>
                        _folderNodes.TryGetValue(node, out var selection) &&
                        string.Equals(selection.Account.Id, account.Id, StringComparison.Ordinal))
                    .Select(node => _folderNodes[node].Folder.Id)
                    .ToList();

                if (folderOrder.Count > 0)
                    _mailService.SaveMailFolderOrder(account.Id, folderOrder);
            }
        }

        private async Task LoadFoldersForNodeAsync(TreeViewNode node, bool forceRefresh = false)
        {
            if (_mailService == null || !_accountNodes.TryGetValue(node, out var account)) return;
            if (!forceRefresh && node.Children.Count > 0 && !node.HasUnrealizedChildren) return;

            node.Children.Clear();
            node.HasUnrealizedChildren = false;

            try
            {
                var folders = await _mailService.FetchFoldersAsync(account, forceRefresh);
                foreach (var folder in folders)
                {
                    var child = new TreeViewNode
                    {
                        Content = FormatFolderContent(folder),
                        HasUnrealizedChildren = false
                    };
                    node.Children.Add(child);
                    _folderNodes[child] = (account, folder);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Load folders failed: {ex.Message}");
                AddStatusText.Text = "加载文件夹失败，请稍后重试。";
            }
        }

        private async Task SelectFirstAvailableFolderAsync()
        {
            if (AccountTree.RootNodes.Count == 0) return;

            var accountNode = AccountTree.RootNodes[0];
            await LoadFoldersForNodeAsync(accountNode);
            accountNode.IsExpanded = true;

            var folderNode = accountNode.Children.FirstOrDefault(node =>
                _folderNodes.TryGetValue(node, out var selection) && !selection.Folder.IsPlaceholder);
            if (folderNode == null) return;

            AccountTree.SelectedNode = folderNode;
            var selected = _folderNodes[folderNode];
            _selectedAccount = selected.Account;
            _selectedFolder = selected.Folder;
                await LoadMessagesAsync();
        }

        public async Task OpenMessageAsync(string accountId, string folderId, string messageId)
        {
            if (!IsLoaded)
                await WaitUntilLoadedAsync();

            _mailService ??= (App.Current as App)?.MailService;
            MailListView.ItemsSource ??= _items;
            _isInitializing = false;
            if (_mailService == null) return;

            await RefreshAccountsAsync(autoSelect: false);

            var accountNode = _accountNodes.FirstOrDefault(pair => pair.Value.Id == accountId).Key;
            if (accountNode == null) return;

            await LoadFoldersForNodeAsync(accountNode);
            accountNode.IsExpanded = true;

            var folderNode = accountNode.Children.FirstOrDefault(node =>
                _folderNodes.TryGetValue(node, out var selection) &&
                string.Equals(selection.Folder.Id, folderId, StringComparison.Ordinal));
            if (folderNode == null) return;

            AccountTree.SelectedNode = folderNode;
            var selected = _folderNodes[folderNode];
            _selectedAccount = selected.Account;
            _selectedFolder = selected.Folder;
            _selectedAccountForRemoval = selected.Account;
            RemoveMailButton.IsEnabled = true;
            UnreadOnlyToggle.IsOn = false;

            await LoadMessagesAsync(forceRefresh: false);

            var target = _items.FirstOrDefault(item => item.Id == messageId);
            if (target == null)
            {
                target = _mailService.TryGetCachedMessage(accountId, folderId, messageId);
                if (target != null)
                {
                    _items.Insert(0, target);
                    MessageListSubtitle.Text = $"{_selectedAccount.DisplayTitle} · {_items.Count} 封邮件";
                }
            }

            if (target != null)
            {
                MailListView.SelectedItem = target;
                MailListView.ScrollIntoView(target);
            }
        }

        private Task WaitUntilLoadedAsync()
        {
            var tcs = new TaskCompletionSource();
            RoutedEventHandler? handler = null;
            handler = (_, _) =>
            {
                Loaded -= handler!;
                tcs.TrySetResult();
            };
            Loaded += handler;
            return tcs.Task;
        }

        private async void AccountTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            if (args.InvokedItem is not TreeViewNode node) return;

            if (_accountNodes.TryGetValue(node, out var account))
            {
                _selectedAccountForRemoval = account;
                RemoveMailButton.IsEnabled = true;
                await SelectFirstFolderForAccountNodeAsync(node);
                return;
            }

            if (!_folderNodes.TryGetValue(node, out var selection)) return;

            _selectedAccount = selection.Account;
            _selectedFolder = selection.Folder;
            _selectedAccountForRemoval = selection.Account;
            RemoveMailButton.IsEnabled = true;
            await LoadMessagesAsync();
        }

        private async Task SelectFirstFolderForAccountNodeAsync(TreeViewNode accountNode)
        {
            await LoadFoldersForNodeAsync(accountNode);
            accountNode.IsExpanded = true;

            var folderNode = accountNode.Children.FirstOrDefault(node =>
                _folderNodes.TryGetValue(node, out var selection) && !selection.Folder.IsPlaceholder);
            if (folderNode == null)
            {
                _items.Clear();
                _selectedFolder = null;
                ClearDetail();
                return;
            }

            AccountTree.SelectedNode = folderNode;
            var selected = _folderNodes[folderNode];
            _selectedAccount = selected.Account;
            _selectedFolder = selected.Folder;
            await LoadMessagesAsync();
        }

        private async Task LoadMessagesAsync(bool forceRefresh = false)
        {
            if (_mailService == null || _selectedAccount == null || _selectedFolder == null) return;

            _isLoadingMessages = true;
            AddAccountPanel.Visibility = Visibility.Collapsed;
            LoadingRing.IsActive = true;
            RefreshButton.IsEnabled = false;
            MessageListTitle.Text = _selectedFolder.DisplayName;
            MessageListSubtitle.Text = $"{_selectedAccount.DisplayTitle} · 正在加载";

            try
            {
                var messages = await _mailService.FetchMessagesAsync(_selectedAccount, _selectedFolder, UnreadOnlyToggle.IsOn, forceRefresh: forceRefresh);
                var previousSelectedId = _selectedItem?.Id;
                _items.Clear();
                foreach (var item in messages.OrderByDescending(item => item.RawReceivedTime))
                    _items.Add(item);

                MessageListSubtitle.Text = $"{_selectedAccount.DisplayTitle} · {_items.Count} 封邮件";
                var itemToSelect = !string.IsNullOrWhiteSpace(previousSelectedId)
                    ? _items.FirstOrDefault(item => item.Id == previousSelectedId)
                    : null;
                MailListView.SelectedItem = itemToSelect ?? (_items.Count > 0 ? _items[0] : null);
                if (_items.Count == 0)
                    ClearDetail();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Load messages failed: {ex.Message}");
                MessageListSubtitle.Text = "加载邮件失败，请稍后重试。";
            }
            finally
            {
                _isLoadingMessages = false;
                LoadingRing.IsActive = false;
                RefreshButton.IsEnabled = true;
            }
        }

        private async void UnreadOnlyToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _selectedFolder == null) return;
            await LoadMessagesAsync();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFolder == null)
            {
                await RefreshAccountsAsync();
                return;
            }

            await LoadMessagesAsync(forceRefresh: true);
        }

        private void AddMailButton_Click(object sender, RoutedEventArgs e)
        {
            ShowAddAccountPanel();
        }

        private async void RemoveMailButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mailService == null || _selectedAccountForRemoval == null) return;

            var account = _selectedAccountForRemoval;
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "删除邮箱账户",
                Content = $"要从 Task Flyout 删除 {account.ProviderName} - {account.Subtitle} 吗？这不会删除邮箱服务器上的邮件。",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            _mailService.RemoveAccount(account.Id);
            _items.Clear();
            _selectedAccount = null;
            _selectedFolder = null;
            _selectedItem = null;
            _selectedAccountForRemoval = null;
            RemoveMailButton.IsEnabled = false;
            ClearDetail();
            await RefreshAccountsAsync();
        }

        private void ShowAddAccountPanel()
        {
            AddAccountPanel.Visibility = Visibility.Visible;
            ComposePanel.Visibility = Visibility.Collapsed;
            DetailPanel.Visibility = Visibility.Collapsed;
            EmptyDetailPanel.Visibility = Visibility.Collapsed;
            AddStatusText.Text = "";
            ImapSettingsPanel.Visibility = Visibility.Collapsed;
        }

        private void ComposeButton_Click(object sender, RoutedEventArgs e)
        {
            ShowComposePanel();
        }

        private void ShowComposePanel(MailItem? replyTo = null)
        {
            if (_mailService == null) return;

            ComposeTitleText.Text = replyTo == null ? "写邮件" : "回复邮件";
            ComposeFromBox.Items.Clear();
            var accounts = _mailService.GetAccounts().Where(account => account.IsSetupComplete).ToList();
            foreach (var account in accounts)
            {
                ComposeFromBox.Items.Add(new ComboBoxItem
                {
                    Content = $"{account.ProviderName} - {account.Subtitle}",
                    Tag = account.Id
                });
            }

            if (_selectedAccount != null)
            {
                var selected = ComposeFromBox.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(item => item.Tag?.ToString() == _selectedAccount.Id);
                if (selected != null) ComposeFromBox.SelectedItem = selected;
            }

            if (ComposeFromBox.SelectedItem == null && ComposeFromBox.Items.Count > 0)
                ComposeFromBox.SelectedIndex = 0;

            ComposeToBox.Text = replyTo == null ? "" : GetReplyRecipient(replyTo);
            ComposeSubjectBox.Text = replyTo == null ? "" : CreateReplySubject(replyTo.Subject);
            ComposeBodyBox.Text = replyTo == null ? "" : CreateReplyBody(replyTo);
            ComposeStatusText.Text = accounts.Count == 0 ? "请先添加邮箱账户。" : "";

            ComposePanel.Visibility = Visibility.Visible;
            AddAccountPanel.Visibility = Visibility.Collapsed;
            DetailPanel.Visibility = Visibility.Collapsed;
            EmptyDetailPanel.Visibility = Visibility.Collapsed;
        }

        private async void AddOutlookButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mailService == null) return;

            SetAddButtonsEnabled(false);
            AddStatusText.Text = "正在打开 Microsoft 授权...";

            try
            {
                var account = await _mailService.AddOutlookAccountAsync();
                AddStatusText.Text = $"已添加 {account.DisplayTitle}";
                await RefreshAccountsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Add Outlook account failed: {ex.Message}");
                AddStatusText.Text = "添加 Outlook 账户失败，请检查授权或网络连接。";
            }
            finally
            {
                SetAddButtonsEnabled(true);
            }
        }

        private void AddGmailButton_Click(object sender, RoutedEventArgs e)
        {
            _ = AddGoogleAccountAsync();
        }

        private void AddImapButton_Click(object sender, RoutedEventArgs e)
        {
            ShowImapSettings();
        }

        private async Task AddGoogleAccountAsync()
        {
            if (_mailService == null) return;

            SetAddButtonsEnabled(false);
            AddStatusText.Text = "正在打开 Google 授权...";

            try
            {
                var account = await _mailService.AddGoogleAccountAsync();
                AddStatusText.Text = $"已添加 {account.DisplayTitle}";
                await RefreshAccountsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Add Google account failed: {ex.Message}");
                AddStatusText.Text = "添加 Gmail 账户失败，请检查授权或网络连接。";
            }
            finally
            {
                SetAddButtonsEnabled(true);
            }
        }

        private void ShowImapSettings()
        {
            ImapSettingsPanel.Visibility = Visibility.Visible;
            ImapDisplayNameBox.Text = "";
            ImapAddressBox.Text = "";
            ImapUserNameBox.Text = "";
            ImapPasswordBox.Password = "";
            ImapHostBox.Text = "";
            ImapPortBox.Value = 993;
            ImapSslToggle.IsOn = true;
            SmtpHostBox.Text = "";
            SmtpPortBox.Value = 587;
            SmtpUserNameBox.Text = "";
            SmtpSslToggle.IsOn = false;
            AddStatusText.Text = "请输入 IMAP 服务器信息。Gmail/Outlook 建议优先使用 OAuth。";
        }

        private void CancelImapButton_Click(object sender, RoutedEventArgs e)
        {
            ImapSettingsPanel.Visibility = Visibility.Collapsed;
            AddStatusText.Text = "";
        }

        private async void SaveImapButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mailService == null) return;

            string address = ImapAddressBox.Text.Trim();
            string host = ImapHostBox.Text.Trim();
            string userName = string.IsNullOrWhiteSpace(ImapUserNameBox.Text) ? address : ImapUserNameBox.Text.Trim();
            string password = ImapPasswordBox.Password;
            int port = double.IsNaN(ImapPortBox.Value) ? 993 : (int)ImapPortBox.Value;
            string smtpHost = SmtpHostBox.Text.Trim();
            int smtpPort = double.IsNaN(SmtpPortBox.Value) ? 587 : (int)SmtpPortBox.Value;
            string smtpUserName = SmtpUserNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(address))
            {
                AddStatusText.Text = "请输入邮箱地址。";
                return;
            }
            if (string.IsNullOrWhiteSpace(host))
            {
                AddStatusText.Text = "请输入 IMAP 服务器。";
                return;
            }
            if (string.IsNullOrWhiteSpace(password))
            {
                AddStatusText.Text = "请输入密码或应用专用密码。";
                return;
            }
            if (string.IsNullOrWhiteSpace(smtpHost))
            {
                AddStatusText.Text = "请输入 SMTP 服务器，IMAP 账户需要它来发送邮件。";
                return;
            }

            SetAddButtonsEnabled(false);
            AddStatusText.Text = "正在连接 IMAP 服务器...";

            try
            {
                var account = await _mailService.AddImapAccountAsync(
                    ImapDisplayNameBox.Text,
                    address,
                    userName,
                    password,
                    host,
                    port,
                    ImapSslToggle.IsOn,
                    smtpHost,
                    smtpPort,
                    SmtpSslToggle.IsOn,
                    smtpUserName);

                AddStatusText.Text = $"已添加 {account.DisplayTitle}";
                await RefreshAccountsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Add IMAP account failed: {ex.Message}");
                AddStatusText.Text = "连接 IMAP 服务器失败，请检查服务器设置和凭据。";
            }
            finally
            {
                SetAddButtonsEnabled(true);
            }
        }

        private void SetAddButtonsEnabled(bool isEnabled)
        {
            AddOutlookButton.IsEnabled = isEnabled;
            AddGmailButton.IsEnabled = isEnabled;
            AddImapButton.IsEnabled = isEnabled;
            SaveImapButton.IsEnabled = isEnabled;
        }

        private async void MailListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MailListView.SelectedItem is not MailItem item)
            {
                if (!_isLoadingMessages)
                    ClearDetail();
                return;
            }

            _selectedItem = item;
            AddAccountPanel.Visibility = Visibility.Collapsed;
            ComposePanel.Visibility = Visibility.Collapsed;
            EmptyDetailPanel.Visibility = Visibility.Collapsed;
            DetailPanel.Visibility = Visibility.Visible;
            DetailSubject.Text = item.Subject;
            DetailSender.Text = item.Sender;
            DetailTime.Text = item.RawReceivedTime?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? item.ReceivedTime;
            UpdateTrustButton(item);
            await RenderMailBodyAsync(item);
            ReplyButton.IsEnabled = _selectedAccount != null;
            OpenInBrowserButton.IsEnabled = !string.IsNullOrWhiteSpace(item.WebLink);

            if (_mailService != null && _selectedAccount != null && !item.IsRead)
            {
                try
                {
                    await _mailService.MarkAsReadAsync(_selectedAccount, item);
                    if (UnreadOnlyToggle.IsOn)
                    {
                        _items.Remove(item);
                        MessageListSubtitle.Text = _selectedAccount != null ? $"{_selectedAccount.DisplayTitle} · {_items.Count} 封邮件" : MessageListSubtitle.Text;
                    }
                    else
                    {
                        MailListView.SelectedItem = item;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Mark as read failed: {ex.Message}");
                    MessageListSubtitle.Text = "标记已读失败。";
                }
            }
        }

        private void ClearDetail()
        {
            _selectedItem = null;
            DetailPanel.Visibility = Visibility.Collapsed;
            DetailHtmlView.Visibility = Visibility.Collapsed;
            DetailTextScrollViewer.Visibility = Visibility.Visible;
            DetailPreview.Text = "";
            TrustSenderButton.IsEnabled = false;
            if (AddAccountPanel.Visibility != Visibility.Visible && ComposePanel.Visibility != Visibility.Visible)
                EmptyDetailPanel.Visibility = Visibility.Visible;
        }

        private bool _webView2Configured;
        private bool _isInternalMailHtmlNavigation;
        private static bool _mailWebViewCachePathConfigured;

        private async Task RenderMailBodyAsync(MailItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.HtmlBody))
            {
                var trusted = _mailTrustStore.IsTrusted(item);
                var html = trusted
                    ? SanitizeTrustedMailHtml(item.HtmlBody)
                    : SanitizeMailHtml(item.HtmlBody);

                if (trusted || HasVisibleHtmlContent(html))
                {
                    var htmlDocument = BuildMailHtmlDocument(html, IsDarkThemeActive(), alreadySanitized: true);
                    try
                    {
                        var itemId = item.Id;
                        DetailPreview.Text = "";
                        DetailTextScrollViewer.Visibility = Visibility.Collapsed;
                        DetailHtmlView.Visibility = Visibility.Visible;
                        EnsureMailWebViewCachePathConfigured();
                        await DetailHtmlView.EnsureCoreWebView2Async();
                        if (!_webView2Configured)
                        {
                            var coreWebView = DetailHtmlView.CoreWebView2;
                            if (coreWebView == null)
                                throw new InvalidOperationException("Mail WebView2 failed to initialize.");

                            _webView2Configured = true;
                            var settings = coreWebView.Settings;
                            settings.IsScriptEnabled = false;
                            settings.IsWebMessageEnabled = false;
                            settings.AreDefaultContextMenusEnabled = false;
                            settings.AreDevToolsEnabled = false;
                            settings.IsStatusBarEnabled = false;
                            settings.IsPinchZoomEnabled = false;
                            settings.IsSwipeNavigationEnabled = false;
                            coreWebView.NavigationStarting += (_, args) =>
                            {
                                if (_isInternalMailHtmlNavigation)
                                    return;

                                if (!args.IsRedirected && args.Uri != "about:blank")
                                {
                                    args.Cancel = true;
                                    OpenSafeExternalUri(args.Uri);
                                }
                            };
                            coreWebView.NavigationCompleted += (_, _) =>
                            {
                                _isInternalMailHtmlNavigation = false;
                            };
                            coreWebView.NewWindowRequested += (_, args) =>
                            {
                                args.Handled = true;
                                OpenSafeExternalUri(args.Uri);
                            };
                        }
                        if (_selectedItem?.Id != itemId) return;
                        _isInternalMailHtmlNavigation = true;
                        DetailHtmlView.NavigateToString(htmlDocument);
                        return;
                    }
                    catch (Exception ex)
                    {
                        _isInternalMailHtmlNavigation = false;
                        System.Diagnostics.Debug.WriteLine($"HTML mail render failed: {ex.Message}");
                    }
                }
            }

            ShowPlainTextMailBody(item);
        }

        private static void EnsureMailWebViewCachePathConfigured()
        {
            if (_mailWebViewCachePathConfigured) return;

            var cachePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TaskFlyout",
                "MailWebView2Cache");
            Directory.CreateDirectory(cachePath);
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", cachePath, EnvironmentVariableTarget.Process);
            _mailWebViewCachePathConfigured = true;
        }

        private void ShowPlainTextMailBody(MailItem item)
        {
            DetailHtmlView.Visibility = Visibility.Collapsed;
            DetailTextScrollViewer.Visibility = Visibility.Visible;
            var fallbackText = item.BodyText;
            if (string.IsNullOrWhiteSpace(fallbackText) && !string.IsNullOrWhiteSpace(item.HtmlBody))
                fallbackText = BuildPlainTextFallback(item.HtmlBody);

            DetailPreview.Text = string.IsNullOrWhiteSpace(fallbackText)
                ? string.IsNullOrWhiteSpace(item.Preview) ? "没有可用正文。" : item.Preview
                : fallbackText;
        }

        private void UpdateTrustButton(MailItem item)
        {
            var source = MailTrustStore.GetDisplaySource(item);
            var trusted = _mailTrustStore.IsTrusted(item);
            TrustSenderButton.IsEnabled = source != "未知来源";
            TrustSenderButtonText.Text = trusted ? "取消信任来源" : "信任此来源";
            ToolTipService.SetToolTip(TrustSenderButton, trusted ? $"已信任：{source}" : $"信任后将允许加载此来源的远程邮件内容：{source}");
        }

        private bool IsDarkThemeActive()
        {
            var theme = ActualTheme;
            if (theme == ElementTheme.Default && App.MyMainWindow?.Content is FrameworkElement root)
                theme = root.ActualTheme;

            return theme == ElementTheme.Dark;
        }

        private static string BuildMailHtmlDocument(string html, bool isDarkTheme, bool alreadySanitized = false)
        {
            var safeHtml = alreadySanitized ? html : SanitizeMailHtml(html);
            var background = isDarkTheme ? "#1f1f1f" : "#ffffff";
            var text = isDarkTheme ? "#f3f3f3" : "#202020";
            var muted = isDarkTheme ? "#d7d7d7" : "#4b5563";
            var border = isDarkTheme ? "#3a3a3a" : "#e5e7eb";
            var link = isDarkTheme ? "#8ab4f8" : "#2563eb";
            var scheme = isDarkTheme ? "dark" : "light";
            var darkOverride = isDarkTheme
                ? $$"""
html, body, .mail-shell {
    background: {{background}} !important;
    color: {{text}} !important;
}
body *:not(img):not(video):not(canvas) {
    color: {{text}} !important;
    border-color: {{border}} !important;
}
body table, body tbody, body thead, body tfoot, body tr, body td, body th,
body div, body section, body article, body header, body footer, body main,
body p, body span, body font, body center, body blockquote, body dl, body dt, body dd,
body ul, body ol, body li {
    background-color: transparent !important;
}
body [bgcolor] {
    background-color: transparent !important;
}
body a, body a * {
    color: {{link}} !important;
}
"""
                : "";
            var baseStyle = $$"""
<style>
:root { color-scheme: {{scheme}}; }
html, body {
    margin: 0;
    padding: 0;
    background: {{background}};
    color: {{text}};
    font-family: "Segoe UI", Arial, sans-serif;
    font-size: 14px;
    line-height: 1.45;
    overflow-wrap: anywhere;
}
body { padding: 2px; }
img { max-width: 100%; height: auto; }
table { max-width: 100%; border-collapse: collapse; }
td, th { border-color: {{border}}; }
a { color: {{link}}; }
pre { white-space: pre-wrap; overflow-wrap: anywhere; }
body, div, p, span, td, th, li, blockquote { color: inherit; }
body { background-color: {{background}} !important; }
.mail-shell { min-height: 100vh; background: {{background}}; color: {{text}}; }
.mail-shell * { scrollbar-color: {{muted}} {{background}}; }
{{darkOverride}}
@media (prefers-color-scheme: dark) {
    html, body { background: {{background}}; color: {{text}}; }
}
</style>
""";

            if (System.Text.RegularExpressions.Regex.IsMatch(safeHtml, @"<\s*html\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(safeHtml, @"<\s*/\s*head\s*>", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    return System.Text.RegularExpressions.Regex.Replace(safeHtml, @"<\s*/\s*head\s*>", baseStyle + "</head>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                return System.Text.RegularExpressions.Regex.Replace(
                    safeHtml,
                    @"<\s*html\b[^>]*>",
                    match => match.Value + baseStyle,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase,
                    TimeSpan.FromMilliseconds(100));
            }

            return """
<!doctype html>
<html>
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
""" + baseStyle + """
</head>
<body>
<div class="mail-shell">
""" + safeHtml + """
</div>
</body>
</html>
""";
        }

        private static void OpenSafeExternalUri(string uriText)
        {
            if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri)) return;
            if (uri.Scheme != "http" && uri.Scheme != "https") return;
            _ = Launcher.LaunchUriAsync(uri);
        }

        private static string SanitizeMailHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return "";

            var value = html;
            // Remove block-level dangerous elements (paired tags)
            value = System.Text.RegularExpressions.Regex.Replace(value, @"<\s*(script|iframe|object|embed|form|input|button|textarea|select|svg|noscript|template|base)\b[^>]*>.*?<\s*/\s*\1\s*>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
            // Remove self-closing / standalone dangerous elements
            value = System.Text.RegularExpressions.Regex.Replace(value, @"<\s*(script|iframe|object|embed|form|input|button|textarea|select|svg|noscript|template|base)\b[^>]*/?\s*>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
            // Remove event handler attributes (on*)
            value = System.Text.RegularExpressions.Regex.Replace(value, @"\s+on\w+\s*=\s*(['""]).*?\1", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
            value = System.Text.RegularExpressions.Regex.Replace(value, @"\s+on\w+\s*=\s*[^\s>]+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            // Remove javascript: and vbscript: URIs
            value = System.Text.RegularExpressions.Regex.Replace(value, @"(href|src|action|formaction|data)\s*=\s*(['""])\s*(javascript|vbscript|data):.*?\2", "$1=\"#\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
            value = System.Text.RegularExpressions.Regex.Replace(value, @"(href|src|action|formaction|data)\s*=\s*(javascript|vbscript|data):[^\s>]+", "$1=\"#\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            // Block remote resources such as tracking pixels. Links remain clickable through the WebView navigation handler.
            value = System.Text.RegularExpressions.Regex.Replace(value, @"\s(src|srcset|background)\s*=\s*(['""])\s*https?://.*?\2", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
            value = System.Text.RegularExpressions.Regex.Replace(value, @"\s(src|srcset|background)\s*=\s*https?://[^\s>]+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            // Remove dangerous CSS in style attributes (expression, url(), -moz-binding, behavior)
            value = System.Text.RegularExpressions.Regex.Replace(value, @"style\s*=\s*(['""])[^'""]*\b(expression|url\s*\(|-moz-binding|behavior)\b[^'""]*\1", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
            value = System.Text.RegularExpressions.Regex.Replace(value, @"url\s*\(\s*(['""]?)https?://.*?\1\s*\)", "none", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

            if (!System.Text.RegularExpressions.Regex.IsMatch(value, @"<\s*(html|head|body|style|table|div|p|span|br|img|a|meta)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                value = $"<pre>{WebUtility.HtmlEncode(RemoveCssNoise(value))}</pre>";

            return value;
        }

        private static string SanitizeTrustedMailHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return "";

            var value = html;
            value = System.Text.RegularExpressions.Regex.Replace(value, @"<\s*(script|iframe|object|embed|form|input|button|textarea|select|svg|noscript|template|base)\b[^>]*>.*?<\s*/\s*\1\s*>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
            value = System.Text.RegularExpressions.Regex.Replace(value, @"<\s*(script|iframe|object|embed|form|input|button|textarea|select|svg|noscript|template|base)\b[^>]*/?\s*>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
            value = System.Text.RegularExpressions.Regex.Replace(value, @"\s+on\w+\s*=\s*(['""]).*?\1", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
            value = System.Text.RegularExpressions.Regex.Replace(value, @"\s+on\w+\s*=\s*[^\s>]+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            value = System.Text.RegularExpressions.Regex.Replace(value, @"(href|action|formaction)\s*=\s*(['""])\s*(javascript|vbscript):.*?\2", "$1=\"#\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
            value = System.Text.RegularExpressions.Regex.Replace(value, @"(href|action|formaction)\s*=\s*(javascript|vbscript):[^\s>]+", "$1=\"#\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            value = System.Text.RegularExpressions.Regex.Replace(value, @"style\s*=\s*(['""])[^'""]*\b(expression|-moz-binding|behavior)\b[^'""]*\1", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

            if (!System.Text.RegularExpressions.Regex.IsMatch(value, @"<\s*(html|head|body|style|table|div|p|span|br|img|a|meta)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                value = $"<pre>{WebUtility.HtmlEncode(RemoveCssNoise(value))}</pre>";

            return value;
        }

        private static bool HasVisibleHtmlContent(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return false;

            var withoutNonContent = System.Text.RegularExpressions.Regex.Replace(
                html,
                @"<\s*(head|style|script|meta|link)\b[^>]*>.*?<\s*/\s*\1\s*>|<\s*(meta|link)\b[^>]*/?\s*>",
                " ",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

            var text = System.Text.RegularExpressions.Regex.Replace(withoutNonContent, "<.*?>", " ");
            text = WebUtility.HtmlDecode(text).Trim();
            if (text.Length > 0)
                return true;

            if (System.Text.RegularExpressions.Regex.IsMatch(
                    withoutNonContent,
                    @"<\s*img\b[^>]+\bsrc\s*=\s*(['""])(?!https?://|cid:|data:)[^'""]+\1",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return true;

            return false;
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

            return string.Join("\n", kept).Trim();
        }

        private static string BuildPlainTextFallback(string value)
        {
            value = System.Text.RegularExpressions.Regex.Replace(value, @"<\s*(head|style|script)\b[^>]*>.*?<\s*/\s*\1\s*>", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
            value = System.Text.RegularExpressions.Regex.Replace(value, @"<\s*(meta|link)\b[^>]*/?\s*>", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
            value = System.Text.RegularExpressions.Regex.Replace(value, "<.*?>", " ");
            value = RemoveCssNoise(value);
            value = WebUtility.HtmlDecode(value);
            return System.Text.RegularExpressions.Regex.Replace(value, @"[ \t]{2,}", " ").Trim();
        }

        private static bool IsCssNoiseLine(string line)
        {
            if (line == "{" || line == "}") return true;
            if (line.StartsWith("@media", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("@font-face", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("@-moz", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("@supports", StringComparison.OrdinalIgnoreCase))
                return true;
            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^[.#][\w\-#.:\s,>+~\[\]=""']+\{?$")) return true;
            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^[a-zA-Z\-]+\s*:\s*[^。！？；，、]*;?$")) return true;
            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^[a-zA-Z][\w\-#.\s,>+~\[\]=""']+\s*\{")) return true;
            return false;
        }

        private void CancelComposeButton_Click(object sender, RoutedEventArgs e)
        {
            ComposePanel.Visibility = Visibility.Collapsed;
            ClearDetail();
        }

        private void ReplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null) return;
            ShowComposePanel(_selectedItem);
        }

        private async void SendComposeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mailService == null) return;

            if (ComposeFromBox.SelectedItem is not ComboBoxItem selectedFrom)
            {
                ComposeStatusText.Text = "请选择发件人。";
                return;
            }

            var account = _mailService.GetAccounts().FirstOrDefault(a => a.Id == selectedFrom.Tag?.ToString());
            if (account == null)
            {
                ComposeStatusText.Text = "找不到发件人账户。";
                return;
            }

            if (string.IsNullOrWhiteSpace(ComposeToBox.Text))
            {
                ComposeStatusText.Text = "请输入收件人。";
                return;
            }

            SendComposeButton.IsEnabled = false;
            ComposeStatusText.Text = "正在发送...";

            try
            {
                await _mailService.SendMailAsync(account, ComposeToBox.Text, ComposeSubjectBox.Text, ComposeBodyBox.Text);
                ComposeStatusText.Text = "邮件已发送。";
                ComposePanel.Visibility = Visibility.Collapsed;
                ClearDetail();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Send mail failed: {ex.Message}");
                ComposeStatusText.Text = "发送邮件失败，请检查网络连接和账户设置。";
            }
            finally
            {
                SendComposeButton.IsEnabled = true;
            }
        }

        private async void OpenInBrowserButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null || string.IsNullOrWhiteSpace(_selectedItem.WebLink)) return;
            if (Uri.TryCreate(_selectedItem.WebLink, UriKind.Absolute, out var uri) &&
                (uri.Scheme == "http" || uri.Scheme == "https"))
                await Launcher.LaunchUriAsync(uri);
        }

        private async void TrustSenderButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null) return;

            if (_mailTrustStore.IsTrusted(_selectedItem))
                _mailTrustStore.Untrust(_selectedItem);
            else
                _mailTrustStore.Trust(_selectedItem);

            UpdateTrustButton(_selectedItem);
            await RenderMailBodyAsync(_selectedItem);
        }

        private static string GetReplyRecipient(MailItem item)
            => string.IsNullOrWhiteSpace(item.SenderAddress) ? item.Sender : item.SenderAddress;

        private static string CreateReplySubject(string subject)
        {
            if (string.IsNullOrWhiteSpace(subject)) return "Re:";
            return subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ? subject : $"Re: {subject}";
        }

        private static string CreateReplyBody(MailItem item)
        {
            var received = item.RawReceivedTime?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? item.ReceivedTime;
            var original = string.IsNullOrWhiteSpace(item.BodyText) ? item.Preview : item.BodyText;
            return $"\r\n\r\n---- 原始邮件 ----\r\n发件人: {item.Sender}\r\n时间: {received}\r\n主题: {item.Subject}\r\n\r\n{original}";
        }
    }
}
