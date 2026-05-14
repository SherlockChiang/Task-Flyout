using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
        private string? _selectedSubscriptionId;
        private string? _selectedFolderId;
        private int _loadedCount;
        private bool _isLoading;
        private bool _hasMore = true;
        private ScrollViewer? _articleScrollViewer;

        public RssPage()
        {
            InitializeComponent();
            Loaded += RssPage_Loaded;
        }

        private async void RssPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadFolders();
            LoadSubscriptions();
            AttachArticleScrollViewer();
            await ResetAndLoadArticlesAsync();
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
                    : _hasMore ? $"{Articles.Count} 篇文章，向下滚动加载更多" : $"{Articles.Count} 篇文章";
            }
            catch (Exception ex)
            {
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

        private async void SubscriptionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SubscriptionList.SelectedItem is not RssSubscription subscription) return;

            FolderList.SelectedItem = null;
            _selectedSubscriptionId = subscription.Id;
            _selectedFolderId = null;
            ArticleListTitle.Text = subscription.Title;
            SubtitleText.Text = subscription.Url;
            await ResetAndLoadArticlesAsync();
        }

        private async void FolderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FolderList.SelectedItem is not RssFolder folder) return;

            SubscriptionList.SelectedItem = null;
            _selectedSubscriptionId = null;
            _selectedFolderId = folder.Id;
            ArticleListTitle.Text = folder.Name;
            SubtitleText.Text = "当前文件夹下的 RSS 文章";
            await ResetAndLoadArticlesAsync();
        }

        private async void AllFeedsButton_Click(object sender, RoutedEventArgs e)
        {
            SubscriptionList.SelectedItem = null;
            FolderList.SelectedItem = null;
            _selectedSubscriptionId = null;
            _selectedFolderId = null;
            ArticleListTitle.Text = "全部文章";
            SubtitleText.Text = "全部订阅文章";
            await ResetAndLoadArticlesAsync();
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
                    _selectedSubscriptionId = null;
                    _selectedFolderId = null;
                    ArticleListTitle.Text = "全部文章";
                    SubtitleText.Text = "全部订阅文章";
                    await ResetAndLoadArticlesAsync();
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
            await ResetAndLoadArticlesAsync();
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
            };

            await dialog.ShowAsync();
        }

        private async void DeleteFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not RssFolder folder) return;

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
            await ResetAndLoadArticlesAsync();
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
            await ResetAndLoadArticlesAsync();
        }

        private void FolderList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            _rssService.SaveFolderOrder(Folders.Select(folder => folder.Id));
            LoadFolders();
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
            await ResetAndLoadArticlesAsync();
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
            if (!Uri.TryCreate(article.Link, UriKind.Absolute, out var uri)) return;
            _ = Launcher.LaunchUriAsync(uri);
        }
    }
}
