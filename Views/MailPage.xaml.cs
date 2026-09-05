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
        private readonly Dictionary<string, long> _mailIntentVersions = new(StringComparer.Ordinal);
        private List<MailUndoState> _undoStates = new();
        private MailMoveUndoState? _moveUndoState;
        private GmailUndoState? _gmailUndoState;
        private ImapUndoState? _imapUndoState;
        private MailMutationKind _undoKind;
        private long _nextMailIntentVersion;

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
        private MailPane _compactMailPane = MailPane.Accounts;
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
        private readonly VersionedUiRefreshGate _cacheRefreshGate = new();
        private bool _isPageLoaded;
        private bool _isSendingMail;
        private bool _isOnlineMailAction;
        private bool _suppressDraftChanges;
        private Task<bool>? _draftRecoveryTask;
        internal bool IsOpeningFromNotification { get; set; }
        private ComposeDraftCoordinator? Drafts => (App.Current as App)?.ComposeDrafts;

        private enum MailPane { Accounts, Messages, Detail }
        private sealed record MailUndoState(MailItem Item, bool PreviousValue, int PreviousIndex);
        private sealed record MailMoveUndoState(MailAccount Account, MailItem MovedItem, string ReturnFolderId);
        private sealed record GmailUndoState(MailAccount Account, GmailLabelMutationResult Result);
        private sealed record ImapUndoState(MailAccount Account, ImapMoveResult Result);

        public MailPage()
        {
            this.Language = Windows.Globalization.ApplicationLanguages.Languages[0];
            InitializeComponent();
            _items.CollectionChanged += (_, _) => ApplyMailSearch();
            Loaded += MailPage_Loaded;
            Unloaded += MailPage_Unloaded;
            ActualThemeChanged += MailPage_ActualThemeChanged;
        }

        private bool _openingCachedMetadataOnly;

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
            _isPageLoaded = false;
            if (_mailService != null)
                _mailService.CachePublished -= MailService_CachePublished;
            _pageRequestCts.Cancel();
            _messageLoadCts?.Cancel();
            _bodyLoadCts?.Cancel();
            _accountAuthCts?.Cancel();
            _imapSetupCts?.Cancel();
            _searchCts?.Cancel();
            ScheduleComposeDraft();
            _messageLoadVersion++;
            CleanupRenderedMailContent(clearAllBodies: true);
            _selectedItem = null;
            SetActiveAccount(null);
            _selectedFolder = null;
            _selectedAccountForRemoval = null;
            _refreshAccountsTask = null;

            MailListView.ItemsSource = null;
            _items.Clear();
            _displayedItems.Clear();
            ComposeAttachments.Clear();
            _accountsById.Clear();
            _accountNodes.Clear();
            _folderNodes.Clear();
        }

        public void ReleaseForMemoryPressure()
        {
            _bodyLoadCts?.Cancel();
            CleanupRenderedMailContent(clearAllBodies: true);
            _selectedItem = null;
            _mailService?.UpdateActiveBodyCacheContext(_selectedAccount?.Id);
            DetailPanel.Visibility = Visibility.Collapsed;
            EmptyDetailPanel.Visibility = Visibility.Visible;
        }

        private void SetActiveAccount(MailAccount? account)
        {
            if (!string.Equals(_selectedAccount?.Id, account?.Id, StringComparison.Ordinal))
            {
                _bodyLoadCts?.Cancel();
                _selectedItem = null;
                ClearRenderedMailBody();
            }
            _selectedAccount = account;
            UpdateProviderMoveCommands();
            var protectedItem = string.Equals(_selectedItem?.AccountId, account?.Id, StringComparison.Ordinal)
                ? _selectedItem
                : null;
            _mailService?.UpdateActiveBodyCacheContext(account?.Id, protectedItem);
        }

        private async void MailPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (_pageRequestCts.IsCancellationRequested)
            {
                _pageRequestCts.Dispose();
                _pageRequestCts = new CancellationTokenSource();
            }
            _mailService = (App.Current as App)?.MailService;
            _isPageLoaded = true;
            if (_mailService != null)
            {
                _mailService.CachePublished -= MailService_CachePublished;
                _mailService.CachePublished += MailService_CachePublished;
            }
            MailListView.ItemsSource = _displayedItems;
            UpdateResponsiveLayout(LayoutRoot.ActualWidth, LayoutRoot.ActualHeight);
            _isInitializing = false;
            await RefreshAccountsAsync(autoSelect: !IsOpeningFromNotification);
            if (!IsOpeningFromNotification)
                await OfferDraftRecoveryAsync();
            if (IsOpeningFromNotification && _layoutMode == ResponsiveLayoutMode.Narrow)
            {
                _compactMailPane = MailPane.Detail;
                ApplyResponsiveLayout();
            }
        }

        private async void MailPage_ActualThemeChanged(FrameworkElement sender, object args)
        {
            if (_selectedItem != null && DetailPanel.Visibility == Visibility.Visible)
                await RenderMailBodyAsync(_selectedItem);
        }

        private void MailService_CachePublished(object? sender, MailCachePublishedEventArgs e)
        {
            if (!_cacheRefreshGate.TryQueue(e.Version)) return;
            if (!DispatcherQueue.TryEnqueue(ApplyPublishedMailCacheUpdate))
                _cacheRefreshGate.CancelQueuedDispatch();
        }

        private void ApplyPublishedMailCacheUpdate()
        {
            if (!_cacheRefreshGate.TryBeginApply(out _) || !_isPageLoaded || _mailService == null)
                return;

            ApplyCachedFolderSnapshots();
            if (!_isLoadingMessages)
                ApplyCachedSelectedMessageWindow();
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
                SetActiveAccount(null);
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

        private void ApplyCachedFolderSnapshots()
        {
            var service = _mailService;
            if (service == null) return;

            foreach (string accountId in _accountNodes.Values
                         .Select(account => account.Id)
                         .Distinct(StringComparer.Ordinal))
            {
                ApplyFolderSnapshot(accountId, service.GetCachedFolderSnapshot(accountId));
            }
        }

        private void ApplyFolderSnapshot(string accountId, IReadOnlyList<MailFolder> folders)
        {
            if (folders.Count == 0) return;

            var byId = folders
                .GroupBy(folder => folder.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            foreach (var pair in _folderNodes.Where(pair =>
                         string.Equals(pair.Value.Account.Id, accountId, StringComparison.Ordinal)))
            {
                if (!byId.TryGetValue(pair.Value.Folder.Id, out var snapshot)) continue;
                CopyFolderMetadata(pair.Value.Folder, snapshot);
                pair.Key.Content = FormatFolderContent(pair.Value.Folder);
            }

            if (_selectedFolder != null &&
                string.Equals(_selectedFolder.AccountId, accountId, StringComparison.Ordinal) &&
                byId.TryGetValue(_selectedFolder.Id, out var selectedSnapshot))
            {
                CopyFolderMetadata(_selectedFolder, selectedSnapshot);
                MessageListTitle.Text = _selectedFolder.DisplayName;
            }
        }

        private static void CopyFolderMetadata(MailFolder target, MailFolder source)
        {
            target.DisplayName = source.DisplayName;
            target.UnreadCount = source.UnreadCount;
            target.IsPlaceholder = source.IsPlaceholder;
            target.IsUserLabel = source.IsUserLabel;
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
                if (folders.Count > 0)
                {
                    NextRenderHelper.RunOnce(() =>
                        PerformanceDiagnostics.MarkOnce(
                            "mail.folders.visible", "mail", "first_folders_visible", source: "ui"));
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
            SetActiveAccount(selected.Account);
            _selectedFolder = selected.Folder;
            await LoadMessagesAsync();
            ShowMailPane(MailPane.Messages);
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
            if (accountNode == null)
            {
                ShowMailNotificationTargetNotFound();
                return;
            }

            await LoadFoldersForNodeAsync(accountNode);
            accountNode.IsExpanded = true;

            var folderNode = accountNode.Children.FirstOrDefault(node =>
                _folderNodes.TryGetValue(node, out var selection) &&
                MailNotificationNavigationPolicy.FolderMatches(selection.Folder, folderId));
            MailItem? cachedFallback = null;
            if (folderNode == null)
            {
                // Folder IDs/display names can change between polling and toast
                // activation.  A stable Gmail/Outlook message ID can still open a
                // cached target; IMAP is excluded by the service because its UID is
                // folder-scoped.
                cachedFallback = _mailService.TryGetCachedMessageById(accountId, messageId);
                if (cachedFallback != null)
                {
                    var cachedFolderNode = accountNode.Children.FirstOrDefault(node =>
                        _folderNodes.TryGetValue(node, out var selection) &&
                        MailNotificationNavigationPolicy.FolderMatches(
                            selection.Folder, cachedFallback.FolderId));
                    if (cachedFolderNode != null)
                    {
                        folderNode = cachedFolderNode;
                        folderId = cachedFallback.FolderId;
                    }
                }
            }
            if (folderNode == null)
            {
                if (cachedFallback != null)
                {
                    AccountTree.SelectedNode = accountNode;
                    await OpenDetachedNotificationTargetAsync(
                        _accountNodes[accountNode],
                        cachedFallback);
                    return;
                }

                ShowMailNotificationTargetNotFound();
                return;
            }

            AccountTree.SelectedNode = folderNode;
            var selected = _folderNodes[folderNode];
            SetActiveAccount(selected.Account);
            _selectedFolder = selected.Folder;
            _selectedAccountForRemoval = selected.Account;
            RemoveMailButton.IsEnabled = true;
            SetUnreadOnlyWithoutReload(false);

            await LoadMessagesAsync(forceRefresh: false, preferredMessageId: messageId, selectFirstWhenNoMatch: false);

            var target = _items.FirstOrDefault(item =>
                MailNotificationNavigationPolicy.MessageMatches(
                    item, accountId, folderId, messageId));
            if (target == null)
            {
                target = _mailService.TryGetCachedMessage(accountId, folderId, messageId);
                if (target != null)
                {
                    _items.Insert(0, target);
                    SetMessageListStatus($"{selected.Account.DisplayTitle} · {string.Format(_loader.GetStringOrDefault("TextNMailItems") ?? "{0} messages", _items.Count)}");
                }
            }

            if (target == null)
            {
                await LoadMessagesAsync(forceRefresh: true, preferredMessageId: messageId, selectFirstWhenNoMatch: false);
                target = _items.FirstOrDefault(item =>
                    MailNotificationNavigationPolicy.MessageMatches(
                        item, accountId, folderId, messageId));
            }

            if (target != null)
            {
                MailListView.SelectedItem = target;
                MailListView.ScrollIntoView(target);
                if (!ReferenceEquals(target, _selectedItem))
                    await OpenMailItemAsync(target);
            }
            else
            {
                ShowMailNotificationTargetNotFound();
            }
        }

        private async Task OpenDetachedNotificationTargetAsync(MailAccount account, MailItem target)
        {
            SetActiveAccount(account);
            _selectedFolder = new MailFolder
            {
                AccountId = account.Id,
                Id = target.FolderId,
                DisplayName = target.FolderId
            };
            _selectedAccountForRemoval = account;
            RemoveMailButton.IsEnabled = true;
            SetUnreadOnlyWithoutReload(false);

            MessageListTitle.Text = target.FolderId;
            _items.Clear();
            _items.Add(target);
            MailListView.SelectedItem = target;
            MailListView.ScrollIntoView(target);
            SetMessageListStatus(
                $"{account.DisplayTitle} · {string.Format(_loader.GetStringOrDefault("TextNMailItems") ?? "{0} messages", _items.Count)}");
            await OpenMailItemAsync(target);
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

            SetActiveAccount(selection.Account);
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
            SetActiveAccount(selected.Account);
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
                MailItem? selectedItem;
                if (itemToSelect != null)
                {
                    MailListView.SelectedItem = itemToSelect;
                    selectedItem = itemToSelect;
                }
                else if (selectFirstWhenNoMatch
                         && ResponsiveLayoutPolicy.ShouldAutoSelectFirstMail(LayoutRoot.ActualWidth))
                {
                    selectedItem = _displayedItems.Count > 0 ? _displayedItems[0] : null;
                    MailListView.SelectedItem = selectedItem;
                }
                else
                {
                    MailListView.SelectedItem = null;
                    selectedItem = null;
                }

                if (selectedItem != null && !ReferenceEquals(selectedItem, _selectedItem))
                    _ = OpenMailItemAsync(selectedItem);
                else if (selectedItem == null && _layoutMode != ResponsiveLayoutMode.Wide)
                    ClearDetail();

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

        private void ApplyCachedSelectedMessageWindow()
        {
            var service = _mailService;
            var account = _selectedAccount;
            var folder = _selectedFolder;
            if (service == null || account == null || folder == null ||
                !service.TryGetCachedMessageWindowSnapshot(
                    account.Id,
                    folder.Id,
                    UnreadOnlyToggle.IsOn,
                    out var window))
                return;

            string? selectedKey = MailListView.SelectedItem is MailItem selected
                ? GetPageMessageIdentity(account.Kind, selected)
                : null;
            string? detailKey = _selectedItem == null
                ? null
                : GetPageMessageIdentity(account.Kind, _selectedItem);
            var existingByKey = _items
                .GroupBy(item => GetPageMessageIdentity(account.Kind, item), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var desiredItems = new List<MailItem>();
            foreach (var snapshot in window.Items
                         .OrderByDescending(item => item.RawReceivedTime)
                         .GroupBy(item => GetPageMessageIdentity(account.Kind, item), StringComparer.Ordinal)
                         .Select(group => group.First()))
            {
                string key = GetPageMessageIdentity(account.Kind, snapshot);
                if (existingByKey.TryGetValue(key, out var existing))
                {
                    CopyCachedMailMetadata(existing, snapshot);
                    desiredItems.Add(existing);
                }
                else
                {
                    desiredItems.Add(snapshot);
                }
            }

            _suppressSearchRefresh = true;
            _suppressSelectionClear = true;
            try
            {
                for (int index = 0; index < desiredItems.Count; index++)
                {
                    var desired = desiredItems[index];
                    if (index < _items.Count && ReferenceEquals(_items[index], desired)) continue;

                    int existingIndex = _items.IndexOf(desired);
                    if (existingIndex >= 0)
                        _items.Move(existingIndex, index);
                    else
                        _items.Insert(index, desired);
                }
                while (_items.Count > desiredItems.Count)
                    _items.RemoveAt(_items.Count - 1);

                _suppressSearchRefresh = false;
                ApplyMailSearch();

                string? selectionKey = selectedKey ?? detailKey;
                MailListView.SelectedItem = selectionKey == null
                    ? null
                    : _displayedItems.FirstOrDefault(item =>
                        string.Equals(
                            GetPageMessageIdentity(account.Kind, item),
                            selectionKey,
                            StringComparison.Ordinal));

                if (detailKey != null)
                {
                    var detailItem = _items.FirstOrDefault(item =>
                        string.Equals(
                            GetPageMessageIdentity(account.Kind, item),
                            detailKey,
                            StringComparison.Ordinal));
                    if (detailItem == null)
                    {
                        ClearDetail();
                    }
                    else
                    {
                        _selectedItem = detailItem;
                        DetailSubject.Text = detailItem.Subject;
                        DetailSender.Text = detailItem.Sender;
                        DetailTime.Text = detailItem.RawReceivedTime?.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                            ?? detailItem.ReceivedTime;
                        OpenInBrowserButton.IsEnabled = !string.IsNullOrWhiteSpace(detailItem.WebLink);
                    }
                }
            }
            finally
            {
                _suppressSearchRefresh = false;
                _suppressSelectionClear = false;
            }

            LoadMoreButton.Visibility = window.HasMore ? Visibility.Visible : Visibility.Collapsed;
            SetMessageListStatus(
                $"{account.DisplayTitle} · {string.Format(_loader.GetStringOrDefault("TextNMailItems") ?? "{0} messages", _items.Count)}");
            UpdateProviderMoveCommands();
        }

        private static string GetPageMessageIdentity(MailAccountKind providerKind, MailItem item)
            => providerKind == MailAccountKind.Imap
                ? $"{item.AccountId}\u001f{item.FolderId}\u001f{item.ImapUidValidity?.ToString() ?? "?"}\u001f{item.Id}"
                : $"{item.AccountId}\u001f{item.Id}";

        private static void CopyCachedMailMetadata(MailItem target, MailItem source)
        {
            target.Subject = source.Subject;
            target.Sender = source.Sender;
            target.SenderAddress = source.SenderAddress;
            target.Recipient = source.Recipient;
            target.Preview = source.Preview;
            target.ReceivedTime = source.ReceivedTime;
            target.RawReceivedTime = source.RawReceivedTime;
            target.IsRead = source.IsRead;
            target.IsFlagged = source.IsFlagged;
            target.HasAttachments = source.HasAttachments;
            target.Importance = source.Importance;
            target.WebLink = source.WebLink;
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

        private async Task RefreshFolderCountsAsync(MailAccount account)
        {
            var service = _mailService;
            if (service == null) return;

            var cancellationToken = _pageRequestCts.Token;
            try
            {
                var folders = await service.FetchFoldersAsync(
                    account,
                    forceRefresh: true,
                    cancellationToken);
                if (!_isPageLoaded || !string.Equals(_selectedAccount?.Id, account.Id, StringComparison.Ordinal))
                    return;
                ApplyFolderSnapshot(account.Id, folders);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Refresh mail folder counts failed: {ex.Message}");
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

            var folderRefresh = _selectedAccount == null
                ? Task.CompletedTask
                : RefreshFolderCountsAsync(_selectedAccount);
            await Task.WhenAll(
                folderRefresh,
                LoadMessagesAsync(forceRefresh: true));
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
                    await app.DisconnectProviderCompletelyAsync(
                        account.ProviderName,
                        account.ProviderAccountId);
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
            SetActiveAccount(null);
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
            SetActiveAccount(null);
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
            _compactMailPane = pane;
            ApplyResponsiveLayout();
        }

        private void LayoutRoot_SizeChanged(object sender, SizeChangedEventArgs e)
            => UpdateResponsiveLayout(e.NewSize.Width, e.NewSize.Height);

        private void UpdateResponsiveLayout(double width, double height)
        {
            ComposeBodyBox.MinHeight = height < 600 ? 120 : 220;
            var mode = ResponsiveLayoutPolicy.GetMailMode(width);
            if (_layoutMode != mode)
            {
                _layoutMode = mode;
                if (mode != ResponsiveLayoutMode.Wide)
                    _compactMailPane = _selectedItem != null ? MailPane.Detail
                        : _selectedFolder != null ? MailPane.Messages : MailPane.Accounts;
            }
            ApplyResponsiveLayout();
        }

        private void MailBackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_layoutMode == ResponsiveLayoutMode.Wide) return;
            _compactMailPane = _compactMailPane == MailPane.Detail ? MailPane.Messages : MailPane.Accounts;
            ApplyResponsiveLayout();
        }

        private void MailAccountsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_layoutMode != ResponsiveLayoutMode.Medium) return;
            _compactMailPane = _compactMailPane == MailPane.Accounts && _selectedFolder != null
                ? MailPane.Messages
                : MailPane.Accounts;
            ApplyResponsiveLayout();
        }

        private void ApplyResponsiveLayout()
        {
            bool wide = _layoutMode == ResponsiveLayoutMode.Wide;
            bool medium = _layoutMode == ResponsiveLayoutMode.Medium;
            bool showAccounts = wide || !wide && _compactMailPane == MailPane.Accounts;
            bool showMessages = wide || !wide && _compactMailPane == MailPane.Messages;
            bool showDetail = wide || !wide && _compactMailPane == MailPane.Detail;

            AccountColumn.MinWidth = wide ? 190 : 0;
            AccountColumn.Width = showAccounts ? (wide ? new GridLength(2, GridUnitType.Star) : new GridLength(1, GridUnitType.Star)) : new GridLength(0);
            MessageColumn.MinWidth = wide ? 280 : 0;
            MessageColumn.Width = showMessages ? new GridLength(wide ? 3 : 1, GridUnitType.Star) : new GridLength(0);
            DetailColumn.MinWidth = 0;
            DetailColumn.Width = showDetail ? new GridLength(wide ? 5 : 1, GridUnitType.Star) : new GridLength(0);
            MailColumnsGrid.ColumnSpacing = wide ? 12 : 0;
            MailAccountPane.Visibility = showAccounts ? Visibility.Visible : Visibility.Collapsed;
            MailMessagePane.Visibility = showMessages ? Visibility.Visible : Visibility.Collapsed;
            MailDetailPane.Visibility = showDetail ? Visibility.Visible : Visibility.Collapsed;
            MailBackButton.Visibility = (_layoutMode == ResponsiveLayoutMode.Narrow && _compactMailPane != MailPane.Accounts)
                || (medium && _compactMailPane == MailPane.Detail)
                ? Visibility.Visible : Visibility.Collapsed;
            MailAccountsButton.Visibility = medium ? Visibility.Visible : Visibility.Collapsed;
            UnreadOnlyToggle.Visibility = _layoutMode == ResponsiveLayoutMode.Narrow ? Visibility.Collapsed : Visibility.Visible;
            ComposeButtonText.Visibility = _layoutMode == ResponsiveLayoutMode.Narrow ? Visibility.Collapsed : Visibility.Visible;
            RefreshButtonText.Visibility = _layoutMode == ResponsiveLayoutMode.Narrow ? Visibility.Collapsed : Visibility.Visible;
            double padding = ResponsiveLayoutPolicy.GetPagePadding(
                LayoutRoot.ActualWidth,
                LayoutRoot.ActualHeight);
            LayoutRoot.Padding = new Thickness(padding);
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

        private async Task<byte[]> ReadAttachmentAsync(StorageFile file)
        {
            await using var source = await file.OpenStreamForReadAsync();
            using var destination = new MemoryStream();
            var buffer = new byte[64 * 1024];
            while (true)
            {
                int read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length));
                if (read == 0) break;
                if (destination.Length + read > MailAttachmentPolicy.MaximumFileBytes)
                    throw new InvalidOperationException(_loader.GetStringOrDefault("TextAttachmentFileLimit") ?? "Attachments may be up to 3 MB each.");
                await destination.WriteAsync(buffer.AsMemory(0, read));
            }
            if (destination.Length == 0)
                throw new InvalidOperationException(_loader.GetStringOrDefault("TextAttachmentEmpty") ?? "Empty attachments are not supported.");
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
                if (App.Current is App app && app.SyncManager.GetProvider("Microsoft") is ISyncProvider provider)
                    await provider.ConnectInteractivelyAsync(authCts.Token);
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
                if (App.Current is not App app)
                    throw new InvalidOperationException("The application account service is unavailable.");
                var account = await app.ConnectNewGoogleAccountAsync(authCts.Token);
                AddStatusText.Text = string.Format(_loader.GetStringOrDefault("TextAccountAdded") ?? "Added {0}", account.DisplayTitle);
                await RefreshAccountsAsync();
            }
            catch (OperationCanceledException) when (authCts.IsCancellationRequested) { }
            catch (GoogleAccountAlreadyConnectedException ex)
            {
                AddStatusText.Text = string.Format(
                    _loader.GetStringOrDefault("AddAccount_GoogleAlreadyConnected") ?? "Google account {0} is already connected.",
                    ex.Address);
            }
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

        private async void MailListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not MailItem item) return;
            MailListView.SelectedItem = item;
            await OpenMailItemAsync(item);
        }

        private void MailListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateProviderMoveCommands();
            if (MailListView.SelectedItem == null && !_isLoadingMessages && !_suppressSelectionClear)
                ClearDetail();
        }

        private async Task OpenMailItemAsync(MailItem item)
        {
            _selectedItem = item;
            _mailService?.UpdateActiveBodyCacheContext(_selectedAccount?.Id, item);
            ClearRenderedMailBody();
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
            if (_openingCachedMetadataOnly)
            {
                _openingCachedMetadataOnly = false;
                DetailHtmlViewHost.Visibility = Visibility.Collapsed;
                DetailTextScrollViewer.Visibility = Visibility.Visible;
                DetailPreview.Text = item.Preview;
                ReplyButton.IsEnabled = false;
                OpenInBrowserButton.IsEnabled = false;
                CompleteBodyLoad(bodyCts);
                return;
            }
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
            if (bodyCts.IsCancellationRequested || !ReferenceEquals(item, _selectedItem))
            {
                CompleteBodyLoad(bodyCts);
                return;
            }
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

            long version = ++_nextMailIntentVersion;
            _mailIntentVersions[GetIntentKey(item, MailMutationKind.SetReadState)] = version;
            _mailService.ApplyCachedMutation(item, MailMutationKind.SetReadState, true);
            var remoteSyncTask = CompleteAutomaticReadMutationAsync(_selectedAccount, item, version);

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

        private async Task CompleteAutomaticReadMutationAsync(MailAccount account, MailItem item, long version)
        {
            try
            {
                await _mailService!.SetReadStateAsync(account, item, true);
            }
            catch (MailMutationSyncQueuedException) { throw; }
            catch
            {
                if (MailBulkMutationPolicy.ShouldRollback(version, GetCurrentIntent(item, MailMutationKind.SetReadState)))
                {
                    _mailService!.ApplyCachedMutation(item, MailMutationKind.SetReadState, false);
                    RestoreUnreadItemIfNeeded(new MailUndoState(item, false, 0), MailMutationKind.SetReadState, false);
                }
                throw;
            }
        }

        private void ShowMailNotificationTargetNotFound()
        {
            SetMessageListStatus(
                _loader.GetStringOrDefault("TextMailNotFound")
                    ?? "This message was not found in the local cache or current folder.",
                isError: true);
            ClearDetail();
        }

        private async Task CompleteMarkAsReadAsync(Task remoteSyncTask)
        {
            try
            {
                await remoteSyncTask;
            }
            catch (MailMutationSyncQueuedException ex)
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
            MessageListSubtitle.Text = StatusMessageFormatter.Format(
                message,
                _lastMessageLoadSucceededAt,
                isError,
                _loader.GetStringOrDefault("TextLastSuccessFormat") ?? "Last success: {0:g}",
                LocalizationHelper.AppCulture);
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
            _mailService?.UpdateActiveBodyCacheContext(_selectedAccount?.Id);
            DetailPanel.Visibility = Visibility.Collapsed;
            ClearRenderedMailBody();
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

        private async void MarkReadButton_Click(object sender, RoutedEventArgs e)
            => await MutateSelectedMailAsync(MailMutationKind.SetReadState, true);

        private async void MarkUnreadButton_Click(object sender, RoutedEventArgs e)
            => await MutateSelectedMailAsync(MailMutationKind.SetReadState, false);

        private async void FlagButton_Click(object sender, RoutedEventArgs e)
            => await MutateSelectedMailAsync(MailMutationKind.SetFlagged, true);

        private async void UnflagButton_Click(object sender, RoutedEventArgs e)
            => await MutateSelectedMailAsync(MailMutationKind.SetFlagged, false);

        private async void ArchiveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedAccount?.Kind == MailAccountKind.Google)
                await MutateSelectedGmailMailAsync((service, account, item, token) => service.ArchiveGmailMessageAsync(account, item, token));
            else if (_selectedAccount?.Kind == MailAccountKind.Imap)
                await MoveSelectedImapMailAsync((service, account, item, token) => service.ArchiveImapMessageAsync(account, item, token));
            else
                await MoveSelectedOutlookMailAsync((service, account, item, token) => service.ArchiveOutlookMessageAsync(account, item, token));
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedAccount?.Kind == MailAccountKind.Google)
                await MutateSelectedGmailMailAsync((service, account, item, token) => service.TrashGmailMessageAsync(account, item, token));
            else if (_selectedAccount?.Kind == MailAccountKind.Imap)
                await MoveSelectedImapMailAsync((service, account, item, token) => service.TrashImapMessageAsync(account, item, token));
            else
                await MoveSelectedOutlookMailAsync((service, account, item, token) => service.TrashOutlookMessageAsync(account, item, token));
        }

        private async void MoveButton_Click(object sender, RoutedEventArgs e)
        {
            var service = _mailService;
            var account = _selectedAccount;
            var item = GetSingleSelectedOnlineActionItem();
            if (service == null || account == null || item == null) return;

            SetProviderMoveCommandsEnabled(false);
            try
            {
                if (account.Kind == MailAccountKind.Google)
                {
                    var labels = await service.FetchGmailMoveDestinationsAsync(account, item.FolderId, _pageRequestCts.Token);
                    if (labels.Count == 0)
                    {
                        SetMessageListStatus(GetResourceStringOrDefault("TextGmailMoveNoDestinations", "No custom labels are available."), isError: true);
                        return;
                    }
                    var labelPicker = CreateDestinationPicker(labels, "TextGmailMoveDestination", "Destination label");
                    var labelDialog = CreateMoveDialog(labelPicker, "TextGmailMoveTitle", "Move message to label");
                    if (await labelDialog.ShowAsync() != ContentDialogResult.Primary || labelPicker.SelectedItem is not GmailLabelDestination label)
                        return;
                    if (!ReferenceEquals(item, GetSingleSelectedOnlineActionItem()) || !ReferenceEquals(account, _selectedAccount))
                        return;
                    await MutateSelectedGmailMailAsync((mailService, selectedAccount, selectedItem, token) =>
                        mailService.MoveGmailMessageToLabelAsync(selectedAccount, selectedItem, label.Id, token), item);
                    return;
                }

                if (account.Kind == MailAccountKind.Imap)
                {
                    var folders = await service.FetchImapMoveDestinationsAsync(account, item.FolderId, _pageRequestCts.Token);
                    if (folders.Count == 0)
                    {
                        SetMessageListStatus(GetResourceStringOrDefault("TextImapMoveNoDestinations", "No IMAP destination folders are available."), isError: true);
                        return;
                    }
                    var folderPicker = CreateDestinationPicker(folders, "TextMailMoveDestination", "Destination folder");
                    var folderDialog = CreateMoveDialog(folderPicker, "TextMailMoveTitle", "Move message");
                    if (await folderDialog.ShowAsync() != ContentDialogResult.Primary || folderPicker.SelectedItem is not ImapMoveDestination folder)
                        return;
                    if (!ReferenceEquals(item, GetSingleSelectedOnlineActionItem()) || !ReferenceEquals(account, _selectedAccount))
                        return;
                    await MoveSelectedImapMailAsync((mailService, selectedAccount, selectedItem, token) =>
                        mailService.MoveImapMessageAsync(selectedAccount, selectedItem, folder.FullName, token), item);
                    return;
                }

                var destinations = await service.FetchOutlookMoveDestinationsAsync(account, item.FolderId, _pageRequestCts.Token);
                if (destinations.Count == 0)
                {
                    SetMessageListStatus(GetResourceStringOrDefault("TextMailMoveNoDestinations", "No destination folders are available."), isError: true);
                    return;
                }

                var picker = new ComboBox
                {
                    Header = GetResourceStringOrDefault("TextMailMoveDestination", "Destination folder"),
                    ItemsSource = destinations,
                    SelectedIndex = 0,
                    MinWidth = 0,
                    MaxWidth = 420,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                var dialog = new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    Title = GetResourceStringOrDefault("TextMailMoveTitle", "Move message"),
                    Content = picker,
                    PrimaryButtonText = GetResourceStringOrDefault("MailPage_Move.Label", "Move"),
                    CloseButtonText = GetResourceStringOrDefault("CalendarDialog.CloseButtonText", "Cancel"),
                    DefaultButton = ContentDialogButton.Primary
                };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary || picker.SelectedItem is not OutlookMoveDestination destination)
                    return;

                await MoveSelectedOutlookMailAsync((mailService, selectedAccount, selectedItem, token) =>
                    mailService.MoveOutlookMessageAsync(selectedAccount, selectedItem, destination.Id, token));
            }
            catch (OperationCanceledException) when (_pageRequestCts.IsCancellationRequested) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Load mail move destinations failed: {ex.Message}");
                SetMessageListStatus(account.Kind switch
                {
                    MailAccountKind.Google => GetResourceStringOrDefault("TextGmailMoveDestinationsFailed", "Failed to load Gmail labels."),
                    MailAccountKind.Imap => GetResourceStringOrDefault("TextImapMoveDestinationsFailed", "Failed to load IMAP destination folders."),
                    _ => GetResourceStringOrDefault("TextMailMoveDestinationsFailed", "Failed to load destination folders.")
                }, isError: true);
            }
            finally
            {
                UpdateProviderMoveCommands();
            }
        }

        private ComboBox CreateDestinationPicker(object items, string headerKey, string headerFallback)
            => new()
            {
                Header = GetResourceStringOrDefault(headerKey, headerFallback),
                ItemsSource = items,
                SelectedIndex = 0,
                MinWidth = 0,
                MaxWidth = 420,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

        private ContentDialog CreateMoveDialog(object content, string titleKey, string titleFallback)
            => new()
            {
                XamlRoot = XamlRoot,
                Title = GetResourceStringOrDefault(titleKey, titleFallback),
                Content = content,
                PrimaryButtonText = GetResourceStringOrDefault("MailPage_Move.Label", "Move"),
                CloseButtonText = GetResourceStringOrDefault("CalendarDialog.CloseButtonText", "Cancel"),
                DefaultButton = ContentDialogButton.Primary
            };

        private MailItem? GetSingleSelectedOutlookItem()
            => _selectedAccount?.Kind == MailAccountKind.Outlook && MailListView.SelectedItems.Count == 1
                ? MailListView.SelectedItems[0] as MailItem
                : null;

        private MailItem? GetSingleSelectedOnlineActionItem()
            => (_selectedAccount?.Kind is MailAccountKind.Outlook or MailAccountKind.Google or MailAccountKind.Imap) && MailListView.SelectedItems.Count == 1
                ? MailListView.SelectedItems[0] as MailItem
                : null;

        private void UpdateProviderMoveCommands()
        {
            if (ArchiveButton == null) return;
            bool visible = _selectedAccount?.Kind is MailAccountKind.Outlook or MailAccountKind.Google or MailAccountKind.Imap;
            bool isGmail = _selectedAccount?.Kind == MailAccountKind.Google;
            bool isImap = _selectedAccount?.Kind == MailAccountKind.Imap;
            OutlookMoveSeparator.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            ArchiveButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            MoveButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            DeleteButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            ArchiveButton.Label = GetResourceStringOrDefault("MailPage_Archive.Label", "Archive");
            MoveButton.Label = GetResourceStringOrDefault(isGmail ? "MailPage_MoveToLabel.Label" : "MailPage_MoveToFolder.Label", isGmail ? "Move to label" : "Move to folder");
            DeleteButton.Label = GetResourceStringOrDefault(isGmail || isImap ? "MailPage_MoveToTrash.Label" : "MailPage_MoveToDeletedItems.Label", isGmail || isImap ? "Move to trash" : "Move to Deleted Items");
            bool selected = visible && MailListView.SelectedItems.Count == 1 && !_isLoadingMessages && !_isOnlineMailAction;
            SetProviderMoveCommandsEnabled(selected);
            if (isGmail)
            {
                ArchiveButton.IsEnabled = selected && string.Equals(_selectedFolder?.Id, "INBOX", StringComparison.Ordinal);
                MoveButton.IsEnabled = selected && (string.Equals(_selectedFolder?.Id, "INBOX", StringComparison.Ordinal) || _selectedFolder?.IsUserLabel == true);
                DeleteButton.IsEnabled = selected && !string.Equals(_selectedFolder?.Id, "TRASH", StringComparison.Ordinal);
            }
        }

        private void SetProviderMoveCommandsEnabled(bool enabled)
        {
            ArchiveButton.IsEnabled = enabled;
            MoveButton.IsEnabled = enabled;
            DeleteButton.IsEnabled = enabled;
        }

        private async Task MoveSelectedOutlookMailAsync(
            Func<MailService, MailAccount, MailItem, CancellationToken, Task<MailMoveResult>> move)
        {
            var service = _mailService;
            var account = _selectedAccount;
            var item = GetSingleSelectedOutlookItem();
            if (service == null || account == null || item == null) return;

            SetProviderMoveCommandsEnabled(false);
            try
            {
                var result = await move(service, account, item, _pageRequestCts.Token);
                _moveUndoState = new MailMoveUndoState(account, result.Item, result.SourceFolderId);
                _gmailUndoState = null;
                _imapUndoState = null;
                _undoStates.Clear();
                _suppressSelectionClear = true;
                try
                {
                    MailListView.SelectedItems.Clear();
                    _items.Remove(item);
                }
                finally
                {
                    _suppressSelectionClear = false;
                }
                ClearDetail();
                ApplyMailSearch();
                RefreshAllFolderCounts();
                SetMessageListStatus($"{account.DisplayTitle} · {string.Format(GetResourceStringOrDefault("TextNMailItems", "{0} messages"), _items.Count)}");
                MailUndoBar.Message = GetResourceStringOrDefault("TextMailMoveComplete", "Message moved.");
                MailUndoBar.IsOpen = true;
            }
            catch (OperationCanceledException) when (_pageRequestCts.IsCancellationRequested) { }
            catch (MailMoveOutcomeUnknownException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Outlook move outcome unknown: {ex.Message}");
                await ReconcileOutlookMoveAsync();
                SetMessageListStatus(GetResourceStringOrDefault("TextMailMoveOutcomeUnknown", "The move outcome is unknown. The folder was refreshed without retrying."), isError: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Outlook move failed: {ex.Message}");
                SetMessageListStatus(GetResourceStringOrDefault("TextMailMoveFailed", "Failed to move the message."), isError: true);
            }
            finally
            {
                UpdateProviderMoveCommands();
            }
        }

        private async Task MutateSelectedGmailMailAsync(
            Func<MailService, MailAccount, MailItem, CancellationToken, Task<GmailLabelMutationResult>> mutate,
            MailItem? expectedItem = null)
        {
            var service = _mailService;
            var account = _selectedAccount;
            var item = GetSingleSelectedOnlineActionItem();
            if (service == null || account?.Kind != MailAccountKind.Google || item == null ||
                _isOnlineMailAction || (expectedItem != null && !ReferenceEquals(expectedItem, item))) return;

            _isOnlineMailAction = true;
            SetProviderMoveCommandsEnabled(false);
            try
            {
                var result = await mutate(service, account, item, _pageRequestCts.Token);
                _gmailUndoState = new GmailUndoState(account, result);
                _moveUndoState = null;
                _imapUndoState = null;
                _undoStates.Clear();
                if (result.RemovedFromSource)
                {
                    _suppressSelectionClear = true;
                    try
                    {
                        MailListView.SelectedItems.Clear();
                        _items.Remove(item);
                    }
                    finally { _suppressSelectionClear = false; }
                    ClearDetail();
                    ApplyMailSearch();
                }
                RefreshAllFolderCounts();
                SetMessageListStatus($"{account.DisplayTitle} · {string.Format(GetResourceStringOrDefault("TextNMailItems", "{0} messages"), _items.Count)}");
                MailUndoBar.Message = GetResourceStringOrDefault("TextGmailActionComplete", "Gmail action completed.");
                MailUndoBar.IsOpen = true;
            }
            catch (OperationCanceledException) when (_pageRequestCts.IsCancellationRequested) { }
            catch (GmailActionOutcomeUnknownException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Gmail action outcome unknown: {ex.Message}");
                _gmailUndoState = null;
                await ReconcileOutlookMoveAsync();
                SetMessageListStatus(GetResourceStringOrDefault("TextGmailActionOutcomeUnknown", "The Gmail action outcome is unknown. Labels were refreshed without retrying."), isError: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Gmail action failed: {ex.Message}");
                SetMessageListStatus(GetResourceStringOrDefault("TextGmailActionFailed", "Failed to update the Gmail message."), isError: true);
            }
            finally
            {
                _isOnlineMailAction = false;
                UpdateProviderMoveCommands();
            }
        }

        private async Task ReconcileOutlookMoveAsync()
        {
            string? selectedFolderId = _selectedFolder?.Id;
            var accountNode = _accountNodes.FirstOrDefault(pair => ReferenceEquals(pair.Value, _selectedAccount)).Key;
            if (accountNode != null)
            {
                await LoadFoldersForNodeAsync(accountNode, forceRefresh: true);
                var selectedNode = accountNode.Children.FirstOrDefault(node =>
                    _folderNodes.TryGetValue(node, out var selection) && selection.Folder.Id == selectedFolderId);
                if (selectedNode != null)
                {
                    AccountTree.SelectedNode = selectedNode;
                    _selectedFolder = _folderNodes[selectedNode].Folder;
                }
            }
            if (_selectedFolder != null)
                await LoadMessagesAsync(forceRefresh: true, selectFirstWhenNoMatch: false);
        }

        private async Task MoveSelectedImapMailAsync(
            Func<MailService, MailAccount, MailItem, CancellationToken, Task<ImapMoveResult>> move,
            MailItem? expectedItem = null)
        {
            var service = _mailService;
            var account = _selectedAccount;
            var item = GetSingleSelectedOnlineActionItem();
            if (service == null || account?.Kind != MailAccountKind.Imap || item == null || _isOnlineMailAction ||
                (expectedItem != null && !ReferenceEquals(expectedItem, item))) return;

            _isOnlineMailAction = true;
            SetProviderMoveCommandsEnabled(false);
            try
            {
                var result = await move(service, account, item, _pageRequestCts.Token);
                if (!IsOnlineActionContextCurrent(account, item))
                {
                    _imapUndoState = null;
                    return;
                }
                _imapUndoState = new ImapUndoState(account, result);
                _moveUndoState = null;
                _gmailUndoState = null;
                _undoStates.Clear();
                _suppressSelectionClear = true;
                try
                {
                    MailListView.SelectedItems.Clear();
                    _items.Remove(item);
                }
                finally { _suppressSelectionClear = false; }
                ClearDetail();
                ApplyMailSearch();
                RefreshAllFolderCounts();
                MailUndoBar.Message = GetResourceStringOrDefault("TextImapMoveComplete", "Message moved.");
                MailUndoBar.IsOpen = true;
            }
            catch (OperationCanceledException) when (_pageRequestCts.IsCancellationRequested) { }
            catch (ImapMoveIdentityUnavailableException)
            {
                _imapUndoState = null;
                if (IsOnlineActionContextCurrent(account, item))
                {
                    await ReconcileOutlookMoveAsync();
                    SetMessageListStatus(GetResourceStringOrDefault("TextImapMoveIdentityUnavailable", "The message was moved, but undo is unavailable because the server did not return its new identity."), isError: true);
                }
            }
            catch (ImapMoveOutcomeUnknownException)
            {
                _imapUndoState = null;
                if (IsOnlineActionContextCurrent(account, item) && !_pageRequestCts.IsCancellationRequested)
                {
                    await ReconcileOutlookMoveAsync();
                    SetMessageListStatus(GetResourceStringOrDefault("TextImapMoveOutcomeUnknown", "The move outcome is unknown. Folders were refreshed without retrying."), isError: true);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"IMAP move failed: {ex.Message}");
                SetMessageListStatus(GetResourceStringOrDefault("TextImapMoveFailed", "Failed to move the IMAP message."), isError: true);
            }
            finally
            {
                _isOnlineMailAction = false;
                UpdateProviderMoveCommands();
            }
        }

        private bool IsOnlineActionContextCurrent(MailAccount account, MailItem item)
            => string.Equals(_selectedAccount?.Id, account.Id, StringComparison.Ordinal) &&
               string.Equals(_selectedFolder?.Id, item.FolderId, StringComparison.Ordinal) &&
               _items.Contains(item);

        private async Task MutateSelectedMailAsync(MailMutationKind kind, bool value, IReadOnlyList<MailUndoState>? restoreTargets = null)
        {
            var service = _mailService;
            var account = _selectedAccount;
            if (service == null || account == null || !MailMutationCapabilityPolicy.Supports(account.Kind, kind)) return;

            var selected = restoreTargets?.Select(state => state.Item).ToList()
                ?? MailBulkMutationPolicy.SelectBounded(MailListView.SelectedItems.Cast<MailItem>()).ToList();
            if (selected.Count == 0) return;

            var previous = selected.Select(item => new MailUndoState(
                item,
                kind == MailMutationKind.SetReadState ? item.IsRead : item.IsFlagged,
                _items.IndexOf(item))).ToList();
            var versions = new Dictionary<MailItem, long>();
            foreach (var item in selected)
            {
                long version = ++_nextMailIntentVersion;
                versions[item] = version;
                _mailIntentVersions[GetIntentKey(item, kind)] = version;
                service.ApplyCachedMutation(item, kind, value);
                var restoreState = restoreTargets?.FirstOrDefault(state => ReferenceEquals(state.Item, item));
                if (restoreState != null)
                    RestoreUnreadItemIfNeeded(restoreState, kind, value);
            }
            RefreshSelectedFolderCount();

            if (kind == MailMutationKind.SetReadState && value && UnreadOnlyToggle.IsOn)
            {
                _suppressSelectionClear = true;
                try { foreach (var item in selected) _items.Remove(item); }
                finally { _suppressSelectionClear = false; }
            }
            ApplyMailSearch();

            if (restoreTargets == null)
            {
                _moveUndoState = null;
                _gmailUndoState = null;
                _imapUndoState = null;
                _undoStates = previous;
                _undoKind = kind;
                MailUndoBar.Message = string.Format(GetResourceStringOrDefault("TextMailMutationComplete", "Updated {0} messages."), selected.Count);
                MailUndoBar.IsOpen = true;
            }

            using var gate = new SemaphoreSlim(MailBulkMutationPolicy.MaximumConcurrency);
            var tasks = selected.Select(async item =>
            {
                await gate.WaitAsync();
                try
                {
                    if (kind == MailMutationKind.SetReadState)
                        await service.SetReadStateAsync(account, item, value);
                    else
                        await service.SetFlaggedAsync(account, item, value);
                    return (Item: item, Error: (Exception?)null);
                }
                catch (MailMutationSyncQueuedException) { return (Item: item, Error: (Exception?)null); }
                catch (Exception ex) { return (Item: item, Error: ex); }
                finally { gate.Release(); }
            });
            var results = await Task.WhenAll(tasks);

            foreach (var result in results.Where(result => result.Error != null))
            {
                var prior = previous.First(state => ReferenceEquals(state.Item, result.Item));
                if (!MailBulkMutationPolicy.ShouldRollback(versions[result.Item], GetCurrentIntent(result.Item, kind))) continue;
                service.ApplyCachedMutation(result.Item, kind, prior.PreviousValue);
                RestoreUnreadItemIfNeeded(prior, kind, prior.PreviousValue);
            }
            RefreshSelectedFolderCount();

            if (results.Any(result => result.Error != null))
                SetMessageListStatus(GetResourceStringOrDefault("TextMailMutationFailed", "Some messages could not be updated."), isError: true);
        }

        private async void UndoMailMutationButton_Click(object sender, RoutedEventArgs e)
        {
            var imapState = _imapUndoState;
            if (imapState != null)
            {
                if (_isOnlineMailAction) return;
                _imapUndoState = null;
                MailUndoBar.IsOpen = false;
                _isOnlineMailAction = true;
                SetProviderMoveCommandsEnabled(false);
                try
                {
                    await _mailService!.UndoImapMoveAsync(imapState.Account, imapState.Result, _pageRequestCts.Token);
                    if (string.Equals(_selectedAccount?.Id, imapState.Account.Id, StringComparison.Ordinal))
                        await ReconcileOutlookMoveAsync();
                    SetMessageListStatus(GetResourceStringOrDefault("TextImapUndoComplete", "IMAP move undone."));
                }
                catch (ImapMoveIdentityUnavailableException)
                {
                    if (string.Equals(_selectedAccount?.Id, imapState.Account.Id, StringComparison.Ordinal))
                        await ReconcileOutlookMoveAsync();
                    SetMessageListStatus(GetResourceStringOrDefault("TextImapUndoIdentityUnavailable", "The message was moved back, but the server did not return its new identity."), isError: true);
                }
                catch (ImapMoveOutcomeUnknownException)
                {
                    if (string.Equals(_selectedAccount?.Id, imapState.Account.Id, StringComparison.Ordinal) && !_pageRequestCts.IsCancellationRequested)
                        await ReconcileOutlookMoveAsync();
                    SetMessageListStatus(GetResourceStringOrDefault("TextImapUndoOutcomeUnknown", "The undo outcome is unknown. Folders were refreshed without retrying."), isError: true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Undo IMAP move failed: {ex.Message}");
                    SetMessageListStatus(GetResourceStringOrDefault("TextImapUndoFailed", "Failed to undo the IMAP move."), isError: true);
                }
                finally
                {
                    _isOnlineMailAction = false;
                    UpdateProviderMoveCommands();
                }
                return;
            }

            var gmailState = _gmailUndoState;
            if (gmailState != null)
            {
                _gmailUndoState = null;
                MailUndoBar.IsOpen = false;
                try
                {
                    await _mailService!.UndoGmailLabelMutationAsync(gmailState.Account, gmailState.Result, _pageRequestCts.Token);
                    await ReconcileOutlookMoveAsync();
                    SetMessageListStatus(GetResourceStringOrDefault("TextGmailUndoComplete", "Gmail action undone."));
                }
                catch (GmailActionOutcomeUnknownException)
                {
                    await ReconcileOutlookMoveAsync();
                    SetMessageListStatus(GetResourceStringOrDefault("TextGmailUndoOutcomeUnknown", "The Gmail undo outcome is unknown. Labels were refreshed without retrying."), isError: true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Undo Gmail action failed: {ex.Message}");
                    SetMessageListStatus(GetResourceStringOrDefault("TextGmailUndoFailed", "Failed to undo the Gmail action."), isError: true);
                }
                return;
            }

            var moveState = _moveUndoState;
            if (moveState != null)
            {
                _moveUndoState = null;
                MailUndoBar.IsOpen = false;
                try
                {
                    await _mailService!.MoveOutlookMessageAsync(moveState.Account, moveState.MovedItem, moveState.ReturnFolderId, _pageRequestCts.Token);
                    await ReconcileOutlookMoveAsync();
                    SetMessageListStatus(GetResourceStringOrDefault("TextMailMoveUndone", "Move undone."));
                }
                catch (MailMoveOutcomeUnknownException)
                {
                    await ReconcileOutlookMoveAsync();
                    SetMessageListStatus(GetResourceStringOrDefault("TextMailMoveUndoOutcomeUnknown", "The undo outcome is unknown. The folder was refreshed without retrying."), isError: true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Undo Outlook move failed: {ex.Message}");
                    SetMessageListStatus(GetResourceStringOrDefault("TextMailMoveUndoFailed", "Failed to undo the move."), isError: true);
                }
                return;
            }

            var states = _undoStates;
            MailUndoBar.IsOpen = false;
            _undoStates = new();
            foreach (var group in states.GroupBy(state => state.PreviousValue))
                await MutateSelectedMailAsync(_undoKind, group.Key, group.ToList());
        }

        private void RestoreUnreadItemIfNeeded(MailUndoState state, MailMutationKind kind, bool value)
        {
            if (kind != MailMutationKind.SetReadState || value || !UnreadOnlyToggle.IsOn || _items.Contains(state.Item)) return;
            _items.Insert(Math.Clamp(state.PreviousIndex, 0, _items.Count), state.Item);
            ApplyMailSearch();
        }

        private static string GetIntentKey(MailItem item, MailMutationKind kind)
            => $"{item.AccountId}\u001f{item.FolderId}\u001f{item.Id}\u001f{(int)kind}";

        private long GetCurrentIntent(MailItem item, MailMutationKind kind)
            => _mailIntentVersions.TryGetValue(GetIntentKey(item, kind), out var version) ? version : 0;

        private void RefreshSelectedFolderCount()
        {
            if (_selectedFolder == null) return;
            foreach (var pair in _folderNodes.Where(pair => ReferenceEquals(pair.Value.Folder, _selectedFolder)))
                pair.Key.Content = FormatFolderContent(_selectedFolder);
        }

        private void RefreshAllFolderCounts()
        {
            foreach (var pair in _folderNodes)
                pair.Key.Content = FormatFolderContent(pair.Value.Folder);
        }

        public async Task OpenCachedMessageAsync(string accountId, string folderId, string messageId)
        {
            if (!IsLoaded)
                await WaitUntilLoadedAsync();

            _mailService ??= (App.Current as App)?.MailService;
            var target = _mailService?.TryGetCachedMessage(accountId, folderId, messageId);
            if (target == null)
            {
                SetMessageListStatus(_loader.GetStringOrDefault("TextMailNotFound") ?? "This message is no longer in the local cache.", isError: true);
                return;
            }

            _items.Clear();
            _displayedItems.Clear();
            _items.Add(target);
            _displayedItems.Add(target);
            MailListView.ItemsSource = _displayedItems;
            _openingCachedMetadataOnly = true;
            MailListView.SelectedItem = target;
            MailListView.ScrollIntoView(target);
            if (!ReferenceEquals(target, _selectedItem))
                await OpenMailItemAsync(target);
            SetMessageListStatus(_loader.GetStringOrDefault("GlobalSearch_CachedMailContext") ?? "Opened from the local mail cache");
        }

        private void CleanupRenderedMailContent(bool clearAllBodies)
        {
            ClearRenderedMailBody();
            if (clearAllBodies)
                ReleaseMessageBodies();
            _isInternalMailHtmlNavigation = false;
        }

        private void ClearRenderedMailBody()
        {
            RemoteImageBanner.Visibility = Visibility.Collapsed;
            DetailHtmlViewHost.Visibility = Visibility.Collapsed;
            DetailTextScrollViewer.Visibility = Visibility.Visible;
            DetailPreview.Text = "";
            ReleaseMailWebView();
        }

        private bool _webView2Configured;
        private bool _isInternalMailHtmlNavigation;
        private WebView2? _detailHtmlView;
        private CancellationTokenSource? _mailResourceCts;
        private PerformanceDiagnostics.Span? _mailNavigationSpan;
        private ulong _mailNavigationId;

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
                        var environmentSpan = PerformanceDiagnostics.StartSpanUntilSuccess(
                            "webview2.environment.init", "webview2", "first_environment_init", "runtime");
                        try
                        {
                            await htmlView.EnsureCoreWebView2Async();
                            environmentSpan.Complete();
                        }
                        catch
                        {
                            environmentSpan.Complete("failure");
                            throw;
                        }
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
                        _mailNavigationSpan = PerformanceDiagnostics.StartSpanUntilSuccess(
                            "mail.html.render", "mail", "first_html_navigation_render", "webview2");
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
            {
                _mailNavigationId = args.NavigationId;
                return;
            }

            if (!args.IsRedirected && args.Uri != "about:blank")
            {
                args.Cancel = true;
                OpenSafeExternalUri(args.Uri);
            }
        }

        private void MailHtml_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (args.NavigationId != _mailNavigationId) return;

            _isInternalMailHtmlNavigation = false;
            _mailNavigationId = 0;
            var span = _mailNavigationSpan;
            _mailNavigationSpan = null;
            if (args.IsSuccess)
                NextRenderHelper.RunOnce(() => span?.Complete());
            else
                span?.Complete("failure");
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

                    var fetched = await RemoteImageProxyService.Instance.FetchTrustedMailAsync(uri!, cts.Token);
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
            ReleaseMailWebView();
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
                SetComposeStatus(GetAttachmentValidationMessage(attachmentError.Value));
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

        private string GetAttachmentValidationMessage(MailAttachmentValidationError error)
            => error switch
            {
                MailAttachmentValidationError.TooMany => _loader.GetStringOrDefault("TextAttachmentCountLimit") ?? "You can attach up to 10 files.",
                MailAttachmentValidationError.Empty => _loader.GetStringOrDefault("TextAttachmentEmpty") ?? "Empty attachments are not supported.",
                MailAttachmentValidationError.FileTooLarge => _loader.GetStringOrDefault("TextAttachmentFileLimit") ?? "Attachments may be up to 3 MB each.",
                MailAttachmentValidationError.InvalidFileName => _loader.GetStringOrDefault("TextAttachmentInvalidFileName") ?? "An attachment has an invalid file name.",
                _ => _loader.GetStringOrDefault("TextAttachmentTotalLimit") ?? "Attachments may total up to 10 MB."
            };

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
