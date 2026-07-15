using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Input;
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

namespace Task_Flyout.Views
{
    public sealed partial class RssPage : Page
    {
        private const int PageSize = 20;

        public ObservableCollection<RssSubscription> Subscriptions { get; } = new();
        public ObservableCollection<RssFolder> Folders { get; } = new();
        public ObservableCollection<RssArticle> Articles { get; } = new();

        private readonly RssService _rssService = new();
        private readonly ResourceLoader _loader = new();
        private readonly Dictionary<TreeViewNode, RssFolder> _folderNodes = new();
        private readonly Dictionary<TreeViewNode, RssSubscription> _subscriptionNodes = new();
        private TreeViewNode? _allNode;
        private string? _selectedSubscriptionId;
        private string? _selectedFolderId;
        private int _loadedCount;
        private bool _isLoading;
        private bool _isRefreshing;
        private bool _hasMore = true;
        private ScrollViewer? _articleScrollViewer;
        private RssArticle? _selectedArticle;
        private bool _rssWebViewConfigured;
        private bool _isInternalArticleNavigation;
        private WebView2? _rssArticleWebView;
        private CancellationTokenSource? _rssResourceCts;
        private DateTimeOffset? _lastArticleLoadSucceededAt;
        private int _pageGeneration;
        private CancellationTokenSource? _pageLifetimeCts;

        public RssPage()
        {
            this.Language = Windows.Globalization.ApplicationLanguages.Languages[0];
            InitializeComponent();
            Loaded += RssPage_Loaded;
            Unloaded += (_, _) => DisposeLikeCleanup();
            ActualThemeChanged += RssPage_ActualThemeChanged;
        }

        public void DisposeLikeCleanup()
        {
            _pageGeneration++;
            _pageLifetimeCts?.Cancel();
            _pageLifetimeCts?.Dispose();
            _pageLifetimeCts = null;
            RssService.LocalDataCleared -= RssService_LocalDataCleared;
            _selectedArticle = null;
            CancelRssResourceRequests();
            _selectedSubscriptionId = null;
            _selectedFolderId = null;
            _loadedCount = 0;
            _hasMore = true;
            _isLoading = false;

            if (_articleScrollViewer != null)
            {
                _articleScrollViewer.ViewChanged -= ArticleScrollViewer_ViewChanged;
                _articleScrollViewer = null;
            }

            ReleaseRssWebView();
            _isInternalArticleNavigation = false;
            Articles.Clear();
            Subscriptions.Clear();
            Folders.Clear();
            _folderNodes.Clear();
            _subscriptionNodes.Clear();
            _allNode = null;
            RssTree?.RootNodes.Clear();
        }

        private async void RssPage_Loaded(object sender, RoutedEventArgs e)
        {
            int generation = ++_pageGeneration;
            _pageLifetimeCts?.Cancel();
            _pageLifetimeCts?.Dispose();
            _pageLifetimeCts = new CancellationTokenSource();
            RssService.LocalDataCleared -= RssService_LocalDataCleared;
            RssService.LocalDataCleared += RssService_LocalDataCleared;
            try
            {
                SetLoading(true);
                await _rssService.InitializeAsync();
                if (generation != _pageGeneration || !IsLoaded) return;

                LoadFolders();
                LoadSubscriptions();
                BuildSubscriptionTree();
                AttachArticleScrollViewer();
                await ResetAndLoadArticlesAsync(generation);
            }
            catch (Exception ex)
            {
                if (generation != _pageGeneration || !IsLoaded) return;
                LogRssError(ex);
                ArticleListSubtitle.Text = string.Format(_loader.GetStringOrDefault("TextRssInitFailed") ?? "RSS initialization failed: {0}", UserSafeErrorMessage.FromException(ex));
            }
            finally
            {
                if (generation == _pageGeneration && IsLoaded)
                    SetLoading(false);
            }
        }

        private void LoadSubscriptions()
        {
            Subscriptions.Clear();
            foreach (var subscription in _rssService.GetSubscriptions())
                Subscriptions.Add(subscription);

            UpdateSubscriptionStatus();
        }

        private void LoadFolders()
        {
            Folders.Clear();
            foreach (var folder in _rssService.GetFolders())
                Folders.Add(folder);
            UpdateSubscriptionStatus();
        }

        private void BuildSubscriptionTree()
        {
            if (RssTree == null) return;

            RssTree.RootNodes.Clear();
            _folderNodes.Clear();
            _subscriptionNodes.Clear();

            _allNode = new TreeViewNode
            {
                Content = _loader.GetStringOrDefault("TextAllArticles") ?? "All Articles",
                IsExpanded = true
            };
            RssTree.RootNodes.Add(_allNode);

            foreach (var folder in _rssService.GetFolders())
            {
                var folderNode = new TreeViewNode
                {
                    Content = CreateFolderNodeContent(folder),
                    IsExpanded = true
                };
                RssTree.RootNodes.Add(folderNode);
                _folderNodes[folderNode] = folder;

                foreach (var subscription in _rssService.GetSubscriptions().Where(item => item.FolderId == folder.Id))
                    AddSubscriptionNode(folderNode, subscription);
            }

            var uncategorizedNode = new TreeViewNode
            {
                Content = _loader.GetStringOrDefault("TextUncategorized") ?? "Uncategorized",
                IsExpanded = true
            };
            RssTree.RootNodes.Add(uncategorizedNode);
            foreach (var subscription in _rssService.GetSubscriptions().Where(item => string.IsNullOrWhiteSpace(item.FolderId)))
                AddSubscriptionNode(uncategorizedNode, subscription);
        }

        private string CreateFolderNodeContent(RssFolder folder)
            => string.IsNullOrWhiteSpace(folder.Name) ? (_loader.GetStringOrDefault("TextFolder") ?? "Folder") : folder.Name;

        private void AddSubscriptionNode(TreeViewNode parent, RssSubscription subscription)
        {
            var node = new TreeViewNode
            {
                Content = CreateSubscriptionNodeContent(subscription),
                HasUnrealizedChildren = false
            };
            parent.Children.Add(node);
            _subscriptionNodes[node] = subscription;
        }

        private static string CreateSubscriptionNodeContent(RssSubscription subscription)
            => string.IsNullOrWhiteSpace(subscription.Title) ? subscription.Url : subscription.Title;

        private async void RssPage_ActualThemeChanged(FrameworkElement sender, object args)
        {
            if (_selectedArticle != null && ArticleReaderPanel.Visibility == Visibility.Visible)
                await RenderArticleAsync(_selectedArticle);
        }

        private void UpdateSubscriptionStatus()
        {
            if (SubscriptionStatusText == null) return;
            SubscriptionStatusText.Text = Subscriptions.Count == 0
                ? (_loader.GetStringOrDefault("TextNoSubscriptionsHint") ?? "No subscriptions yet. Add an RSS URL to start reading.")
                : string.Format(_loader.GetStringOrDefault("TextNSubscriptions") ?? "{0} subscriptions, {1} folders", Subscriptions.Count, Folders.Count);
        }

        private async Task ResetAndLoadArticlesAsync(int? pageGeneration = null)
        {
            Articles.Clear();
            _loadedCount = 0;
            _hasMore = true;
            await LoadMoreArticlesAsync(pageGeneration);
        }

        private void ResetAndLoadCachedArticles()
        {
            Articles.Clear();
            _loadedCount = 0;
            _hasMore = true;

            try
            {
                var items = _rssService.GetCachedArticlesPage(_selectedSubscriptionId, _selectedFolderId, 0, PageSize);
                foreach (var item in items)
                    Articles.Add(item);

                _loadedCount = items.Count;
                _hasMore = items.Count == PageSize;
                SetArticleListStatus(Articles.Count == 0
                    ? (_loader.GetStringOrDefault("TextNoCachedArticles") ?? "No cached articles, click refresh to fetch")
                    : _hasMore ? string.Format(_loader.GetStringOrDefault("TextNArticlesScroll") ?? "{0} articles, scroll down to load more", Articles.Count) : string.Format(_loader.GetStringOrDefault("TextNArticles") ?? "{0} articles", Articles.Count));
            }
            catch (Exception ex)
            {
                LogRssError(ex);
                SetArticleListStatus(string.Format(_loader.GetStringOrDefault("TextCacheLoadFailed") ?? "Cache load failed: {0}", UserSafeErrorMessage.FromException(ex)), isError: true);
            }
        }

        private async Task LoadMoreArticlesAsync(int? pageGeneration = null)
        {
            if (_isLoading || !_hasMore) return;

            try
            {
                _isLoading = true;
                SetLoading(true);
                var items = await _rssService.LoadMoreArticlesAsync(_selectedSubscriptionId, _selectedFolderId, _loadedCount, PageSize);
                if (pageGeneration.HasValue && (pageGeneration.Value != _pageGeneration || !IsLoaded))
                    return;

                foreach (var item in items)
                    Articles.Add(item);

                _loadedCount += items.Count;
                _hasMore = items.Count == PageSize;
                _lastArticleLoadSucceededAt = DateTimeOffset.Now;
                SetArticleListStatus(Articles.Count == 0
                    ? (_loader.GetStringOrDefault("TextNoArticles") ?? "No articles")
                    : _hasMore ? string.Format(_loader.GetStringOrDefault("TextNArticlesClickLoad") ?? "{0} articles, click to load more", Articles.Count) : string.Format(_loader.GetStringOrDefault("TextNArticles") ?? "{0} articles", Articles.Count));
            }
            catch (Exception ex)
            {
                if (pageGeneration.HasValue && (pageGeneration.Value != _pageGeneration || !IsLoaded))
                    return;

                LogRssError(ex);
                SetArticleListStatus(string.Format(_loader.GetStringOrDefault("TextLoadFailed") ?? "Load failed: {0}", UserSafeErrorMessage.FromException(ex)), isError: true);
            }
            finally
            {
                if (!pageGeneration.HasValue || (pageGeneration.Value == _pageGeneration && IsLoaded))
                    SetLoading(false);
                _isLoading = false;
            }
        }

        private void SetLoading(bool isLoading)
        {
            LoadingRing.IsActive = isLoading;
            LoadingRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            if (RefreshButton != null) RefreshButton.IsEnabled = !isLoading;
            if (AddSubscriptionButton != null) AddSubscriptionButton.IsEnabled = !isLoading;
            if (AddFolderButton != null) AddFolderButton.IsEnabled = !isLoading;
        }

        private async void RssTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            if (args.InvokedItem is not TreeViewNode node) return;

            if (node == _allNode)
            {
                ShowArticleList();
                _selectedSubscriptionId = null;
                _selectedFolderId = null;
                ArticleListTitle.Text = _loader.GetStringOrDefault("TextAllArticles") ?? "All Articles";
                SubtitleText.Text = _loader.GetStringOrDefault("TextAllSubscriptions") ?? "All subscriptions";
            }
            else if (_folderNodes.TryGetValue(node, out var folder))
            {
                ShowArticleList();
                _selectedSubscriptionId = null;
                _selectedFolderId = folder.Id;
                ArticleListTitle.Text = folder.Name;
                SubtitleText.Text = _loader.GetStringOrDefault("TextCurrentFolderArticles") ?? "RSS articles in current folder";
            }
            else if (_subscriptionNodes.TryGetValue(node, out var subscription))
            {
                ShowArticleList();
                _selectedSubscriptionId = subscription.Id;
                _selectedFolderId = null;
                ArticleListTitle.Text = subscription.Title;
                SubtitleText.Text = subscription.Url;
            }
            else if (node.Content?.ToString() == (_loader.GetStringOrDefault("TextUncategorized") ?? "Uncategorized"))
            {
                ShowArticleList();
                _selectedSubscriptionId = null;
                _selectedFolderId = "";
                ArticleListTitle.Text = _loader.GetStringOrDefault("TextUncategorized") ?? "Uncategorized";
                SubtitleText.Text = _loader.GetStringOrDefault("TextUncategorizedArticles") ?? "Uncategorized subscription articles";
            }
            else
            {
                return;
            }

            ResetAndLoadCachedArticles();
        }

        private async void AddSubscriptionButton_Click(object sender, RoutedEventArgs e)
        {
            var urlBox = new TextBox
            {
                Header = _loader.GetStringOrDefault("TextRssAddress") ?? "RSS URL",
                PlaceholderText = "https://example.com/feed.xml"
            };
            var folderBox = CreateFolderComboBox(_selectedFolderId);
            var insecureHttpCheckBox = new CheckBox
            {
                Content = _loader.GetStringOrDefault("TextAllowInsecureHttpRss") ?? "Allow unencrypted HTTP for this subscription",
                IsChecked = false
            };
            var insecureHttpWarning = new TextBlock
            {
                Text = _loader.GetStringOrDefault("TextInsecureHttpRssWarning") ?? "HTTP feeds can be read or modified by other devices on the network. HTTPS is recommended.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            };
            var localNetworkCheckBox = new CheckBox
            {
                Content = _loader.GetStringOrDefault("TextAllowLocalNetworkRss") ?? "Allow this subscription to access its exact local network host and port",
                IsChecked = false
            };
            var localNetworkWarning = new TextBlock
            {
                Text = _loader.GetStringOrDefault("TextLocalNetworkRssWarning") ?? "Enable only for a trusted feed on your home or work network. Redirects to a different local host or port remain blocked.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            };
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(urlBox);
            panel.Children.Add(folderBox);
            panel.Children.Add(insecureHttpCheckBox);
            panel.Children.Add(insecureHttpWarning);
            panel.Children.Add(localNetworkCheckBox);
            panel.Children.Add(localNetworkWarning);

            var dialog = new ContentDialog
            {
                Title = _loader.GetStringOrDefault("TextAddRssSubscription") ?? "Add RSS Subscription",
                Content = panel,
                PrimaryButtonText = _loader.GetStringOrDefault("TextAdd") ?? "Add",
                CloseButtonText = _loader.GetStringOrDefault("CalendarDialog.CloseButtonText") ?? "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            dialog.PrimaryButtonClick += async (_, args) =>
            {
                var url = urlBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(url))
                {
                    args.Cancel = true;
                    urlBox.Focus(FocusState.Programmatic);
                    return;
                }

                var deferral = args.GetDeferral();
                try
                {
                    SetLoading(true);
                    var folderId = (folderBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
                    await _rssService.AddSubscriptionAsync(
                        url,
                        folderId,
                        insecureHttpCheckBox.IsChecked == true,
                        localNetworkCheckBox.IsChecked == true);
                    LoadSubscriptions();
                    LoadFolders();
                    BuildSubscriptionTree();
                    _selectedSubscriptionId = null;
                    _selectedFolderId = null;
                    ArticleListTitle.Text = _loader.GetStringOrDefault("TextAllArticles") ?? "All Articles";
                    SubtitleText.Text = _loader.GetStringOrDefault("TextAllSubscriptions") ?? "All subscriptions";
                    ResetAndLoadCachedArticles();
                }
                catch (Exception ex)
                {
                    args.Cancel = true;
                    SubscriptionStatusText.Text = string.Format(_loader.GetStringOrDefault("TextAddFailed2") ?? "Add failed: {0}", UserSafeErrorMessage.FromException(ex));
                }
                finally
                {
                    SetLoading(false);
                    deferral.Complete();
                }
            };

            await dialog.ShowAsync();
        }

        private async void DeleteSubscriptionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not RssSubscription subscription) return;
            await DeleteSubscriptionAsync(subscription);
        }

        private async Task DeleteSubscriptionAsync(RssSubscription subscription)
        {
            var dialog = new ContentDialog
            {
                Title = _loader.GetStringOrDefault("TextDeleteSubscription") ?? "Delete Subscription",
                Content = string.Format(_loader.GetStringOrDefault("TextDeleteSubscriptionContent") ?? "Delete \"{0}\"? Cached articles will also be removed.", subscription.Title),
                PrimaryButtonText = _loader.GetStringOrDefault("CalendarDialog.SecondaryButtonText") ?? "Delete",
                CloseButtonText = _loader.GetStringOrDefault("CalendarDialog.CloseButtonText") ?? "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            _rssService.RemoveSubscription(subscription.Id);
            if (_selectedSubscriptionId == subscription.Id)
            {
                _selectedSubscriptionId = null;
                ArticleListTitle.Text = _loader.GetStringOrDefault("TextAllArticles") ?? "All Articles";
                SubtitleText.Text = _loader.GetStringOrDefault("TextAllSubscriptions") ?? "All subscriptions";
            }
            LoadSubscriptions();
            BuildSubscriptionTree();
            ResetAndLoadCachedArticles();
        }

        private async void DeleteRssTreeItem_Click(object sender, RoutedEventArgs e)
        {
            var node = RssTree.SelectedNode;
            if (node == null || node == _allNode) return;

            if (_subscriptionNodes.TryGetValue(node, out var subscription))
            {
                await DeleteSubscriptionAsync(subscription);
                return;
            }

            if (_folderNodes.TryGetValue(node, out var folder))
                await DeleteFolderAsync(folder);
        }

        private async void RenameRssTreeItem_Click(object sender, RoutedEventArgs e)
        {
            var node = RssTree.SelectedNode;
            if (node == null || node == _allNode) return;

            if (_folderNodes.TryGetValue(node, out var folder))
            {
                await RenameFolderAsync(folder);
                return;
            }

            if (_subscriptionNodes.TryGetValue(node, out var subscription))
                await RenameSubscriptionAsync(subscription);
        }

        private async Task RenameFolderAsync(RssFolder folder)
        {
            var box = new TextBox { Header = _loader.GetStringOrDefault("TextFolderName") ?? "Folder name", Text = folder.Name };
            var dialog = new ContentDialog
            {
                Title = _loader.GetStringOrDefault("TextRenameFolder") ?? "Rename Folder",
                Content = box,
                PrimaryButtonText = _loader.GetStringOrDefault("CalendarDialog.PrimaryButtonText") ?? "Save",
                CloseButtonText = _loader.GetStringOrDefault("CalendarDialog.CloseButtonText") ?? "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            _rssService.RenameFolder(folder.Id, box.Text);
            LoadFolders();
            LoadSubscriptions();
            BuildSubscriptionTree();
            ResetAndLoadCachedArticles();
        }

        private async Task RenameSubscriptionAsync(RssSubscription subscription)
        {
            var box = new TextBox { Header = _loader.GetStringOrDefault("TextSubscriptionName") ?? "Subscription name", Text = subscription.Title };
            var dialog = new ContentDialog
            {
                Title = _loader.GetStringOrDefault("TextRenameSubscription") ?? "Rename Subscription",
                Content = box,
                PrimaryButtonText = _loader.GetStringOrDefault("CalendarDialog.PrimaryButtonText") ?? "Save",
                CloseButtonText = _loader.GetStringOrDefault("CalendarDialog.CloseButtonText") ?? "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            _rssService.RenameSubscription(subscription.Id, box.Text);
            LoadSubscriptions();
            BuildSubscriptionTree();
            ResetAndLoadCachedArticles();
        }

        private async void AddFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var nameBox = new TextBox
            {
                Header = _loader.GetStringOrDefault("TextFolderName") ?? "Folder name",
                PlaceholderText = _loader.GetStringOrDefault("TextFolderNamePlaceholder") ?? "e.g. Tech, News, Blog"
            };
            var dialog = new ContentDialog
            {
                Title = _loader.GetStringOrDefault("TextAddRssFolder") ?? "Add RSS Folder",
                Content = nameBox,
                PrimaryButtonText = _loader.GetStringOrDefault("TextAdd") ?? "Add",
                CloseButtonText = _loader.GetStringOrDefault("CalendarDialog.CloseButtonText") ?? "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            dialog.PrimaryButtonClick += (_, args) =>
            {
                if (string.IsNullOrWhiteSpace(nameBox.Text))
                {
                    args.Cancel = true;
                    nameBox.Focus(FocusState.Programmatic);
                    return;
                }

                _rssService.AddFolder(nameBox.Text);
                LoadFolders();
                LoadSubscriptions();
                BuildSubscriptionTree();
            };

            await dialog.ShowAsync();
        }

        private async void DeleteFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not RssFolder folder) return;
            await DeleteFolderAsync(folder);
        }

        private async Task DeleteFolderAsync(RssFolder folder)
        {
            var dialog = new ContentDialog
            {
                Title = _loader.GetStringOrDefault("TextDeleteFolder") ?? "Delete Folder",
                Content = string.Format(_loader.GetStringOrDefault("TextDeleteFolderContent") ?? "Delete \"{0}\"? Subscriptions will be moved to Uncategorized.", folder.Name),
                PrimaryButtonText = _loader.GetStringOrDefault("CalendarDialog.SecondaryButtonText") ?? "Delete",
                CloseButtonText = _loader.GetStringOrDefault("CalendarDialog.CloseButtonText") ?? "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            _rssService.RemoveFolder(folder.Id);
            if (_selectedFolderId == folder.Id)
            {
                _selectedFolderId = null;
                ArticleListTitle.Text = _loader.GetStringOrDefault("TextAllArticles") ?? "All Articles";
                SubtitleText.Text = _loader.GetStringOrDefault("TextAllSubscriptions") ?? "All subscriptions";
            }
            LoadFolders();
            LoadSubscriptions();
            BuildSubscriptionTree();
            ResetAndLoadCachedArticles();
        }

        private async void MoveSubscriptionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not RssSubscription subscription) return;

            var folderBox = CreateFolderComboBox(subscription.FolderId);
            var dialog = new ContentDialog
            {
                Title = string.Format(_loader.GetStringOrDefault("TextSetFolder") ?? "Set folder for \"{0}\"", subscription.Title),
                Content = folderBox,
                PrimaryButtonText = _loader.GetStringOrDefault("CalendarDialog.PrimaryButtonText") ?? "Save",
                CloseButtonText = _loader.GetStringOrDefault("CalendarDialog.CloseButtonText") ?? "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            var folderId = (folderBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
            _rssService.MoveSubscriptionToFolder(subscription.Id, folderId);
            LoadSubscriptions();
            BuildSubscriptionTree();
            ResetAndLoadCachedArticles();
        }

        private void RssTree_DragItemsCompleted(TreeView sender, TreeViewDragItemsCompletedEventArgs args)
        {
            SaveTreeLayout();
            LoadFolders();
            LoadSubscriptions();
            BuildSubscriptionTree();
        }

        private void RssTree_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject source) return;
            var item = FindAncestor<TreeViewItem>(source);
            if (item?.DataContext is TreeViewNode node)
            {
                RssTree.SelectedNode = node;
                return;
            }

            if (source is FrameworkElement element && element.DataContext is TreeViewNode contextNode)
                RssTree.SelectedNode = contextNode;
        }

        private void RssTreeMenu_Opening(object sender, object e)
        {
            RenameRssTreeItem.Visibility = Visibility.Visible;
            DeleteRssTreeItem.Visibility = Visibility.Visible;
            var node = RssTree.SelectedNode;
            var canEdit = node != null && node != _allNode && (_folderNodes.ContainsKey(node) || _subscriptionNodes.ContainsKey(node));
            RenameRssTreeItem.IsEnabled = canEdit;
            DeleteRssTreeItem.IsEnabled = canEdit;
        }

        private void SaveTreeLayout()
        {
            var folderOrder = RssTree.RootNodes
                .Where(node => _folderNodes.ContainsKey(node))
                .Select(node => _folderNodes[node].Id)
                .ToList();
            _rssService.SaveFolderOrder(folderOrder);

            foreach (var folderNode in RssTree.RootNodes.Where(node => _folderNodes.ContainsKey(node)))
            {
                var folder = _folderNodes[folderNode];
                foreach (var child in folderNode.Children.Where(node => _subscriptionNodes.ContainsKey(node)))
                    _rssService.MoveSubscriptionToFolder(_subscriptionNodes[child].Id, folder.Id);
            }

            foreach (var root in RssTree.RootNodes.Where(node => node.Content?.ToString() == (_loader.GetStringOrDefault("TextUncategorized") ?? "Uncategorized")))
            {
                foreach (var child in root.Children.Where(node => _subscriptionNodes.ContainsKey(node)))
                    _rssService.MoveSubscriptionToFolder(_subscriptionNodes[child].Id, "");
            }
        }


        private ComboBox CreateFolderComboBox(string? selectedFolderId)
        {
            var folderBox = new ComboBox
            {
                Header = _loader.GetStringOrDefault("TextFolder") ?? "Folder",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            folderBox.Items.Add(new ComboBoxItem { Content = _loader.GetStringOrDefault("TextUncategorized") ?? "Uncategorized", Tag = "" });
            foreach (var folder in _rssService.GetFolders())
                folderBox.Items.Add(new ComboBoxItem { Content = folder.Name, Tag = folder.Id });

            var selectedItem = folderBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString() ?? "", selectedFolderId ?? "", StringComparison.Ordinal));
            folderBox.SelectedItem = selectedItem ?? folderBox.Items[0];
            return folderBox;
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRefreshing || _isLoading) return;
            var pageLifetime = _pageLifetimeCts;
            if (pageLifetime == null) return;

            try
            {
                _isRefreshing = true;
                SetLoading(true);
                if (_selectedSubscriptionId == null)
                {
                    IEnumerable<RssSubscription> visibleSubscriptions = _rssService.GetSubscriptions();
                    if (_selectedFolderId != null)
                        visibleSubscriptions = visibleSubscriptions.Where(subscription => subscription.FolderId == _selectedFolderId);

                    foreach (var subscription in visibleSubscriptions
                                 .Where(subscription => !RssService.RequiresInsecureHttpApproval(subscription))
                                 .Take(10))
                    {
                        if (await RssService.RequiresLocalNetworkApprovalAsync(subscription)) continue;
                        await _rssService.RefreshSubscriptionAsync(subscription, force: true, pageLifetime.Token);
                    }
                }
                else if (_rssService.GetSubscriptions().FirstOrDefault(item => item.Id == _selectedSubscriptionId) is { } subscription)
                {
                    if (RssService.RequiresInsecureHttpApproval(subscription) && !await ConfirmInsecureHttpAsync(subscription))
                        return;
                    if (await RssService.RequiresLocalNetworkApprovalAsync(subscription) && !await ConfirmLocalNetworkAsync(subscription))
                        return;
                    await _rssService.RefreshSubscriptionAsync(subscription, force: true, pageLifetime.Token);
                }

                if (pageLifetime.IsCancellationRequested || !IsLoaded) return;
                LoadSubscriptions();
                ResetAndLoadCachedArticles();
                _lastArticleLoadSucceededAt = DateTimeOffset.Now;
                SetArticleListStatus(ArticleListSubtitle.Text);
            }
            catch (OperationCanceledException) when (pageLifetime.IsCancellationRequested)
            {
                // The page was unloaded while its refresh was still in flight.
            }
            catch (OperationCanceledException)
            {
                // Clearing RSS data cancels any refresh that still belongs to the old cache.
            }
            catch (Exception ex)
            {
                LogRssError(ex);
                SetArticleListStatus(FormatRssRefreshError(ex), isError: true);
            }
            finally
            {
                if (!pageLifetime.IsCancellationRequested && IsLoaded)
                    SetLoading(false);
                _isRefreshing = false;
            }
        }

        private async Task<bool> ConfirmInsecureHttpAsync(RssSubscription subscription)
        {
            var dialog = new ContentDialog
            {
                Title = _loader.GetStringOrDefault("TextInsecureHttpRssTitle") ?? "Allow insecure RSS connection?",
                Content = string.Format(
                    _loader.GetStringOrDefault("TextInsecureHttpRssConfirm") ?? "{0} uses unencrypted HTTP. Other devices on the network may read or modify its content. Allow it for this subscription?",
                    subscription.Url),
                PrimaryButtonText = _loader.GetStringOrDefault("TextAllow") ?? "Allow",
                CloseButtonText = _loader.GetStringOrDefault("CalendarDialog.CloseButtonText") ?? "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return false;

            _rssService.ApproveInsecureHttp(subscription);
            return true;
        }

        private async Task<bool> ConfirmLocalNetworkAsync(RssSubscription subscription)
        {
            if (!Uri.TryCreate(subscription.Url, UriKind.Absolute, out var uri)) return false;
            var authority = RssFetchPolicy.GetLocalNetworkAuthority(uri);
            var dialog = new ContentDialog
            {
                Title = _loader.GetStringOrDefault("TextLocalNetworkRssTitle") ?? "Allow local network RSS access?",
                Content = string.Format(
                    _loader.GetStringOrDefault("TextLocalNetworkRssConfirm") ?? "Allow only this subscription to connect to {0}? Redirects to another local host, port, or protocol will remain blocked.",
                    authority),
                PrimaryButtonText = _loader.GetStringOrDefault("TextAllow") ?? "Allow",
                CloseButtonText = _loader.GetStringOrDefault("CalendarDialog.CloseButtonText") ?? "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return false;
            _rssService.ApproveLocalNetwork(subscription);
            return true;
        }

        private void RssService_LocalDataCleared(object? sender, EventArgs e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!IsLoaded) return;

                _selectedArticle = null;
                _selectedSubscriptionId = null;
                _selectedFolderId = null;
                _lastArticleLoadSucceededAt = null;
                CancelRssResourceRequests();
                ReleaseRssWebView();
                ShowArticleList();
                ArticleListTitle.Text = _loader.GetStringOrDefault("TextAllArticles") ?? "All Articles";
                LoadFolders();
                LoadSubscriptions();
                BuildSubscriptionTree();
                ResetAndLoadCachedArticles();
            });
        }

        private string FormatRssRefreshError(Exception ex)
        {
            var message = ex.Message ?? "";
            if (message.Contains("non-public IP", StringComparison.OrdinalIgnoreCase))
                return _loader.GetStringOrDefault("TextRssRefreshBlockedPrivateNetwork")
                    ?? "Refresh was blocked because the RSS URL or redirect resolved to a non-public IP address. Local access must be approved for this subscription's exact host, port, and protocol.";

            return string.Format(
                _loader.GetStringOrDefault("TextRssRefreshFailed") ?? "Refresh failed: {0}",
                UserSafeErrorMessage.FromException(ex));
        }

        private void SetArticleListStatus(string message, bool isError = false)
        {
            if (ArticleListSubtitle == null) return;

            ArticleListSubtitle.Text = StatusMessageFormatter.Format(message, _lastArticleLoadSucceededAt, isError);
            var peer = FrameworkElementAutomationPeer.FromElement(ArticleListSubtitle) ??
                       FrameworkElementAutomationPeer.CreatePeerForElement(ArticleListSubtitle);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        private void AttachArticleScrollViewer()
        {
            if (_articleScrollViewer != null) return;
            _articleScrollViewer = FindDescendant<ScrollViewer>(ArticleList);
            if (_articleScrollViewer != null)
                _articleScrollViewer.ViewChanged += ArticleScrollViewer_ViewChanged;
        }

        private async void ArticleScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            if (_articleScrollViewer == null || e.IsIntermediate) return;
            if (_articleScrollViewer.ScrollableHeight - _articleScrollViewer.VerticalOffset < 240)
                await LoadMoreArticlesAsync();
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) return match;
                var nested = FindDescendant<T>(child);
                if (nested != null) return nested;
            }

            return null;
        }

        private void ArticleList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not RssArticle article) return;
            _ = OpenArticleReaderAsync(article);
        }

        private async Task OpenArticleReaderAsync(RssArticle article)
        {
            CancelRssResourceRequests();
            _rssResourceCts = new CancellationTokenSource();
            _selectedArticle = article;
            ArticleListHeader.Visibility = Visibility.Collapsed;
            ArticleList.Visibility = Visibility.Collapsed;
            ArticleReaderPanel.Visibility = Visibility.Visible;
            ReaderTitleText.Text = article.Title;
            ReaderMetaText.Text = $"{article.FeedTitle} · {article.PublishedText}";
            ReaderPrivacyText.Text = Windows.Storage.ApplicationData.Current.LocalSettings.Values["AllowRssRemoteResources"] as bool? ?? false
                ? (_loader.GetStringOrDefault("TextRssRemoteImagesProxied") ?? "Remote images are proxied through Task Flyout and blocked if they resolve to private networks.")
                : (_loader.GetStringOrDefault("TextRssRemoteImagesBlockedPrivacy") ?? "Remote images are blocked for privacy. Enable RSS remote resources in Settings to load them through the safe proxy.");
            await RenderArticleAsync(article);
        }

        private async Task RenderArticleAsync(RssArticle article)
        {
            try
            {
                var webView = EnsureRssArticleWebView();
                await webView.EnsureCoreWebView2Async();
                if (!_rssWebViewConfigured && webView.CoreWebView2 != null)
                {
                    WebView2RuntimeService.RegisterProfile(webView.CoreWebView2.Profile);
                    _rssWebViewConfigured = true;
                    var settings = webView.CoreWebView2.Settings;
                    settings.IsScriptEnabled = false;
                    settings.IsWebMessageEnabled = false;
                    settings.AreDefaultContextMenusEnabled = false;
                    settings.AreDevToolsEnabled = false;
                    settings.IsStatusBarEnabled = false;
                    settings.IsPinchZoomEnabled = true;
                    webView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                    webView.CoreWebView2.NavigationStarting += RssArticle_NavigationStarting;
                    webView.CoreWebView2.NavigationCompleted += RssArticle_NavigationCompleted;
                    webView.CoreWebView2.NewWindowRequested += RssArticle_NewWindowRequested;
                    webView.CoreWebView2.WebResourceRequested += RssArticle_WebResourceRequested;
                }

                // List rows no longer carry html_content (memory optimisation);
                // hydrate on demand the first time the article is opened.
                if (string.IsNullOrEmpty(article.HtmlContent) && _rssService != null)
                {
                    article.HtmlContent = _rssService.GetArticleHtml(article.Id);
                }

                _isInternalArticleNavigation = true;
                webView.NavigateToString(BuildArticleHtml(article, IsDarkThemeActive()));
            }
            catch (Exception ex)
            {
                LogRssError(ex);
                ArticleListSubtitle.Text = string.Format(_loader.GetStringOrDefault("TextArticleRenderFailed") ?? "Article render failed: {0}", UserSafeErrorMessage.FromException(ex));
            }
        }

        private WebView2 EnsureRssArticleWebView()
        {
            if (_rssArticleWebView != null)
                return _rssArticleWebView;

            WebView2RuntimeService.ConfigureSharedRuntime();
            _rssArticleWebView = new WebView2();
            RssArticleWebViewHost.Children.Clear();
            RssArticleWebViewHost.Children.Add(_rssArticleWebView);
            _rssWebViewConfigured = false;
            return _rssArticleWebView;
        }

        private void ReleaseRssWebView()
        {
            var webView = _rssArticleWebView;
            if (webView == null) return;

            try
            {
                if (webView.CoreWebView2 != null)
                {
                    webView.CoreWebView2.NavigationStarting -= RssArticle_NavigationStarting;
                    webView.CoreWebView2.NavigationCompleted -= RssArticle_NavigationCompleted;
                    webView.CoreWebView2.NewWindowRequested -= RssArticle_NewWindowRequested;
                    webView.CoreWebView2.WebResourceRequested -= RssArticle_WebResourceRequested;
                    webView.NavigateToString("<html></html>");
                }

                webView.Close();
            }
            catch { }

            RssArticleWebViewHost.Children.Clear();
            _rssArticleWebView = null;
            _rssWebViewConfigured = false;
        }

        private void RssArticle_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
        {
            if (_isInternalArticleNavigation)
                return;

            if (args.Uri == "about:blank") return;
            args.Cancel = true;
            OpenSafeExternalUri(args.Uri);
        }

        private void RssArticle_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            _isInternalArticleNavigation = false;
        }

        private void RssArticle_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs args)
        {
            args.Handled = true;
            OpenSafeExternalUri(args.Uri);
        }

        private async void RssArticle_WebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
        {
            if (sender is not CoreWebView2 coreWebView) return;
            var uri = args.Request?.Uri;

            // about:/data: resources need no network and pass through unchanged.
            if (WebView2RuntimeService.IsAllowedRssNonRemoteResource(uri))
                return;

            // Enabled remote images: fetch through the app's IP-pinned HTTP client and serve
            // the bytes, so the WebView never connects to the (possibly rebinding) host itself.
            if (WebView2RuntimeService.ShouldProxyRssRemoteResource(uri))
            {
                var deferral = args.GetDeferral();
                try
                {
                    var cts = _rssResourceCts;
                    if (cts == null || cts.IsCancellationRequested)
                    {
                        args.Response = BlockedRssResponse(coreWebView);
                        return;
                    }

                    var fetched = await _rssService.FetchRemoteImageSafelyAsync(uri!, cts.Token);
                    if (fetched != null)
                    {
                        args.Response = coreWebView.Environment.CreateWebResourceResponse(
                            fetched.Stream, 200, "OK", $"Content-Type: {fetched.ContentType}");
                    }
                    else
                    {
                        args.Response = BlockedRssResponse(coreWebView);
                    }
                }
                catch
                {
                    args.Response = BlockedRssResponse(coreWebView);
                }
                finally
                {
                    deferral.Complete();
                }
                return;
            }

            args.Response = BlockedRssResponse(coreWebView);
        }

        private static CoreWebView2WebResourceResponse BlockedRssResponse(CoreWebView2 coreWebView)
            => coreWebView.Environment.CreateWebResourceResponse(
                new InMemoryRandomAccessStream(), 403, "Blocked", "Content-Type: text/plain");

        private void BackToArticleListButton_Click(object sender, RoutedEventArgs e)
        {
            ShowArticleList();
        }

        private void ShowArticleList()
        {
            CancelRssResourceRequests();
            ArticleReaderPanel.Visibility = Visibility.Collapsed;
            ArticleListHeader.Visibility = Visibility.Visible;
            ArticleList.Visibility = Visibility.Visible;
            _selectedArticle = null;
            _isInternalArticleNavigation = false;
        }

        private void CancelRssResourceRequests()
        {
            try { _rssResourceCts?.Cancel(); }
            catch { }
            _rssResourceCts?.Dispose();
            _rssResourceCts = null;
        }

        private void OpenArticleInBrowserButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedArticle == null) return;
            OpenSafeExternalUri(_selectedArticle.Link);
        }

        private bool IsDarkThemeActive()
        {
            var theme = ActualTheme;
            if (theme == ElementTheme.Default && App.MyMainWindow?.Content is FrameworkElement root)
                theme = root.ActualTheme;
            return theme == ElementTheme.Dark;
        }

        private static string BuildArticleHtml(RssArticle article, bool isDarkTheme)
        {
            var background = isDarkTheme ? "#1f1f1f" : "#ffffff";
            var text = isDarkTheme ? "#f3f3f3" : "#202020";
            var muted = isDarkTheme ? "#d7d7d7" : "#4b5563";
            var border = isDarkTheme ? "#3a3a3a" : "#e5e7eb";
            var link = isDarkTheme ? "#8ab4f8" : "#2563eb";
            var body = SanitizeArticleHtml(string.IsNullOrWhiteSpace(article.HtmlContent)
                ? $"<p>{WebUtility.HtmlEncode(article.Summary)}</p>"
                : article.HtmlContent);
            var darkOverride = isDarkTheme
                ? $$"""
body *:not(img):not(video):not(canvas) { color: {{text}} !important; border-color: {{border}} !important; }
body table, body tbody, body thead, body tr, body td, body th, body div, body section, body article, body p, body span, body font, body blockquote, body ul, body ol, body li { background-color: transparent !important; }
body [bgcolor] { background-color: transparent !important; }
body a, body a * { color: {{link}} !important; }
"""
                : "";

            return $$"""
<!doctype html>
<html>
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<style>
:root { color-scheme: {{(isDarkTheme ? "dark" : "light")}}; }
html, body {
    margin: 0;
    padding: 0;
    background: {{background}};
    color: {{text}};
    font-family: "Segoe UI", Arial, sans-serif;
    font-size: 15px;
    line-height: 1.62;
    overflow-wrap: anywhere;
}
body { padding: 18px 20px 32px; }
img { max-width: 100%; height: auto; border-radius: 6px; }
table { max-width: 100%; border-collapse: collapse; }
td, th { border-color: {{border}}; }
a { color: {{link}}; }
pre, code { white-space: pre-wrap; overflow-wrap: anywhere; }
.meta { color: {{muted}}; font-size: 12px; margin-bottom: 18px; }
{{darkOverride}}
</style>
</head>
<body>
<div class="meta">{{WebUtility.HtmlEncode(article.FeedTitle)}} · {{WebUtility.HtmlEncode(article.PublishedText)}}</div>
{{body}}
</body>
</html>
""";
        }

        private static string SanitizeArticleHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return "";

            try
            {
                var value = html;
                value = Regex.Replace(value, @"<\s*(script|iframe|object|embed|form|input|button|textarea|select|svg|noscript|template|base)\b[^>]*>.*?<\s*/\s*\1\s*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                value = Regex.Replace(value, @"<\s*(script|iframe|object|embed|form|input|button|textarea|select|svg|noscript|template|base)\b[^>]*/?\s*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                value = Regex.Replace(value, @"\s+on\w+\s*=\s*(['""]).*?\1", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                value = Regex.Replace(value, @"\s+on\w+\s*=\s*[^\s>]+", "", RegexOptions.IgnoreCase);
                value = Regex.Replace(value, @"(href|src|action|formaction|data)\s*=\s*(['""])\s*(javascript|vbscript|data):.*?\2", "$1=\"#\"", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                value = Regex.Replace(value, @"(href|src|action|formaction|data)\s*=\s*(javascript|vbscript|data):[^\s>]+", "$1=\"#\"", RegexOptions.IgnoreCase);
                value = Regex.Replace(value, @"\sbackground\s*=\s*(['""])\s*https?://.*?\1", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                value = Regex.Replace(value, @"\sbackground\s*=\s*https?://[^\s>]+", "", RegexOptions.IgnoreCase);
                value = Regex.Replace(value, @"style\s*=\s*(['""])[^'""]*\b(expression|url\s*\(|-moz-binding|behavior)\b[^'""]*\1", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                return value;
            }
            catch (RegexMatchTimeoutException)
            {
                // Pathological markup tripped the ReDoS guard — escape everything so the
                // article renders as inert plain text rather than hanging the renderer.
                try { return WebUtility.HtmlEncode(Regex.Replace(html, "<.*?>", " ", RegexOptions.Singleline)); }
                catch { return WebUtility.HtmlEncode(html); }
            }
        }

        private static void OpenSafeExternalUri(string uriText)
            => _ = SafeUriLauncher.TryLaunchExternalHttpUriAsync(uriText);

        private static T? FindAncestor<T>(DependencyObject source) where T : DependencyObject
        {
            var current = source;
            while (current != null)
            {
                if (current is T match) return match;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private static void LogRssError(Exception ex)
        {
            const long maxLogBytes = 512 * 1024;
            try
            {
                var logDir = AppDataPathHelper.EnsureDirectory(AppDataPathHelper.ResolveLocal("Logs"));
                var logPath = AppDataPathHelper.ResolveLocal("Logs", "TaskFlyout_RssLog.txt");
                if (File.Exists(logPath) && new FileInfo(logPath).Length >= maxLogBytes)
                    File.WriteAllText(logPath, "");
                File.AppendAllText(logPath, DiagnosticEventFormatter.FormatException("rss.page", ex) + Environment.NewLine);
            }
            catch { }
        }
    }
}
