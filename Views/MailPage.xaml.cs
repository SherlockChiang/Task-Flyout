using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
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
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace Task_Flyout.Views
{
    public sealed partial class MailPage : Page
    {
        private readonly ObservableCollection<MailItem> _items = new();
        private readonly ObservableCollection<MailItem> _displayedItems = new();
        public ObservableCollection<MailAttachmentData> ComposeAttachments { get; } = new();
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
        private ResponsiveLayoutMode _layoutMode = ResponsiveLayoutMode.Wide;
        private MailPane _narrowMailPane = MailPane.Accounts;
        private bool _showMediumAccounts;
        private int _messageLoadVersion;
        private DateTimeOffset? _lastMessageLoadSucceededAt;
        private Task? _refreshAccountsTask;
        private CancellationTokenSource _pageRequestCts = new();
        private CancellationTokenSource? _messageLoadCts;
        private CancellationTokenSource? _bodyLoadCts;
        private CancellationTokenSource? _accountAuthCts;
        private CancellationTokenSource? _imapSetupCts;
        private CancellationTokenSource? _searchCts;
        private string _searchText = "";
        private bool _suppressSearchRefresh;
        private bool _isSendingMail;
        private bool _suppressDraftChanges;
        private Task<bool>? _draftRecoveryTask;
        internal bool IsOpeningFromNotification { get; set; }
        private ComposeDraftCoordinator? Drafts => (App.Current as App)?.ComposeDrafts;

        private enum MailPane { Accounts, Messages, Detail }

        public MailPage()
        {
            this.Language = Windows.Globalization.ApplicationLanguages.Languages[0];
            InitializeComponent();
            _items.CollectionChanged += (_, _) => ApplyMailSearch();
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
            _accountAuthCts?.Cancel();
            _imapSetupCts?.Cancel();
            _searchCts?.Cancel();
            ScheduleComposeDraft();
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
            _displayedItems.Clear();
            ComposeAttachments.Clear();
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
            MailListView.ItemsSource = _displayedItems;
            _isInitializing = false;
            await RefreshAccountsAsync(autoSelect: !IsOpeningFromNotification);
            if (!IsOpeningFromNotification)
                await OfferDraftRecoveryAsync();
            if (IsOpeningFromNotification && _layoutMode == ResponsiveLayoutMode.Narrow)
            {
                _narrowMailPane = MailPane.Detail;
                ApplyResponsiveLayout();
            }
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

            _searchCts?.Cancel();
            _searchText = "";
            if (MailSearchBox != null) MailSearchBox.Text = "";
            ApplyMailSearch();

            _mailService ??= (App.Current as App)?.MailService;
            MailListView.ItemsSource ??= _displayedItems;
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
                ShowMailPane(MailPane.Messages);
                return;
            }

            if (!_folderNodes.TryGetValue(node, out var selection)) return;

            _selectedAccount = selection.Account;
            _selectedFolder = selection.Folder;
            _selectedAccountForRemoval = selection.Account;
            RemoveMailButton.IsEnabled = true;
            await LoadMessagesAsync();
            ShowMailPane(MailPane.Messages);
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
                var window = await _mailService.FetchMessagesAsync(
                    account,
                    folder,
                    UnreadOnlyToggle.IsOn,
                    pageSize: _mailService.PageSize,
                    forceRefresh: forceRefresh,
                    loadMore: loadMore,
                    cancellationToken: requestCts.Token);
                if (loadVersion != _messageLoadVersion || !ReferenceEquals(account, _selectedAccount) || !ReferenceEquals(folder, _selectedFolder))
                    return;
                var messages = window.Items;
                var previousSelectedId = !string.IsNullOrWhiteSpace(preferredMessageId) ? preferredMessageId : _selectedItem?.Id;
                _suppressSearchRefresh = true;
                if (loadMore)
                {
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
                _suppressSearchRefresh = false;
                _lastMessageLoadSucceededAt = DateTimeOffset.Now;
                SetMessageListStatus($"{account.DisplayTitle} · {string.Format(_loader.GetStringOrDefault("TextNMailItems") ?? "{0} messages", _items.Count)}");
                ApplyMailSearch();

                LoadMoreButton.Visibility = window.HasMore ? Visibility.Visible : Visibility.Collapsed;

                var itemToSelect = !string.IsNullOrWhiteSpace(previousSelectedId)
                    ? _displayedItems.FirstOrDefault(item => item.Id == previousSelectedId)
                    : null;
                if (itemToSelect != null)
                    MailListView.SelectedItem = itemToSelect;
                else if (selectFirstWhenNoMatch)
                    MailListView.SelectedItem = _displayedItems.Count > 0 ? _displayedItems[0] : null;
                else
                    MailListView.SelectedItem = null;

                if (_items.Count == 0)
                    ClearDetail();
            }
            catch (OperationCanceledException) when (requestCts.IsCancellationRequested) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Load messages failed: {ex.Message}");
                if (loadMore)
                    LoadMoreButton.Visibility = Visibility.Collapsed;
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

        private async void MailSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            var cts = new CancellationTokenSource();
            _searchCts = cts;
            try
            {
                await Task.Delay(250, cts.Token);
                _searchText = sender.Text.Trim();
                ApplyMailSearch();
            }
            catch (OperationCanceledException) { }
        }

        private void ApplyMailSearch()
        {
            if (_suppressSearchRefresh) return;
            _displayedItems.Clear();
            foreach (var item in _items.Where(item => LocalSearchMatcher.Matches(
                         _searchText,
                         item.Subject,
                         item.Sender,
                         item.SenderAddress,
                         item.Recipient,
                         item.Preview)))
                _displayedItems.Add(item);

            if (!string.IsNullOrWhiteSpace(_searchText) && _selectedAccount != null)
                SetMessageListStatus($"{_selectedAccount.DisplayTitle} · {string.Format(GetResourceStringOrDefault("TextSearchMatches", "{0} matches of {1} loaded"), _displayedItems.Count, _items.Count)}");
        }

        private async void LoadMoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoadingMessages || _mailService == null) return;

            int previousCount = _items.Count;
            var listScrollViewer = FindVisualChild<ScrollViewer>(MailListView);
            double? previousVerticalOffset = listScrollViewer?.VerticalOffset;

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
                Content = ProviderAuthorizationLifecycle.HasSharedAuthorization(account.ProviderName)
                    ? string.Format(GetResourceStringOrDefault("TextMailProviderRemovalContent", "Remove {0} - {1} from Mail only, or disconnect the provider completely from Mail, Calendar, and Tasks? Embedded browser data is cleared either way."), account.ProviderName, account.Subtitle)
                    : string.Format(GetResourceStringOrDefault("TextDeleteMailAccountContent", "Remove {0} - {1} from Task Flyout? This will not delete emails on the server. Mail and RSS share an embedded browser profile, so its site data, history, and disk cache will also be cleared for both readers."), account.ProviderName, account.Subtitle),
                PrimaryButtonText = ProviderAuthorizationLifecycle.HasSharedAuthorization(account.ProviderName)
                    ? GetResourceStringOrDefault("TextRemoveMailOnly", "Remove Mail only")
                    : GetResourceStringOrDefault("CalendarDialog.SecondaryButtonText", "Delete"),
                SecondaryButtonText = ProviderAuthorizationLifecycle.HasSharedAuthorization(account.ProviderName)
                    ? GetResourceStringOrDefault("TextDisconnectProvider", "Disconnect completely")
                    : "",
                CloseButtonText = GetResourceStringOrDefault("CalendarDialog.CloseButtonText", "Cancel"),
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result is not (ContentDialogResult.Primary or ContentDialogResult.Secondary)) return;

            if (result == ContentDialogResult.Secondary && App.Current is App app)
            {
                try
                {
                    await app.DisconnectProviderCompletelyAsync(account.ProviderName);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Complete provider disconnect failed: {ex.Message}");
                    SetMessageListStatus(GetResourceStringOrDefault("TextDisconnectProviderFailed", "The account change did not finish. Existing local data may remain; try again."), isError: true);
                    return;
                }
            }
            else
            {
                try
                {
                    if (Drafts != null)
                        await Drafts.DiscardForAccountAsync(account.Id);
                }
                catch
                {
                    SetMessageListStatus(_loader.GetStringOrDefault("TextDiscardDraftFailed") ?? "The protected draft could not be deleted. Try again.", isError: true);
                    return;
                }
                if (!_mailService.RemoveAccount(account.Id))
                    return;
            }

            ReleaseMailWebView();
            await WebView2RuntimeService.ClearSensitiveBrowsingDataAsync();
            _items.Clear();
            _displayedItems.Clear();
            _selectedAccount = null;
            _selectedFolder = null;
            _selectedItem = null;
            _selectedAccountForRemoval = null;
            RemoveMailButton.IsEnabled = false;
            ClearDetail();
            await RefreshAccountsAsync();
        }

        public async Task RefreshAfterProviderDisconnectAsync()
        {
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
            ShowMailPane(MailPane.Detail);
            AddAccountPanel.Visibility = Visibility.Visible;
            ComposePanel.Visibility = Visibility.Collapsed;
            DetailPanel.Visibility = Visibility.Collapsed;
            EmptyDetailPanel.Visibility = Visibility.Collapsed;
            AddStatusText.Text = "";
            ImapSettingsPanel.Visibility = Visibility.Collapsed;
        }

        private async void ComposeButton_Click(object sender, RoutedEventArgs e)
        {
            ShowMailPane(MailPane.Detail);
            await StartComposeAsync();
        }

        private void ShowMailPane(MailPane pane)
        {
            if (_layoutMode == ResponsiveLayoutMode.Wide) return;
            if (_layoutMode == ResponsiveLayoutMode.Narrow)
                _narrowMailPane = pane;
            else
                _showMediumAccounts = false;
            ApplyResponsiveLayout();
        }

        private void LayoutRoot_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ComposeBodyBox.MinHeight = e.NewSize.Height < 600 ? 120 : 220;
            var mode = ResponsiveLayoutPolicy.GetMode(e.NewSize.Width);
            if (_layoutMode == mode) return;
            _layoutMode = mode;
            _showMediumAccounts = false;
            if (mode == ResponsiveLayoutMode.Narrow)
                _narrowMailPane = _selectedItem != null ? MailPane.Detail
                    : _selectedFolder != null ? MailPane.Messages : MailPane.Accounts;
            ApplyResponsiveLayout();
        }

        private void MailBackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_layoutMode != ResponsiveLayoutMode.Narrow) return;
            _narrowMailPane = _narrowMailPane == MailPane.Detail ? MailPane.Messages : MailPane.Accounts;
            ApplyResponsiveLayout();
        }

        private void MailAccountsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_layoutMode != ResponsiveLayoutMode.Medium) return;
            _showMediumAccounts = !_showMediumAccounts;
            ApplyResponsiveLayout();
        }

        private void ApplyResponsiveLayout()
        {
            bool wide = _layoutMode == ResponsiveLayoutMode.Wide;
            bool medium = _layoutMode == ResponsiveLayoutMode.Medium;
            bool showAccounts = wide || medium && _showMediumAccounts
                || _layoutMode == ResponsiveLayoutMode.Narrow && _narrowMailPane == MailPane.Accounts;
            bool showMessages = wide || medium && !_showMediumAccounts
                || _layoutMode == ResponsiveLayoutMode.Narrow && _narrowMailPane == MailPane.Messages;
            bool showDetail = wide || medium && !_showMediumAccounts
                || _layoutMode == ResponsiveLayoutMode.Narrow && _narrowMailPane == MailPane.Detail;

            AccountColumn.MinWidth = wide ? 190 : 0;
            AccountColumn.Width = showAccounts ? (wide ? new GridLength(2, GridUnitType.Star) : new GridLength(1, GridUnitType.Star)) : new GridLength(0);
            MessageColumn.MinWidth = wide ? 280 : 0;
            MessageColumn.Width = showMessages ? new GridLength(medium ? 2 : 3, GridUnitType.Star) : new GridLength(0);
            DetailColumn.MinWidth = 0;
            DetailColumn.Width = showDetail ? new GridLength(medium ? 3 : 5, GridUnitType.Star) : new GridLength(0);
            MailAccountPane.Visibility = showAccounts ? Visibility.Visible : Visibility.Collapsed;
            MailMessagePane.Visibility = showMessages ? Visibility.Visible : Visibility.Collapsed;
            MailDetailPane.Visibility = showDetail ? Visibility.Visible : Visibility.Collapsed;
            MailBackButton.Visibility = _layoutMode == ResponsiveLayoutMode.Narrow && _narrowMailPane != MailPane.Accounts
                ? Visibility.Visible : Visibility.Collapsed;
            MailAccountsButton.Visibility = medium ? Visibility.Visible : Visibility.Collapsed;
            UnreadOnlyToggle.Visibility = _layoutMode == ResponsiveLayoutMode.Narrow ? Visibility.Collapsed : Visibility.Visible;
            ComposeButtonText.Visibility = _layoutMode == ResponsiveLayoutMode.Narrow ? Visibility.Collapsed : Visibility.Visible;
            RefreshButtonText.Visibility = _layoutMode == ResponsiveLayoutMode.Narrow ? Visibility.Collapsed : Visibility.Visible;
            LayoutRoot.Padding = _layoutMode == ResponsiveLayoutMode.Narrow ? new Thickness(12) : new Thickness(28);
            Grid.SetRow(MailHeaderCommands, _layoutMode == ResponsiveLayoutMode.Narrow ? 1 : 0);
            Grid.SetColumn(MailHeaderCommands, _layoutMode == ResponsiveLayoutMode.Narrow ? 0 : 1);
            Grid.SetColumnSpan(MailHeaderCommands, _layoutMode == ResponsiveLayoutMode.Narrow ? 2 : 1);
            MailHeaderCommands.HorizontalAlignment = _layoutMode == ResponsiveLayoutMode.Narrow
                ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        }

        private void ShowComposePanel(MailItem? replyTo = null)
        {
            if (_mailService == null) return;

            _suppressDraftChanges = true;
            ComposeAttachments.Clear();
            UpdateAttachmentSummary();
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
            SetComposeStatus(accounts.Count == 0 ? (_loader.GetStringOrDefault("TextAddMailAccountFirst") ?? "Please add an email account first.") : "");

            ComposePanel.Visibility = Visibility.Visible;
            AddAccountPanel.Visibility = Visibility.Collapsed;
            DetailPanel.Visibility = Visibility.Collapsed;
            EmptyDetailPanel.Visibility = Visibility.Collapsed;
            _suppressDraftChanges = false;
            ScheduleComposeDraft();
        }

        public async Task StartComposeAsync()
        {
            if (!IsLoaded)
                await WaitUntilLoadedAsync();
            _mailService ??= (App.Current as App)?.MailService;
            if (ComposePanel.Visibility == Visibility.Visible)
            {
                ComposeToBox.Focus(FocusState.Programmatic);
                return;
            }
            if (await OfferDraftRecoveryAsync()) return;
            ShowComposePanel();
        }

        private void ComposeField_Changed(object sender, RoutedEventArgs e)
            => ScheduleComposeDraft();

        private void ScheduleComposeDraft()
        {
            if (_suppressDraftChanges || ComposePanel?.Visibility != Visibility.Visible || Drafts == null) return;
            Drafts.Schedule(CaptureComposeDraft());
        }

        private ComposeDraft CaptureComposeDraft()
            => new(
                ComposeDraft.CurrentSchemaVersion,
                (ComposeFromBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "",
                ComposeToBox?.Text ?? "",
                ComposeSubjectBox?.Text ?? "",
                ComposeBodyBox?.Text ?? "",
                DateTimeOffset.UtcNow);

        private async Task<bool> OfferDraftRecoveryAsync()
        {
            if (_draftRecoveryTask != null) return await _draftRecoveryTask;
            var task = OfferDraftRecoveryCoreAsync();
            _draftRecoveryTask = task;
            try { return await task; }
            finally
            {
                if (ReferenceEquals(_draftRecoveryTask, task))
                    _draftRecoveryTask = null;
            }
        }

        private async Task<bool> OfferDraftRecoveryCoreAsync()
        {
            if (Drafts == null || XamlRoot == null) return false;
            var draft = await Drafts.LoadAsync();
            if (draft == null) return false;

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = _loader.GetStringOrDefault("TextRecoverDraftTitle") ?? "Recover unsent draft?",
                Content = string.Format(
                    _loader.GetStringOrDefault("TextRecoverDraftContent") ?? "A protected draft from {0} is available.",
                    draft.UpdatedAt.ToLocalTime().ToString("g")),
                PrimaryButtonText = _loader.GetStringOrDefault("TextRestore") ?? "Restore",
                SecondaryButtonText = _loader.GetStringOrDefault("TextDiscard") ?? "Discard",
                CloseButtonText = _loader.GetStringOrDefault("TextNotNow") ?? "Not now",
                DefaultButton = ContentDialogButton.Primary
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Secondary)
            {
                try { await Drafts.DiscardAsync(); }
                catch
                {
                    SetMessageListStatus(_loader.GetStringOrDefault("TextDiscardDraftFailed") ?? "The protected draft could not be deleted. Try again.", isError: true);
                    return true;
                }
                return false;
            }
            if (result != ContentDialogResult.Primary) return true;

            ShowMailPane(MailPane.Detail);
            ShowComposePanel();
            _suppressDraftChanges = true;
            ComposeFromBox.SelectedItem = ComposeFromBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag?.ToString() == draft.AccountId);
            ComposeToBox.Text = draft.Recipient;
            ComposeSubjectBox.Text = draft.Subject;
            ComposeBodyBox.Text = draft.Body;
            _suppressDraftChanges = false;
            ScheduleComposeDraft();
            ComposeToBox.Focus(FocusState.Programmatic);
            return true;
        }

        private async Task ClearComposeDraftAsync()
        {
            if (Drafts != null) await Drafts.DiscardAsync();
            _suppressDraftChanges = true;
            ComposeToBox.Text = "";
            ComposeSubjectBox.Text = "";
            ComposeBodyBox.Text = "";
            ComposeAttachments.Clear();
            UpdateAttachmentSummary();
            ComposeFromBox.IsEnabled = true;
            ComposeToBox.IsEnabled = true;
            ComposeSubjectBox.IsEnabled = true;
            ComposeBodyBox.IsEnabled = true;
            _suppressDraftChanges = false;
        }

        private async void AddAttachmentsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isSendingMail) return;
            try
            {
                var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
                picker.FileTypeFilter.Add("*");
                var window = App.MyMainWindow ?? throw new InvalidOperationException("The application window is unavailable.");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
                var files = await picker.PickMultipleFilesAsync();
                if (files.Count == 0) return;

                long currentTotal = ComposeAttachments.Sum(item => item.Size);
                foreach (var file in files)
                {
                    if (ComposeAttachments.Count >= MailAttachmentPolicy.MaximumCount)
                        throw new InvalidOperationException(_loader.GetStringOrDefault("TextAttachmentCountLimit") ?? "You can attach up to 10 files.");
                    var properties = await file.GetBasicPropertiesAsync();
                    if (properties.Size == 0 || properties.Size > MailAttachmentPolicy.MaximumFileBytes)
                        throw new InvalidOperationException(_loader.GetStringOrDefault("TextAttachmentFileLimit") ?? "Each attachment must be between 1 byte and 3 MB.");
                    if (currentTotal + (long)properties.Size > MailAttachmentPolicy.MaximumTotalBytes)
                        throw new InvalidOperationException(_loader.GetStringOrDefault("TextAttachmentTotalLimit") ?? "Attachments may total up to 10 MB.");

                    var safeName = MailAttachmentPolicy.NormalizeFileName(file.Name);
                    if (ComposeAttachments.Any(item => item.FileName.Equals(safeName, StringComparison.CurrentCultureIgnoreCase) && item.Size == (long)properties.Size))
                        continue;
                    var content = await ReadAttachmentAsync(file);
                    if (currentTotal + content.LongLength > MailAttachmentPolicy.MaximumTotalBytes)
                        throw new InvalidOperationException(_loader.GetStringOrDefault("TextAttachmentTotalLimit") ?? "Attachments may total up to 10 MB.");
                    var attachment = new MailAttachmentData(safeName, "application/octet-stream", content);
                    ComposeAttachments.Add(attachment);
                    currentTotal += attachment.Size;
                    UpdateAttachmentSummary();
                }
            }
            catch (Exception ex)
            {
                SetComposeStatus(string.Format(
                    _loader.GetStringOrDefault("TextAttachmentAddFailed") ?? "Could not add attachment: {0}",
                    UserSafeErrorMessage.FromException(ex)));
            }
        }

        private static async Task<byte[]> ReadAttachmentAsync(StorageFile file)
        {
            await using var source = await file.OpenStreamForReadAsync();
            using var destination = new MemoryStream();
            var buffer = new byte[64 * 1024];
            while (true)
            {
                int read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length));
                if (read == 0) break;
                if (destination.Length + read > MailAttachmentPolicy.MaximumFileBytes)
                    throw new InvalidOperationException("The attachment exceeds the per-file limit.");
                await destination.WriteAsync(buffer.AsMemory(0, read));
            }
            if (destination.Length == 0)
                throw new InvalidOperationException("Empty attachments are not supported.");
            return destination.ToArray();
        }

        private void RemoveAttachmentButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isSendingMail || sender is not Button { DataContext: MailAttachmentData attachment }) return;
            ComposeAttachments.Remove(attachment);
            UpdateAttachmentSummary();
        }

        private void UpdateAttachmentSummary()
        {
            if (AttachmentSummaryText == null) return;
            AttachmentSummaryText.Text = string.Format(
                _loader.GetStringOrDefault("TextAttachmentSummary") ?? "{0} files · {1} of 10 MB",
                ComposeAttachments.Count,
                MailAttachmentPolicy.FormatSize(ComposeAttachments.Sum(item => item.Size)));
        }

        private void ReportMailSendProgress(MailSendProgress progress)
        {
            AttachmentProgress.Visibility = progress.TotalFiles > 0 ? Visibility.Visible : Visibility.Collapsed;
            var status = progress.Stage switch
            {
                MailSendStage.Preparing => _loader.GetStringOrDefault("TextPreparingAttachments") ?? "Preparing attachments...",
                MailSendStage.UploadingAttachments => _loader.GetStringOrDefault("TextUploadingAttachments") ?? "Uploading attachments...",
                MailSendStage.Confirming => _loader.GetStringOrDefault("TextConfirmingSend") ?? "Confirming send status...",
                _ => _loader.GetStringOrDefault("TextSending") ?? "Sending..."
            };
            SetComposeStatus(status);
        }

        private async void AddOutlookButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mailService == null) return;

            _accountAuthCts?.Cancel();
            var authCts = CancellationTokenSource.CreateLinkedTokenSource(_pageRequestCts.Token);
            _accountAuthCts = authCts;

            SetAddButtonsEnabled(false);
            AddStatusText.Text = _loader.GetStringOrDefault("TextOpeningMSAuth") ?? "Opening Microsoft authorization...";

            try
            {
                var account = await _mailService.AddOutlookAccountAsync(authCts.Token);
                AddStatusText.Text = string.Format(_loader.GetStringOrDefault("TextAccountAdded") ?? "Added {0}", account.DisplayTitle);
                await RefreshAccountsAsync();
            }
            catch (OperationCanceledException) when (authCts.IsCancellationRequested) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Add Outlook account failed: {ex.Message}");
                AddStatusText.Text = _loader.GetStringOrDefault("TextAddOutlookFailed") ?? "Failed to add Outlook account. Please check authorization or network.";
            }
            finally
            {
                CompleteAccountAuthorization(authCts);
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

            _accountAuthCts?.Cancel();
            var authCts = CancellationTokenSource.CreateLinkedTokenSource(_pageRequestCts.Token);
            _accountAuthCts = authCts;

            SetAddButtonsEnabled(false);
            AddStatusText.Text = _loader.GetStringOrDefault("TextOpeningGoogleAuth") ?? "Opening Google authorization...";

            try
            {
                var account = await _mailService.AddGoogleAccountAsync(authCts.Token);
                AddStatusText.Text = string.Format(_loader.GetStringOrDefault("TextAccountAdded") ?? "Added {0}", account.DisplayTitle);
                await RefreshAccountsAsync();
            }
            catch (OperationCanceledException) when (authCts.IsCancellationRequested) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Add Google account failed: {ex.Message}");
                AddStatusText.Text = _loader.GetStringOrDefault("TextAddGmailFailed") ?? "Failed to add Gmail account. Please check authorization or network.";
            }
            finally
            {
                CompleteAccountAuthorization(authCts);
                SetAddButtonsEnabled(true);
            }
        }

        private void CompleteAccountAuthorization(CancellationTokenSource authCts)
        {
            if (ReferenceEquals(_accountAuthCts, authCts))
                _accountAuthCts = null;
            authCts.Dispose();
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
            _imapSetupCts?.Cancel();
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
            _imapSetupCts?.Cancel();
            var setupCts = CancellationTokenSource.CreateLinkedTokenSource(_pageRequestCts.Token);
            _imapSetupCts = setupCts;

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
                    smtpUserName,
                    setupCts.Token);

                AddStatusText.Text = string.Format(_loader.GetStringOrDefault("TextAccountAdded") ?? "Added {0}", account.DisplayTitle);
                await RefreshAccountsAsync();
            }
            catch (OperationCanceledException) when (setupCts.IsCancellationRequested) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Add IMAP account failed: {ex.Message}");
                AddStatusText.Text = _loader.GetStringOrDefault("TextImapConnectFailed") ?? "Failed to connect to IMAP server. Please check settings and credentials.";
            }
            finally
            {
                if (ReferenceEquals(_imapSetupCts, setupCts))
                    _imapSetupCts = null;
                setupCts.Dispose();
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
            ShowMailPane(MailPane.Detail);
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
            catch (MailReadSyncQueuedException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Mark as read queued: {ex.Message}");
                SetMessageListStatus(_loader.GetStringOrDefault("TextReadSyncQueued") ?? "Read status will sync automatically when the account is available.");
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
            RaiseLiveRegionChanged(MessageListSubtitle);
        }

        private void SetComposeStatus(string message)
        {
            if (ComposeStatusText == null) return;
            ComposeStatusText.Text = message;
            RaiseLiveRegionChanged(ComposeStatusText);
        }

        private static void RaiseLiveRegionChanged(FrameworkElement element)
        {
            var peer = FrameworkElementAutomationPeer.FromElement(element) ??
                       FrameworkElementAutomationPeer.CreatePeerForElement(element);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
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

                            WebView2RuntimeService.RegisterProfile(coreWebView.Profile);
                            _webView2Configured = true;
                            var settings = coreWebView.Settings;
                            settings.IsScriptEnabled = false;
                            settings.IsWebMessageEnabled = false;
                            settings.AreDefaultContextMenusEnabled = false;
                            settings.AreDevToolsEnabled = false;
                            settings.IsStatusBarEnabled = false;
                            settings.IsPinchZoomEnabled = true;
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

                    var fetched = await RemoteImageProxyService.Instance.FetchAsync(uri!, cts.Token);
                    if (fetched != null)
                    {
                        args.Response = coreWebView.Environment.CreateWebResourceResponse(
                            fetched.Stream, 200, "OK", $"Content-Type: {fetched.ContentType}");
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
            if (_isSendingMail) return;
            if (!string.IsNullOrWhiteSpace(ComposeToBox.Text) ||
                !string.IsNullOrWhiteSpace(ComposeSubjectBox.Text) ||
                !string.IsNullOrWhiteSpace(ComposeBodyBox.Text) ||
                ComposeAttachments.Count > 0)
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

            try
            {
                await ClearComposeDraftAsync();
            }
            catch
            {
                SetComposeStatus(_loader.GetStringOrDefault("TextDiscardDraftFailed") ?? "The protected draft could not be deleted. Try again.");
                return;
            }
            ComposePanel.Visibility = Visibility.Collapsed;
            ClearDetail();
        }

        private async void ReplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null) return;
            if (ComposePanel.Visibility != Visibility.Visible && await OfferDraftRecoveryAsync()) return;
            ShowComposePanel(_selectedItem);
        }

        private async void SendComposeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mailService == null || _isSendingMail) return;

            if (ComposeFromBox.SelectedItem is not ComboBoxItem selectedFrom)
            {
                SetComposeStatus(_loader.GetStringOrDefault("TextSelectSender") ?? "Please select a sender.");
                return;
            }

            var account = _mailService.GetAccounts().FirstOrDefault(a => a.Id == selectedFrom.Tag?.ToString());
            if (account == null)
            {
                SetComposeStatus(_loader.GetStringOrDefault("TextSenderNotFound") ?? "Sender account not found.");
                return;
            }

            if (string.IsNullOrWhiteSpace(ComposeToBox.Text))
            {
                SetComposeStatus(_loader.GetStringOrDefault("TextEnterRecipient") ?? "Please enter a recipient.");
                return;
            }

            _isSendingMail = true;
            bool sentButCleanupFailed = false;
            var submitted = CaptureComposeDraft();
            var submittedAttachments = ComposeAttachments.ToList();
            var attachmentError = MailAttachmentPolicy.Validate(submittedAttachments);
            if (attachmentError != null)
            {
                _isSendingMail = false;
                SetComposeStatus(attachmentError);
                return;
            }
            Drafts?.Schedule(submitted);
            if (Drafts != null) await Drafts.FlushAsync();
            SendComposeButton.IsEnabled = false;
            CancelComposeButton.IsEnabled = false;
            ComposeFromBox.IsEnabled = false;
            ComposeToBox.IsEnabled = false;
            ComposeSubjectBox.IsEnabled = false;
            ComposeBodyBox.IsEnabled = false;
            AddAttachmentsButton.IsEnabled = false;
            AttachmentList.IsEnabled = false;
            AttachmentProgress.Visibility = submittedAttachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            SetComposeStatus(_loader.GetStringOrDefault("TextSending") ?? "Sending...");

            try
            {
                var progress = new Progress<MailSendProgress>(ReportMailSendProgress);
                await _mailService.SendMailAsync(
                    account,
                    submitted.Recipient,
                    submitted.Subject,
                    submitted.Body,
                    submittedAttachments,
                    progress);
                try
                {
                    await ClearComposeDraftAsync();
                }
                catch
                {
                    sentButCleanupFailed = true;
                    SetComposeStatus(_loader.GetStringOrDefault("TextMailSentDraftCleanupFailed") ?? "Email sent, but the protected recovery copy could not be deleted. Discard it before composing again.");
                    return;
                }
                SetComposeStatus(_loader.GetStringOrDefault("TextMailSent") ?? "Email sent.");
                ComposePanel.Visibility = Visibility.Collapsed;
                ClearDetail();
            }
            catch (MailSendStatusUnknownException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Send mail status unknown: {ex.Message}");
                SetComposeStatus(_loader.GetStringOrDefault("TextSendStatusUnknown") ?? "Sending timed out. Check your Sent folder before trying again.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Send mail failed: {ex.Message}");
                SetComposeStatus(_loader.GetStringOrDefault("TextSendFailed") ?? "Failed to send email. Please check network and account settings.");
            }
            finally
            {
                _isSendingMail = false;
                SendComposeButton.IsEnabled = !sentButCleanupFailed;
                CancelComposeButton.IsEnabled = true;
                ComposeFromBox.IsEnabled = !sentButCleanupFailed;
                ComposeToBox.IsEnabled = !sentButCleanupFailed;
                ComposeSubjectBox.IsEnabled = !sentButCleanupFailed;
                ComposeBodyBox.IsEnabled = !sentButCleanupFailed;
                AddAttachmentsButton.IsEnabled = !sentButCleanupFailed;
                AttachmentList.IsEnabled = !sentButCleanupFailed;
                AttachmentProgress.Visibility = Visibility.Collapsed;
                if (ComposePanel.Visibility == Visibility.Visible && !sentButCleanupFailed)
                    SendComposeButton.Focus(FocusState.Programmatic);
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
