using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Task_Flyout.Services;
using Windows.System;

namespace Task_Flyout.Views
{
    public sealed partial class RssPage : Page
    {
        private const int PageSize = 20;

        public ObservableCollection<RssSubscription> Subscriptions { get; } = new();
        public ObservableCollection<RssFolder> Folders { get; } = new();
        public ObservableCollection<RssArticle> Articles { get; } = new();

        private readonly RssService _rssService = new();
        private readonly Dictionary<TreeViewNode, RssFolder> _folderNodes = new();
        private readonly Dictionary<TreeViewNode, RssSubscription> _subscriptionNodes = new();
        private TreeViewNode? _allNode;
        private string? _selectedSubscriptionId;
        private string? _selectedFolderId;
        private int _loadedCount;
        private bool _isLoading;
        private bool _hasMore = true;
        private ScrollViewer? _articleScrollViewer;
        private RssArticle? _selectedArticle;
        private bool _rssWebViewConfigured;
        private bool _isInternalArticleNavigation;

        public RssPage()
        {
            InitializeComponent();
            Loaded += RssPage_Loaded;
            ActualThemeChanged += RssPage_ActualThemeChanged;
        }

        private async void RssPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                LoadFolders();
                LoadSubscriptions();
                BuildSubscriptionTree();
                AttachArticleScrollViewer();
                ResetAndLoadCachedArticles();
            }
            catch (Exception ex)
            {
                LogRssError(ex);
                ArticleListSubtitle.Text = $"RSS 初始化失败：{ex.Message}";
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
                Content = "全部文章",
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
                Content = "未分类",
                IsExpanded = true
            };
            RssTree.RootNodes.Add(uncategorizedNode);
            foreach (var subscription in _rssService.GetSubscriptions().Where(item => string.IsNullOrWhiteSpace(item.FolderId)))
                AddSubscriptionNode(uncategorizedNode, subscription);
        }

        private static string CreateFolderNodeContent(RssFolder folder)
            => string.IsNullOrWhiteSpace(folder.Name) ? "文件夹" : folder.Name;

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
                ? "还没有订阅。添加 RSS 地址后开始阅读。"
                : $"{Subscriptions.Count} 个订阅，{Folders.Count} 个文件夹";
        }

        private async Task ResetAndLoadArticlesAsync()
        {
            Articles.Clear();
            _loadedCount = 0;
            _hasMore = true;
            await LoadMoreArticlesAsync();
        }

        private void ResetAndLoadCachedArticles()
        {
            Articles.Clear();
            _loadedCount = 0;
            _hasMore = true;

            try
            {
                var items = _rssService.GetCachedArticles(_selectedSubscriptionId, _selectedFolderId)
                    .Take(PageSize)
                    .ToList();
                foreach (var item in items)
                    Articles.Add(item);

                _loadedCount = items.Count;
                _hasMore = items.Count == PageSize;
                ArticleListSubtitle.Text = Articles.Count == 0
                    ? "暂无缓存文章，点击刷新获取"
                    : _hasMore ? $"{Articles.Count} 篇文章，向下滚动加载更多" : $"{Articles.Count} 篇文章";
            }
            catch (Exception ex)
            {
                LogRssError(ex);
                ArticleListSubtitle.Text = $"缓存读取失败：{ex.Message}";
            }
        }

        private async Task LoadMoreArticlesAsync()
        {
            if (_isLoading || !_hasMore) return;

            try
            {
                _isLoading = true;
                SetLoading(true);
                var items = await _rssService.LoadMoreArticlesAsync(_selectedSubscriptionId, _selectedFolderId, _loadedCount, PageSize);
                foreach (var item in items)
                    Articles.Add(item);

                _loadedCount += items.Count;
                _hasMore = items.Count == PageSize;
                ArticleListSubtitle.Text = Articles.Count == 0
                    ? "暂无文章"
                    : _hasMore ? $"{Articles.Count} 篇文章，点击加载更多" : $"{Articles.Count} 篇文章";
            }
            catch (Exception ex)
            {
                LogRssError(ex);
                ArticleListSubtitle.Text = $"加载失败：{ex.Message}";
            }
            finally
            {
                SetLoading(false);
                _isLoading = false;
            }
        }

        private void SetLoading(bool isLoading)
        {
            LoadingRing.IsActive = isLoading;
            LoadingRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void RssTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            if (args.InvokedItem is not TreeViewNode node) return;

            if (node == _allNode)
            {
                ShowArticleList();
                _selectedSubscriptionId = null;
                _selectedFolderId = null;
                ArticleListTitle.Text = "全部文章";
                SubtitleText.Text = "全部订阅文章";
            }
            else if (_folderNodes.TryGetValue(node, out var folder))
            {
                ShowArticleList();
                _selectedSubscriptionId = null;
                _selectedFolderId = folder.Id;
                ArticleListTitle.Text = folder.Name;
                SubtitleText.Text = "当前文件夹下的 RSS 文章";
            }
            else if (_subscriptionNodes.TryGetValue(node, out var subscription))
            {
                ShowArticleList();
                _selectedSubscriptionId = subscription.Id;
                _selectedFolderId = null;
                ArticleListTitle.Text = subscription.Title;
                SubtitleText.Text = subscription.Url;
            }
            else if (node.Content?.ToString() == "未分类")
            {
                ShowArticleList();
                _selectedSubscriptionId = null;
                _selectedFolderId = "";
                ArticleListTitle.Text = "未分类";
                SubtitleText.Text = "未分类订阅文章";
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
                Header = "RSS 地址",
                PlaceholderText = "https://example.com/feed.xml"
            };
            var folderBox = CreateFolderComboBox(_selectedFolderId);
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(urlBox);
            panel.Children.Add(folderBox);

            var dialog = new ContentDialog
            {
                Title = "添加 RSS 订阅",
                Content = panel,
                PrimaryButtonText = "添加",
                CloseButtonText = "取消",
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
                    await _rssService.AddSubscriptionAsync(url, folderId);
                    LoadSubscriptions();
                    LoadFolders();
                    BuildSubscriptionTree();
                    _selectedSubscriptionId = null;
                    _selectedFolderId = null;
                    ArticleListTitle.Text = "全部文章";
                    SubtitleText.Text = "全部订阅文章";
                    ResetAndLoadCachedArticles();
                }
                catch (Exception ex)
                {
                    args.Cancel = true;
                    SubscriptionStatusText.Text = $"添加失败：{ex.Message}";
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
                Title = "删除订阅",
                Content = $"确定要删除“{subscription.Title}”吗？本地缓存文章也会一起删除。",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            _rssService.RemoveSubscription(subscription.Id);
            if (_selectedSubscriptionId == subscription.Id)
            {
                _selectedSubscriptionId = null;
                ArticleListTitle.Text = "全部文章";
                SubtitleText.Text = "全部订阅文章";
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
            var box = new TextBox { Header = "文件夹名称", Text = folder.Name };
            var dialog = new ContentDialog
            {
                Title = "重命名文件夹",
                Content = box,
                PrimaryButtonText = "保存",
                CloseButtonText = "取消",
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
            var box = new TextBox { Header = "订阅名称", Text = subscription.Title };
            var dialog = new ContentDialog
            {
                Title = "重命名订阅",
                Content = box,
                PrimaryButtonText = "保存",
                CloseButtonText = "取消",
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
                Header = "文件夹名称",
                PlaceholderText = "例如：科技、新闻、博客"
            };
            var dialog = new ContentDialog
            {
                Title = "添加 RSS 文件夹",
                Content = nameBox,
                PrimaryButtonText = "添加",
                CloseButtonText = "取消",
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
                Title = "删除文件夹",
                Content = $"确定要删除“{folder.Name}”吗？订阅不会删除，会移动到未分类。",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            _rssService.RemoveFolder(folder.Id);
            if (_selectedFolderId == folder.Id)
            {
                _selectedFolderId = null;
                ArticleListTitle.Text = "全部文章";
                SubtitleText.Text = "全部订阅文章";
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
                Title = $"设置“{subscription.Title}”的文件夹",
                Content = folderBox,
                PrimaryButtonText = "保存",
                CloseButtonText = "取消",
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

            foreach (var root in RssTree.RootNodes.Where(node => node.Content?.ToString() == "未分类"))
            {
                foreach (var child in root.Children.Where(node => _subscriptionNodes.ContainsKey(node)))
                    _rssService.MoveSubscriptionToFolder(_subscriptionNodes[child].Id, "");
            }
        }


        private ComboBox CreateFolderComboBox(string? selectedFolderId)
        {
            var folderBox = new ComboBox
            {
                Header = "文件夹",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            folderBox.Items.Add(new ComboBoxItem { Content = "未分类", Tag = "" });
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
            if (_selectedSubscriptionId == null)
            {
                IEnumerable<RssSubscription> visibleSubscriptions = _rssService.GetSubscriptions();
                if (_selectedFolderId != null)
                    visibleSubscriptions = visibleSubscriptions.Where(subscription => subscription.FolderId == _selectedFolderId);

                foreach (var subscription in visibleSubscriptions.Take(10))
                    await _rssService.RefreshSubscriptionAsync(subscription, force: true);
            }
            else if (_rssService.GetSubscriptions().FirstOrDefault(item => item.Id == _selectedSubscriptionId) is { } subscription)
            {
                await _rssService.RefreshSubscriptionAsync(subscription, force: true);
            }

            LoadSubscriptions();
            ResetAndLoadCachedArticles();
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

        private void Article_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not RssArticle article) return;
            _ = OpenArticleReaderAsync(article);
        }

        private async Task OpenArticleReaderAsync(RssArticle article)
        {
            _selectedArticle = article;
            ArticleListHeader.Visibility = Visibility.Collapsed;
            ArticleList.Visibility = Visibility.Collapsed;
            ArticleReaderPanel.Visibility = Visibility.Visible;
            ReaderTitleText.Text = article.Title;
            ReaderMetaText.Text = $"{article.FeedTitle} · {article.PublishedText}";
            await RenderArticleAsync(article);
        }

        private async Task RenderArticleAsync(RssArticle article)
        {
            try
            {
                await RssArticleWebView.EnsureCoreWebView2Async();
                if (!_rssWebViewConfigured && RssArticleWebView.CoreWebView2 != null)
                {
                    _rssWebViewConfigured = true;
                    var settings = RssArticleWebView.CoreWebView2.Settings;
                    settings.IsScriptEnabled = false;
                    settings.IsWebMessageEnabled = false;
                    settings.AreDefaultContextMenusEnabled = false;
                    settings.AreDevToolsEnabled = false;
                    settings.IsStatusBarEnabled = false;
                    settings.IsPinchZoomEnabled = true;
                    RssArticleWebView.CoreWebView2.NavigationStarting += (_, args) =>
                    {
                        if (_isInternalArticleNavigation)
                            return;

                        if (args.Uri == "about:blank") return;
                        args.Cancel = true;
                        OpenSafeExternalUri(args.Uri);
                    };
                    RssArticleWebView.CoreWebView2.NavigationCompleted += (_, _) =>
                    {
                        _isInternalArticleNavigation = false;
                    };
                    RssArticleWebView.CoreWebView2.NewWindowRequested += (_, args) =>
                    {
                        args.Handled = true;
                        OpenSafeExternalUri(args.Uri);
                    };
                }

                _isInternalArticleNavigation = true;
                RssArticleWebView.NavigateToString(BuildArticleHtml(article, IsDarkThemeActive()));
            }
            catch (Exception ex)
            {
                LogRssError(ex);
                ArticleListSubtitle.Text = $"文章渲染失败：{ex.Message}";
            }
        }

        private void BackToArticleListButton_Click(object sender, RoutedEventArgs e)
        {
            ShowArticleList();
        }

        private void ShowArticleList()
        {
            ArticleReaderPanel.Visibility = Visibility.Collapsed;
            ArticleListHeader.Visibility = Visibility.Visible;
            ArticleList.Visibility = Visibility.Visible;
            _selectedArticle = null;
            _isInternalArticleNavigation = false;
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
            var value = html;
            value = Regex.Replace(value, @"<\s*(script|iframe|object|embed|form|input|button|textarea|select|svg|noscript|template|base)\b[^>]*>.*?<\s*/\s*\1\s*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            value = Regex.Replace(value, @"<\s*(script|iframe|object|embed|form|input|button|textarea|select|svg|noscript|template|base)\b[^>]*/?\s*>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            value = Regex.Replace(value, @"\s+on\w+\s*=\s*(['""]).*?\1", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            value = Regex.Replace(value, @"\s+on\w+\s*=\s*[^\s>]+", "", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"(href|src|action|formaction)\s*=\s*(['""])\s*(javascript|vbscript):.*?\2", "$1=\"#\"", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            value = Regex.Replace(value, @"(href|src|action|formaction)\s*=\s*(javascript|vbscript):[^\s>]+", "$1=\"#\"", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"style\s*=\s*(['""])[^'""]*\b(expression|-moz-binding|behavior)\b[^'""]*\1", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return value;
        }

        private static void OpenSafeExternalUri(string uriText)
        {
            if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri)) return;
            if (uri.Scheme != "http" && uri.Scheme != "https") return;
            _ = Launcher.LaunchUriAsync(uri);
        }

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
            try
            {
                var logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "TaskFlyout",
                    "Logs");
                Directory.CreateDirectory(logDir);
                var logPath = Path.Combine(logDir, "TaskFlyout_RssLog.txt");
                File.AppendAllText(logPath, $"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{ex}\n\n");
            }
            catch { }
        }
    }
}
