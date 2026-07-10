using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Task_Flyout.Services;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.System;
using Windows.Storage.Streams;
using System.Runtime.InteropServices.WindowsRuntime;

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
        private readonly ResourceLoader _loader = new();

        // How many messages the current folder view is requesting. Grows by PageStep
        // when the user taps "Load more"; reset to the base PageSize on a fresh load.
        private int _loadedCount;
        private const int PageStep = 25;

        // Pre-compiled regex patterns for mail HTML sanitization
        private static readonly Regex RxHtmlContentTags = new(@"<\s*(html|head|body|style|table|div|p|span|br|img|a|meta)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxHtmlTag = new(@"<\s*html\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxHeadClose = new(@"<\s*/\s*head\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxHtmlOpenTag = new(@"<\s*html\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxNonContentTags = new(@"<\s*(head|style|script|meta|link)\b[^>]*>.*?<\s*/\s*\1\s*>|<\s*(meta|link)\b[^>]*/?\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RxHtmlTagsOnly = new("<.*?>", RegexOptions.Compiled);
        private static readonly Regex RxLocalImgSrc = new(@"<\s*img\b[^>]+\bsrc\s*=\s*(['""])(?!https?://|cid:|data:)[^'""]+\1", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxPlainTextHeadStyle = new(@"<\s*(head|style|script)\b[^>]*>.*?<\s*/\s*\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RxPlainTextMetaLink = new(@"<\s*(meta|link)\b[^>]*/?\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RxMultiSpace = new(@"[ \t]{2,}", RegexOptions.Compiled);
        private static readonly Regex RxCssSelector = new(@"^[.#][\w\-#.:\s,>+~\[\]=""']+\{?$", RegexOptions.Compiled);
        private static readonly Regex RxCssProperty = new(@"^[a-zA-Z\-]+\s*:\s*[^。！？；，、]*;?$", RegexOptions.Compiled);
        private static readonly Regex RxCssRuleStart = new(@"^[a-zA-Z][\w\-#.\s,>+~\[\]=""']+\s*\{", RegexOptions.Compiled);
        private bool _isInitializing = true;
        private bool _isLoadingMessages;
        private bool _suppressSelectionClear;
        private bool _suppressUnreadToggle;
        private int _messageLoadVersion;
        private DateTimeOffset? _lastMessageLoadSucceededAt;
        private Task? _refreshAccountsTask;
        private CancellationTokenSource _pageRequestCts = new();
        private CancellationTokenSource? _messageLoadCts;
        private CancellationTokenSource? _bodyLoadCts;
        internal bool IsOpeningFromNotification { get; set; }

        public MailPage()
        {
            this.Language = Windows.Globalization.ApplicationLanguages.Languages[0];
            InitializeComponent();
            Loaded += MailPage_Loaded;
            Unloaded += MailPage_Unloaded;
            ActualThemeChanged += MailPage_ActualThemeChanged;
        }

        private string GetResourceStringOrDefault(string resourceId, string fallback)
        {
            try
            {
                var value = _loader.GetStringOrDefault(resourceId);
                return string.IsNullOrWhiteSpace(value) ? fallback : value;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Resource lookup failed for {resourceId}: {ex.Message}");
                return fallback;
            }
        }

        private void MailPage_Unloaded(object sender, RoutedEventArgs e)
        {
            DisposeLikeCleanup();
        }

        public void DisposeLikeCleanup()
        {
            _pageRequestCts.Cancel();
            _messageLoadCts?.Cancel();
            _bodyLoadCts?.Cancel();
            _messageLoadVersion++;
            ReleaseMessageBodies();
            _selectedItem = null;
            _selectedAccount = null;
            _selectedFolder = null;
            _selectedAccountForRemoval = null;
            _refreshAccountsTask = null;

            ReleaseMailWebView();
            _isInternalMailHtmlNavigation = false;
            MailListView.ItemsSource = null;
            _items.Clear();
            _accountsById.Clear();
            _accountNodes.Clear();
            _folderNodes.Clear();
        }

        private async void MailPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (_pageRequestCts.IsCancellationRequested)
            {
                _pageRequestCts.Dispose();
                _pageRequestCts = new CancellationTokenSource();
            }
            _mailService = (App.Current as App)?.MailService;
            MailListView.ItemsSource = _items;
            _isInitializing = false;
            await RefreshAccountsAsync(autoSelect: !IsOpeningFromNotification);
        }

        private async void MailPage_ActualThemeChanged(FrameworkElement sender, object args)
        {
            if (_selectedItem != null && DetailPanel.Visibility == Visibility.Visible)
                await RenderMailBodyAsync(_selectedItem);
        }

        private async Task RefreshAccountsAsync(bool autoSelect = true)
        {
            if (_mailService == null) return;

            if (_refreshAccountsTask != null)
            {
                await _refreshAccountsTask;
                return;
            }

            var tcs = new TaskCompletionSource();
            _refreshAccountsTask = tcs.Task;
            try
            {
                await RefreshAccountsCoreAsync(autoSelect);
            }
            finally
            {
                _refreshAccountsTask = null;
                tcs.SetResult();
            }
        }

        private async Task RefreshAccountsCoreAsync(bool autoSelect)
        {
            var mailService = _mailService;
            if (mailService == null) return;

            AccountTree.RootNodes.Clear();
            _accountsById.Clear();
            _accountNodes.Clear();
            _folderNodes.Clear();

            var accounts = mailService.GetAccounts().ToList();
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
            SetMessageListStatus(hasAccounts ? (_loader.GetStringOrDefault("TextSelectFolder") ?? "Select a folder on the left") : (_loader.GetStringOrDefault("TextAddMailAccountFirst") ?? "Add an email account first"));
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
            var cancellationToken = _pageRequestCts.Token;

            try
            {
                var folders = await _mailService.FetchFoldersAsync(account, forceRefresh, cancellationToken);
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Load folders failed: {ex.Message}");
                AddStatusText.Text = _loader.GetStringOrDefault("TextLoadFoldersFailed") ?? "Failed to load folders, please try again later.";
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
            SetUnreadOnlyWithoutReload(false);

            await LoadMessagesAsync(forceRefresh: false, preferredMessageId: messageId, selectFirstWhenNoMatch: false);

            var target = _items.FirstOrDefault(item => item.Id == messageId);
            if (target == null)
            {
                target = _mailService.TryGetCachedMessage(accountId, folderId, messageId);
                if (target != null)
                {
                    _items.Insert(0, target);
                    SetMessageListStatus($"{_selectedAccount.DisplayTitle} · {string.Format(_loader.GetStringOrDefault("TextNMailItems") ?? "{0} messages", _items.Count)}");
                }
            }

            if (target == null)
            {
                await LoadMessagesAsync(forceRefresh: true, preferredMessageId: messageId, selectFirstWhenNoMatch: false);
                target = _items.FirstOrDefault(item => item.Id == messageId);
            }

            if (target != null)
            {
                MailListView.SelectedItem = target;
                MailListView.ScrollIntoView(target);
            }
            else
            {
                SetMessageListStatus(_loader.GetStringOrDefault("TextMailNotFound") ?? "This message was not found in the local cache or current folder.", isError: true);
                ClearDetail();
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

        private void AccountTree_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
        {
            UpdateSelectedAccountForRemoval(sender.SelectedNode);
        }

        private MailAccount? ResolveSelectedAccountForRemoval()
        {
            if (_selectedAccountForRemoval != null)
                return _selectedAccountForRemoval;

            if (UpdateSelectedAccountForRemoval(AccountTree.SelectedNode))
                return _selectedAccountForRemoval;

            return _selectedAccount;
        }

        private bool UpdateSelectedAccountForRemoval(TreeViewNode? node)
        {
            if (node != null)
            {
                if (_accountNodes.TryGetValue(node, out var account))
                {
                    _selectedAccountForRemoval = account;
                    RemoveMailButton.IsEnabled = true;
                    return true;
                }

                if (_folderNodes.TryGetValue(node, out var selection))
                {
                    _selectedAccountForRemoval = selection.Account;
                    RemoveMailButton.IsEnabled = true;
                    return true;
                }
            }

            _selectedAccountForRemoval = _selectedAccount;
            RemoveMailButton.IsEnabled = _selectedAccountForRemoval != null;
            return _selectedAccountForRemoval != null;
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

        private async Task LoadMessagesAsync(bool forceRefresh = false, string? preferredMessageId = null, bool selectFirstWhenNoMatch = true, bool loadMore = false)
        {
            if (_mailService == null || _selectedAccount == null || _selectedFolder == null) return;

            var loadVersion = ++_messageLoadVersion;
            var account = _selectedAccount;
            var folder = _selectedFolder;
            _messageLoadCts?.Cancel();
            var requestCts = CancellationTokenSource.CreateLinkedTokenSource(_pageRequestCts.Token);
            _messageLoadCts = requestCts;

            // A fresh load (folder switch, refresh, toggle) starts from the base page
            // size again; only "Load more" keeps the grown window.
            if (!loadMore)
                _loadedCount = Math.Clamp(_mailService.PageSize, MailService.MinPageSize, MailService.MaxPageSize);

            _isLoadingMessages = true;
            AddAccountPanel.Visibility = Visibility.Collapsed;
            LoadingRing.IsActive = true;
            RefreshButton.IsEnabled = false;
            LoadMoreButton.IsEnabled = false;
            UnreadOnlyToggle.IsEnabled = false;
            MessageListTitle.Text = folder.DisplayName;
            SetMessageListStatus($"{account.DisplayTitle} · {(_loader.GetStringOrDefault("TextLoading") ?? "Loading")}");

            try
            {
                var messages = await _mailService.FetchMessagesAsync(account, folder, UnreadOnlyToggle.IsOn, pageSize: _loadedCount, forceRefresh: forceRefresh || loadMore, cancellationToken: requestCts.Token);
                if (loadVersion != _messageLoadVersion || !ReferenceEquals(account, _selectedAccount) || !ReferenceEquals(folder, _selectedFolder))
                    return;
                var previousSelectedId = !string.IsNullOrWhiteSpace(preferredMessageId) ? preferredMessageId : _selectedItem?.Id;
                if (loadMore)
                {
                    // Providers currently return an expanded window rather than a cursor
                    // page. Keep realized rows intact and append only the newly exposed IDs.
                    var existingIds = _items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
                    foreach (var item in messages.OrderByDescending(item => item.RawReceivedTime))
                    {
                        if (existingIds.Add(item.Id))
                            _items.Add(item);
                    }
                }
                else
                {
                    _items.Clear();
                    foreach (var item in messages.OrderByDescending(item => item.RawReceivedTime))
                        _items.Add(item);
                }

                _lastMessageLoadSucceededAt = DateTimeOffset.Now;
                SetMessageListStatus($"{account.DisplayTitle} · {string.Format(_loader.GetStringOrDefault("TextNMailItems") ?? "{0} messages", _items.Count)}");

                // Offer "Load more" only while the server filled the whole window (so older
                // messages likely remain) and we haven't hit the fetch ceiling.
                bool mightHaveMore = _items.Count >= _loadedCount && _loadedCount < MailService.MaxPageSize && !UnreadOnlyToggle.IsOn;
                LoadMoreButton.Visibility = mightHaveMore ? Visibility.Visible : Visibility.Collapsed;

                var itemToSelect = !string.IsNullOrWhiteSpace(previousSelectedId)
                    ? _items.FirstOrDefault(item => item.Id == previousSelectedId)
                    : null;
                if (itemToSelect != null)
                    MailListView.SelectedItem = itemToSelect;
                else if (selectFirstWhenNoMatch)
                    MailListView.SelectedItem = _items.Count > 0 ? _items[0] : null;
                else
                    MailListView.SelectedItem = null;

                if (_items.Count == 0)
                    ClearDetail();
            }
            catch (OperationCanceledException) when (requestCts.IsCancellationRequested) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Load messages failed: {ex.Message}");
                SetMessageListStatus(_loader.GetStringOrDefault("TextLoadMailFailed") ?? "Failed to load messages, please try again later.", isError: true);
            }
            finally
            {
                if (loadVersion == _messageLoadVersion)
                {
                    _isLoadingMessages = false;
                    LoadingRing.IsActive = false;
                    RefreshButton.IsEnabled = true;
                    LoadMoreButton.IsEnabled = true;
                    UnreadOnlyToggle.IsEnabled = true;
                }
                if (ReferenceEquals(_messageLoadCts, requestCts))
                    _messageLoadCts = null;
                requestCts.Dispose();
            }
        }

        private async void LoadMoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoadingMessages || _mailService == null) return;

            int previousCount = _items.Count;
            var listScrollViewer = FindVisualChild<ScrollViewer>(MailListView);
            double? previousVerticalOffset = listScrollViewer?.VerticalOffset;

            _loadedCount = Math.Min(_loadedCount + PageStep, MailService.MaxPageSize);
            await LoadMessagesAsync(loadMore: true, selectFirstWhenNoMatch: false);
            await RestoreMailListScrollPositionAsync(listScrollViewer, previousVerticalOffset);

            // Nothing new came back — we've reached the end of the folder.
            if (_items.Count <= previousCount)
                LoadMoreButton.Visibility = Visibility.Collapsed;
        }

        private async Task RestoreMailListScrollPositionAsync(ScrollViewer? scrollViewer, double? verticalOffset)
        {
            if (scrollViewer == null || verticalOffset == null) return;

            await Task.Yield();
            MailListView.UpdateLayout();

            double targetOffset = Math.Min(verticalOffset.Value, scrollViewer.ScrollableHeight);
            scrollViewer.ChangeView(null, targetOffset, null, disableAnimation: true);
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match)
                    return match;

                var descendant = FindVisualChild<T>(child);
                if (descendant != null)
                    return descendant;
            }

            return null;
        }

        private async void UnreadOnlyToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _suppressUnreadToggle || _selectedFolder == null) return;
            await LoadMessagesAsync();
        }

        private void SetUnreadOnlyWithoutReload(bool isOn)
        {
            if (UnreadOnlyToggle.IsOn == isOn) return;

            _suppressUnreadToggle = true;
            try
            {
                UnreadOnlyToggle.IsOn = isOn;
            }
            finally
            {
                _suppressUnreadToggle = false;
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoadingMessages || _refreshAccountsTask != null) return;

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
            if (_mailService == null) return;

            var account = ResolveSelectedAccountForRemoval();
            if (account == null) return;
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = GetResourceStringOrDefault("TextDeleteMailAccount", "Delete Email Account"),
                Content = string.Format(GetResourceStringOrDefault("TextDeleteMailAccountContent", "Remove {0} - {1} from Task Flyout? This will not delete emails on the server."), account.ProviderName, account.Subtitle),
                PrimaryButtonText = GetResourceStringOrDefault("CalendarDialog.SecondaryButtonText", "Delete"),
                CloseButtonText = GetResourceStringOrDefault("CalendarDialog.CloseButtonText", "Cancel"),
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            if (!_mailService.RemoveAccount(account.Id))
                return;

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

            ComposeTitleText.Text = replyTo == null ? (_loader.GetStringOrDefault("TextCompose") ?? "Compose") : (_loader.GetStringOrDefault("TextReply") ?? "Reply");
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
            ComposeStatusText.Text = accounts.Count == 0 ? (_loader.GetStringOrDefault("TextAddMailAccountFirst") ?? "Please add an email account first.") : "";

            ComposePanel.Visibility = Visibility.Visible;
            AddAccountPanel.Visibility = Visibility.Collapsed;
            DetailPanel.Visibility = Visibility.Collapsed;
            EmptyDetailPanel.Visibility = Visibility.Collapsed;
        }

        private async void AddOutlookButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mailService == null) return;

            SetAddButtonsEnabled(false);
            AddStatusText.Text = _loader.GetStringOrDefault("TextOpeningMSAuth") ?? "Opening Microsoft authorization...";

            try
            {
                var account = await _mailService.AddOutlookAccountAsync();
                AddStatusText.Text = string.Format(_loader.GetStringOrDefault("TextAccountAdded") ?? "Added {0}", account.DisplayTitle);
                await RefreshAccountsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Add Outlook account failed: {ex.Message}");
                AddStatusText.Text = _loader.GetStringOrDefault("TextAddOutlookFailed") ?? "Failed to add Outlook account. Please check authorization or network.";
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
            AddStatusText.Text = _loader.GetStringOrDefault("TextOpeningGoogleAuth") ?? "Opening Google authorization...";

            try
            {
                var account = await _mailService.AddGoogleAccountAsync();
                AddStatusText.Text = string.Format(_loader.GetStringOrDefault("TextAccountAdded") ?? "Added {0}", account.DisplayTitle);
                await RefreshAccountsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Add Google account failed: {ex.Message}");
                AddStatusText.Text = _loader.GetStringOrDefault("TextAddGmailFailed") ?? "Failed to add Gmail account. Please check authorization or network.";
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
            AddStatusText.Text = _loader.GetStringOrDefault("TextImapSetupHint") ?? "Please enter IMAP server info. Gmail/Outlook OAuth is recommended.";
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
                AddStatusText.Text = _loader.GetStringOrDefault("TextEnterEmail") ?? "Please enter email address.";
                return;
            }
            if (string.IsNullOrWhiteSpace(host))
            {
                AddStatusText.Text = _loader.GetStringOrDefault("TextEnterImapServer") ?? "Please enter IMAP server.";
                return;
            }
            if (string.IsNullOrWhiteSpace(password))
            {
                AddStatusText.Text = _loader.GetStringOrDefault("TextEnterPassword") ?? "Please enter password or app-specific password.";
                return;
            }
            if (string.IsNullOrWhiteSpace(smtpHost))
            {
                AddStatusText.Text = _loader.GetStringOrDefault("TextEnterSmtpServer") ?? "Please enter SMTP server. IMAP accounts need it to send mail.";
                return;
            }

            SetAddButtonsEnabled(false);
            AddStatusText.Text = _loader.GetStringOrDefault("TextConnectingImap") ?? "Connecting to IMAP server...";

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

                AddStatusText.Text = string.Format(_loader.GetStringOrDefault("TextAccountAdded") ?? "Added {0}", account.DisplayTitle);
                await RefreshAccountsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Add IMAP account failed: {ex.Message}");
                AddStatusText.Text = _loader.GetStringOrDefault("TextImapConnectFailed") ?? "Failed to connect to IMAP server. Please check settings and credentials.";
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
                if (!_isLoadingMessages && !_suppressSelectionClear)
                    ClearDetail();
                return;
            }

            _selectedItem = item;
            _bodyLoadCts?.Cancel();
            var bodyCts = CancellationTokenSource.CreateLinkedTokenSource(_pageRequestCts.Token);
            _bodyLoadCts = bodyCts;
            var account = _selectedAccount;
            _showRemoteImagesForCurrentMessage = false; // reset the per-message image override
            AddAccountPanel.Visibility = Visibility.Collapsed;
            ComposePanel.Visibility = Visibility.Collapsed;
            EmptyDetailPanel.Visibility = Visibility.Collapsed;
            DetailPanel.Visibility = Visibility.Visible;
            DetailSubject.Text = item.Subject;
            DetailSender.Text = item.Sender;
            DetailTime.Text = item.RawReceivedTime?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? item.ReceivedTime;
            var markAsReadTask = MarkSelectedMailReadOptimistically(item);
            UpdateTrustButton(item);

            if (string.IsNullOrWhiteSpace(item.BodyText) && string.IsNullOrWhiteSpace(item.HtmlBody)
                && _mailService != null && account != null)
            {
                try { await _mailService.FetchMessageBodyAsync(account, item, bodyCts.Token); }
                catch (OperationCanceledException) when (bodyCts.IsCancellationRequested)
                {
                    CompleteBodyLoad(bodyCts);
                    return;
                }
                catch { }
            }

            if (bodyCts.IsCancellationRequested || !ReferenceEquals(item, _selectedItem))
            {
                CompleteBodyLoad(bodyCts);
                return;
            }

            await RenderMailBodyAsync(item);
            ReplyButton.IsEnabled = _selectedAccount != null;
            OpenInBrowserButton.IsEnabled = !string.IsNullOrWhiteSpace(item.WebLink);

            if (markAsReadTask != null)
                _ = CompleteMarkAsReadAsync(markAsReadTask);

            CompleteBodyLoad(bodyCts);
        }

        private void CompleteBodyLoad(CancellationTokenSource bodyCts)
        {
            if (ReferenceEquals(_bodyLoadCts, bodyCts))
                _bodyLoadCts = null;
            bodyCts.Dispose();
        }

        private Task? MarkSelectedMailReadOptimistically(MailItem item)
        {
            if (_mailService == null || _selectedAccount == null || item.IsRead || !_mailService.AutoMarkMailAsRead)
                return null;

            _mailService.MarkCachedRead(item);
            var remoteSyncTask = _mailService.MarkAsReadAsync(_selectedAccount, item, forceRemoteSync: true);

            if (UnreadOnlyToggle.IsOn)
            {
                _suppressSelectionClear = true;
                try
                {
                    _items.Remove(item);
                }
                finally
                {
                    _suppressSelectionClear = false;
                }

                SetMessageListStatus($"{_selectedAccount.DisplayTitle} · {string.Format(_loader.GetStringOrDefault("TextNMailItems") ?? "{0} messages", _items.Count)}");
            }

            return remoteSyncTask;
        }

        private async Task CompleteMarkAsReadAsync(Task remoteSyncTask)
        {
            try
            {
                await remoteSyncTask;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Mark as read failed: {ex.Message}");
                SetMessageListStatus(_loader.GetStringOrDefault("TextReadSyncFailed") ?? "Failed to sync read status.", isError: true);
            }
        }

        private void SetMessageListStatus(string message, bool isError = false)
        {
            if (MessageListSubtitle == null) return;
            MessageListSubtitle.Text = StatusMessageFormatter.Format(message, _lastMessageLoadSucceededAt, isError);
        }

        private void ClearDetail()
        {
            _bodyLoadCts?.Cancel();
            _selectedItem = null;
            DetailPanel.Visibility = Visibility.Collapsed;
            DetailHtmlViewHost.Visibility = Visibility.Collapsed;
            DetailTextScrollViewer.Visibility = Visibility.Visible;
            DetailPreview.Text = "";
            TrustSenderButton.IsEnabled = false;
            if (AddAccountPanel.Visibility != Visibility.Visible && ComposePanel.Visibility != Visibility.Visible)
                EmptyDetailPanel.Visibility = Visibility.Visible;
        }

        private void ReleaseMessageBodies()
        {
            foreach (var item in _items)
            {
                item.BodyText = "";
                item.HtmlBody = "";
            }

            if (_selectedItem != null)
            {
                _selectedItem.BodyText = "";
                _selectedItem.HtmlBody = "";
            }

            _mailService?.ClearVolatileMessageBodies();
        }

        private bool _webView2Configured;
        private bool _isInternalMailHtmlNavigation;
        private WebView2? _detailHtmlView;
        private CancellationTokenSource? _mailResourceCts;

        // Per-message "show images this once" override for the remote-image privacy block.
        private bool _showRemoteImagesForCurrentMessage;

        // When on (default), remote images/tracking pixels are blocked for senders the user
        // hasn't trusted; a banner offers a per-message "show images" without trusting them.
        private static bool BlockRemoteImagesByDefault =>
            Windows.Storage.ApplicationData.Current.LocalSettings.Values["BlockRemoteImagesByDefault"] as bool? ?? true;

        private async Task RenderMailBodyAsync(MailItem item)
        {
            if (!ReferenceEquals(item, _selectedItem)) return;
            RemoteImageBanner.Visibility = Visibility.Collapsed;

            if (!string.IsNullOrWhiteSpace(item.HtmlBody))
            {
                var senderTrusted = _mailTrustStore.IsTrusted(item);
                // Remote images load only for trusted senders, when the user shows them for this
                // message, or when the default-block setting is off — otherwise they're stripped.
                bool allowRemote = senderTrusted || _showRemoteImagesForCurrentMessage || !BlockRemoteImagesByDefault;
                var html = allowRemote
                    ? MailHtmlSanitizer.SanitizeTrusted(item.HtmlBody)
                    : MailHtmlSanitizer.SanitizeUntrusted(item.HtmlBody);

                bool remoteBlocked = !allowRemote && MailHtmlSanitizer.HasRemoteResources(item.HtmlBody);

                if (allowRemote || HasRenderableHtml(html))
                {
                    var htmlDocument = BuildMailHtmlDocument(html, IsDarkThemeActive(), alreadySanitized: true);
                    try
                    {
                        var itemId = item.Id;
                        DetailPreview.Text = "";
                        DetailTextScrollViewer.Visibility = Visibility.Collapsed;
                        DetailHtmlViewHost.Visibility = Visibility.Visible;
                        RemoteImageBanner.Visibility = remoteBlocked ? Visibility.Visible : Visibility.Collapsed;
                        var htmlView = EnsureDetailHtmlView();
                        await htmlView.EnsureCoreWebView2Async();
                        if (!_webView2Configured)
                        {
                            var coreWebView = htmlView.CoreWebView2;
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
                            coreWebView.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                            coreWebView.NavigationStarting += MailHtml_NavigationStarting;
                            coreWebView.NavigationCompleted += MailHtml_NavigationCompleted;
                            coreWebView.NewWindowRequested += MailHtml_NewWindowRequested;
                            coreWebView.WebResourceRequested += MailHtml_WebResourceRequested;
                        }
                        if (_selectedItem?.Id != itemId) return;
                        CancelMailResourceRequests();
                        _mailResourceCts = new CancellationTokenSource();
                        _isInternalMailHtmlNavigation = true;
                        htmlView.NavigateToString(htmlDocument);
                        return;
                    }
                    catch (Exception ex)
                    {
                        _isInternalMailHtmlNavigation = false;
                        System.Diagnostics.Debug.WriteLine($"HTML mail render failed: {ex.Message}");
                    }
                }
            }

            if (ReferenceEquals(item, _selectedItem))
                ShowPlainTextMailBody(item);
        }

        private async void ShowRemoteImagesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null) return;
            _showRemoteImagesForCurrentMessage = true;
            RemoteImageBanner.Visibility = Visibility.Collapsed;
            await RenderMailBodyAsync(_selectedItem);
        }

        private void MailHtml_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
        {
            if (_isInternalMailHtmlNavigation)
                return;

            if (!args.IsRedirected && args.Uri != "about:blank")
            {
                args.Cancel = true;
                OpenSafeExternalUri(args.Uri);
            }
        }

        private void MailHtml_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            _isInternalMailHtmlNavigation = false;
        }

        private void MailHtml_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs args)
        {
            args.Handled = true;
            OpenSafeExternalUri(args.Uri);
        }

        private async void MailHtml_WebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
        {
            if (sender is not CoreWebView2 coreWebView) return;
            var uri = args.Request?.Uri;

            if (WebView2RuntimeService.IsAllowedMailNonRemoteResource(uri))
                return;

            if (args.ResourceContext == CoreWebView2WebResourceContext.Image &&
                WebView2RuntimeService.ShouldProxyMailRemoteImage(uri))
            {
                var deferral = args.GetDeferral();
                try
                {
                    var cts = _mailResourceCts;
                    if (cts == null || cts.IsCancellationRequested)
                    {
                        args.Response = BlockedMailResponse(coreWebView);
                        return;
                    }

                    var fetched = await new RssService().FetchRemoteImageSafelyAsync(uri!, cts.Token);
                    if (fetched != null)
                    {
                        var stream = new InMemoryRandomAccessStream();
                        await stream.WriteAsync(fetched.Value.Bytes.AsBuffer());
                        stream.Seek(0);
                        args.Response = coreWebView.Environment.CreateWebResourceResponse(
                            stream, 200, "OK", $"Content-Type: {fetched.Value.ContentType}");
                    }
                    else
                    {
                        args.Response = BlockedMailResponse(coreWebView);
                    }
                }
                catch
                {
                    args.Response = BlockedMailResponse(coreWebView);
                }
                finally
                {
                    deferral.Complete();
                }
                return;
            }

            args.Response = BlockedMailResponse(coreWebView);
        }

        private static CoreWebView2WebResourceResponse BlockedMailResponse(CoreWebView2 coreWebView)
            => coreWebView.Environment.CreateWebResourceResponse(
                new InMemoryRandomAccessStream(), 403, "Blocked", "Content-Type: text/plain");

        private WebView2 EnsureDetailHtmlView()
        {
            if (_detailHtmlView != null)
                return _detailHtmlView;

            WebView2RuntimeService.ConfigureSharedRuntime();
            _detailHtmlView = new WebView2
            {
                Visibility = Visibility.Visible,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            DetailHtmlViewHost.Children.Clear();
            DetailHtmlViewHost.Children.Add(_detailHtmlView);
            _webView2Configured = false;
            return _detailHtmlView;
        }

        private void ReleaseMailWebView()
        {
            CancelMailResourceRequests();
            var webView = _detailHtmlView;
            if (webView == null) return;

            try
            {
                if (webView.CoreWebView2 != null)
                {
                    webView.CoreWebView2.NavigationStarting -= MailHtml_NavigationStarting;
                    webView.CoreWebView2.NavigationCompleted -= MailHtml_NavigationCompleted;
                    webView.CoreWebView2.NewWindowRequested -= MailHtml_NewWindowRequested;
                    webView.CoreWebView2.WebResourceRequested -= MailHtml_WebResourceRequested;
                    webView.NavigateToString("<html></html>");
                }

                webView.Close();
            }
            catch { }

            DetailHtmlViewHost.Children.Clear();
            _detailHtmlView = null;
            _webView2Configured = false;
        }

        private void CancelMailResourceRequests()
        {
            try { _mailResourceCts?.Cancel(); }
            catch { }
            _mailResourceCts?.Dispose();
            _mailResourceCts = null;
        }

        private void ShowPlainTextMailBody(MailItem item)
        {
            DetailHtmlViewHost.Visibility = Visibility.Collapsed;
            DetailTextScrollViewer.Visibility = Visibility.Visible;
            var fallbackText = item.BodyText;
            if (string.IsNullOrWhiteSpace(fallbackText) && !string.IsNullOrWhiteSpace(item.HtmlBody))
                fallbackText = BuildPlainTextFallback(item.HtmlBody);

            DetailPreview.Text = string.IsNullOrWhiteSpace(fallbackText)
                ? string.IsNullOrWhiteSpace(item.Preview) ? (_loader.GetStringOrDefault("TextNoBody") ?? "No body available.") : item.Preview
                : fallbackText;
        }

        private void UpdateTrustButton(MailItem item)
        {
            var source = _mailTrustStore.GetDisplaySource(item);
            var senderTrusted = _mailTrustStore.IsTrusted(item);
            var domainTrusted = _mailTrustStore.IsDomainTrusted(item);
            var domain = _mailTrustStore.GetDomain(item);

            TrustSenderButton.IsEnabled = source != (_loader.GetStringOrDefault("TextUnknownSource") ?? "Unknown source");

            if (senderTrusted && !domainTrusted)
            {
                TrustSenderButtonText.Text = _loader.GetStringOrDefault("TextUntrustSource") ?? "Untrust source";
                ToolTipService.SetToolTip(TrustSenderButton, string.Format(_loader.GetStringOrDefault("TextTrusted") ?? "Trusted: {0}", source));
            }
            else if (domainTrusted)
            {
                TrustSenderButtonText.Text = string.Format(_loader.GetStringOrDefault("TextUntrustDomain") ?? "Untrust @{0}", domain);
                ToolTipService.SetToolTip(TrustSenderButton, string.Format(_loader.GetStringOrDefault("TextDomainTrusted") ?? "Domain trusted: @{0}", domain));
            }
            else
            {
                TrustSenderButtonText.Text = _loader.GetStringOrDefault("TextTrustSender") ?? "Trust this sender";
                ToolTipService.SetToolTip(TrustSenderButton, string.Format(_loader.GetStringOrDefault("TextTrustTooltip") ?? "Trusting will allow loading remote content from: {0}", source));
            }
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
            var safeHtml = alreadySanitized ? html : MailHtmlSanitizer.SanitizeUntrusted(html);
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

            if (RxHtmlTag.IsMatch(safeHtml))
            {
                if (RxHeadClose.IsMatch(safeHtml))
                    return RxHeadClose.Replace(safeHtml, baseStyle + "</head>");

                return RxHtmlOpenTag.Replace(safeHtml, match => match.Value + baseStyle);
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
            => _ = SafeUriLauncher.TryLaunchExternalHttpUriAsync(uriText);

        private static bool HasVisibleHtmlContent(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return false;

            var withoutNonContent = RxNonContentTags.Replace(html, " ");

            var text = RxHtmlTagsOnly.Replace(withoutNonContent, " ");
            text = WebUtility.HtmlDecode(text).Trim();
            if (text.Length > 0)
                return true;

            if (RxLocalImgSrc.IsMatch(withoutNonContent))
                return true;

            return false;
        }

        private static bool HasRenderableHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return false;
            if (HasVisibleHtmlContent(html)) return true;
            return RxHtmlContentTags.IsMatch(html);
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
            value = RxPlainTextHeadStyle.Replace(value, " ");
            value = RxPlainTextMetaLink.Replace(value, " ");
            value = RxHtmlTagsOnly.Replace(value, " ");
            value = RemoveCssNoise(value);
            value = WebUtility.HtmlDecode(value);
            return RxMultiSpace.Replace(value, " ").Trim();
        }

        private static bool IsCssNoiseLine(string line)
        {
            if (line == "{" || line == "}") return true;
            if (line.StartsWith("@media", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("@font-face", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("@-moz", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("@supports", StringComparison.OrdinalIgnoreCase))
                return true;
            if (RxCssSelector.IsMatch(line)) return true;
            if (RxCssProperty.IsMatch(line)) return true;
            if (RxCssRuleStart.IsMatch(line)) return true;
            return false;
        }

        private async void CancelComposeButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(ComposeToBox.Text) ||
                !string.IsNullOrWhiteSpace(ComposeSubjectBox.Text) ||
                !string.IsNullOrWhiteSpace(ComposeBodyBox.Text))
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    Title = _loader.GetStringOrDefault("TextDiscardDraftTitle") ?? "Discard draft?",
                    Content = _loader.GetStringOrDefault("TextDiscardDraftContent") ?? "Your unsent message will be lost.",
                    PrimaryButtonText = _loader.GetStringOrDefault("TextDiscard") ?? "Discard",
                    CloseButtonText = _loader.GetStringOrDefault("TextContinueEditing") ?? "Keep editing",
                    DefaultButton = ContentDialogButton.Close
                };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                    return;
            }

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
                ComposeStatusText.Text = _loader.GetStringOrDefault("TextSelectSender") ?? "Please select a sender.";
                return;
            }

            var account = _mailService.GetAccounts().FirstOrDefault(a => a.Id == selectedFrom.Tag?.ToString());
            if (account == null)
            {
                ComposeStatusText.Text = _loader.GetStringOrDefault("TextSenderNotFound") ?? "Sender account not found.";
                return;
            }

            if (string.IsNullOrWhiteSpace(ComposeToBox.Text))
            {
                ComposeStatusText.Text = _loader.GetStringOrDefault("TextEnterRecipient") ?? "Please enter a recipient.";
                return;
            }

            SendComposeButton.IsEnabled = false;
            ComposeStatusText.Text = _loader.GetStringOrDefault("TextSending") ?? "Sending...";

            try
            {
                await _mailService.SendMailAsync(account, ComposeToBox.Text, ComposeSubjectBox.Text, ComposeBodyBox.Text);
                ComposeStatusText.Text = _loader.GetStringOrDefault("TextMailSent") ?? "Email sent.";
                ComposePanel.Visibility = Visibility.Collapsed;
                ClearDetail();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Send mail failed: {ex.Message}");
                ComposeStatusText.Text = _loader.GetStringOrDefault("TextSendFailed") ?? "Failed to send email. Please check network and account settings.";
            }
            finally
            {
                SendComposeButton.IsEnabled = true;
            }
        }

        private async void OpenInBrowserButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null || string.IsNullOrWhiteSpace(_selectedItem.WebLink)) return;
            await SafeUriLauncher.TryLaunchExternalHttpUriAsync(_selectedItem.WebLink);
        }

        private async void TrustSenderButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null) return;

            var flyout = new MenuFlyout();
            var senderTrusted = _mailTrustStore.IsTrusted(_selectedItem);
            var domainTrusted = _mailTrustStore.IsDomainTrusted(_selectedItem);
            var domain = _mailTrustStore.GetDomain(_selectedItem);

            if (!senderTrusted)
            {
                flyout.Items.Add(new MenuFlyoutItem
                {
                    Text = _loader.GetStringOrDefault("TextTrustSender") ?? "Trust this sender",
                });
                ((MenuFlyoutItem)flyout.Items[^1]).Click += async (_, _) =>
                {
                    _mailTrustStore.Trust(_selectedItem);
                    UpdateTrustButton(_selectedItem);
                    await RenderMailBodyAsync(_selectedItem);
                };
            }
            else
            {
                flyout.Items.Add(new MenuFlyoutItem
                {
                    Text = _loader.GetStringOrDefault("TextUntrustSource") ?? "Untrust source",
                });
                ((MenuFlyoutItem)flyout.Items[^1]).Click += async (_, _) =>
                {
                    _mailTrustStore.Untrust(_selectedItem);
                    UpdateTrustButton(_selectedItem);
                    await RenderMailBodyAsync(_selectedItem);
                };
            }

            if (domain != null && !domainTrusted)
            {
                flyout.Items.Add(new MenuFlyoutItem
                {
                    Text = string.Format(_loader.GetStringOrDefault("TextTrustDomain") ?? "Trust all @{0}", domain),
                });
                ((MenuFlyoutItem)flyout.Items[^1]).Click += async (_, _) =>
                {
                    _mailTrustStore.TrustDomain(_selectedItem);
                    UpdateTrustButton(_selectedItem);
                    await RenderMailBodyAsync(_selectedItem);
                };
            }
            else if (domain != null && domainTrusted)
            {
                flyout.Items.Add(new MenuFlyoutItem
                {
                    Text = string.Format(_loader.GetStringOrDefault("TextUntrustDomain") ?? "Untrust @{0}", domain),
                });
                ((MenuFlyoutItem)flyout.Items[^1]).Click += async (_, _) =>
                {
                    _mailTrustStore.UntrustDomain(_selectedItem);
                    UpdateTrustButton(_selectedItem);
                    await RenderMailBodyAsync(_selectedItem);
                };
            }

            flyout.ShowAt(sender as FrameworkElement);
        }

        private static string GetReplyRecipient(MailItem item)
            => string.IsNullOrWhiteSpace(item.SenderAddress) ? item.Sender : item.SenderAddress;

        private static string CreateReplySubject(string subject)
        {
            if (string.IsNullOrWhiteSpace(subject)) return "Re:";
            return subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ? subject : $"Re: {subject}";
        }

        private string CreateReplyBody(MailItem item)
        {
            var received = item.RawReceivedTime?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? item.ReceivedTime;
            var original = string.IsNullOrWhiteSpace(item.BodyText) ? item.Preview : item.BodyText;
            var originalMail = _loader.GetStringOrDefault("TextOriginalMail") ?? "---- Original Message ----";
            var fromLabel = _loader.GetStringOrDefault("TextOriginalSender") ?? "From";
            var dateLabel = _loader.GetStringOrDefault("TextOriginalTime") ?? "Date";
            var subjectLabel = _loader.GetStringOrDefault("TextOriginalSubject") ?? "Subject";
            return $"\r\n\r\n{originalMail}\r\n{fromLabel}: {item.Sender}\r\n{dateLabel}: {received}\r\n{subjectLabel}: {item.Subject}\r\n\r\n{original}";
        }
    }
}
