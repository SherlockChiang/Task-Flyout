using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Task_Flyout.Models;
using Task_Flyout.Services;

namespace Task_Flyout.Views
{
    public sealed partial class TasksPage : Page
    {
        public ObservableCollection<AgendaItem> PendingTasks { get; } = new();
        public ObservableCollection<AgendaItem> CompletedTasks { get; } = new();

        private SyncManager? _syncManager;
        private AppCache _localCache = new();
        private bool _isAccountPaneCollapsed;

        public TasksPage()
        {
            InitializeComponent();
            if (App.Current is App app)
                _syncManager = app.SyncManager;

            Loaded += TasksPage_Loaded;
        }

        private async void TasksPage_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshAccountList();
            LoadCache();
            ReloadTasks();

            if (PendingTasks.Count == 0 && CompletedTasks.Count == 0)
                await SyncTasksAsync(forceRefresh: false);
        }

        private void LoadCache()
        {
            _localCache = _syncManager?.GetLocalCache() ?? new AppCache();
        }

        public void RefreshAccountList()
        {
            var mgr = _syncManager?.AccountManager;
            if (mgr == null || AccountListRepeater == null) return;

            AccountListRepeater.ItemsSource = null;
            AccountListRepeater.ItemsSource = mgr.Accounts;
        }

        public void ReloadFilters()
        {
            ReloadTasks();
        }

        public void ForceSync()
        {
            _ = SyncTasksAsync(forceRefresh: true);
        }

        private async Task SyncTasksAsync(bool forceRefresh)
        {
            if (_syncManager == null || SyncProgress == null) return;

            try
            {
                SyncProgress.IsActive = true;
                var min = DateTime.Today.AddDays(-30);
                var max = DateTime.Today.AddDays(365);
                await _syncManager.GetAllDataAsync(min, max, forceRefresh);
                LoadCache();
                ReloadTasks();
            }
            finally
            {
                SyncProgress.IsActive = false;
            }
        }

        private void ReloadTasks()
        {
            PendingTasks.Clear();
            CompletedTasks.Clear();

            var tasks = _localCache.DayItems.Values
                .SelectMany(items => items)
                .Where(item => item.IsTask && IsItemVisible(item))
                .GroupBy(GetTaskIdentity)
                .Select(group => group.OrderByDescending(GetTaskSortDate).First())
                .OrderBy(GetTaskSortDate)
                .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            foreach (var task in tasks)
            {
                PopulateItemColor(task);
                if (task.IsCompleted)
                    CompletedTasks.Add(task);
                else
                    PendingTasks.Add(task);
            }

            CompletedTasks.SortInPlace((a, b) =>
                GetTaskSortDate(b).CompareTo(GetTaskSortDate(a)) != 0
                    ? GetTaskSortDate(b).CompareTo(GetTaskSortDate(a))
                    : string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase));

            PendingHeaderText.Text = $"待完成 ({PendingTasks.Count})";
            CompletedHeaderText.Text = $"已完成 ({CompletedTasks.Count})";
            TaskSummaryText.Text = $"{PendingTasks.Count} 个待完成，{CompletedTasks.Count} 个已完成";
        }

        private bool IsItemVisible(AgendaItem item)
        {
            var accountMgr = _syncManager?.AccountManager;
            return accountMgr?.IsItemVisible(item) ?? true;
        }

        private void PopulateItemColor(AgendaItem item)
        {
            _syncManager?.AccountManager.PopulateItemColor(item);
        }

        private static string GetTaskIdentity(AgendaItem item)
            => $"{item.Provider}|{item.Id}|{item.Title}|{item.DateKey}";

        private static DateTime GetTaskSortDate(AgendaItem item)
        {
            if (item.StartDateTime.HasValue) return item.StartDateTime.Value;
            if (DateTime.TryParse(item.DateKey, out var date)) return date;
            return DateTime.MaxValue;
        }

        private async void TaskCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (_syncManager == null || sender is not CheckBox cb || cb.DataContext is not AgendaItem item)
                return;

            var newValue = cb.IsChecked == true;
            var oldValue = !newValue;

            try
            {
                cb.IsEnabled = false;
                SetTaskCompletionInCache(item, newValue);
                await _syncManager.SaveLocalCacheAsync();
                ReloadTasks();

                if (!string.IsNullOrWhiteSpace(item.Provider) && !string.IsNullOrWhiteSpace(item.Id))
                    await _syncManager.UpdateTaskStatusAsync(item.Provider, item.Id, newValue);

                App.MyFlyoutWindow?.ReloadFilters();
                if (App.MyMainWindow?.Content is FrameworkElement)
                {
                    // Calendar and task views share the same cache; opened pages refresh when re-entered.
                }
            }
            catch
            {
                SetTaskCompletionInCache(item, oldValue);
                await _syncManager.SaveLocalCacheAsync();
                ReloadTasks();
            }
            finally
            {
                cb.IsEnabled = true;
            }
        }

        private void SetTaskCompletionInCache(AgendaItem target, bool isCompleted)
        {
            foreach (var task in _localCache.DayItems.Values.SelectMany(items => items)
                         .Where(item => item.IsTask && IsSameTask(item, target)))
            {
                task.IsCompleted = isCompleted;
            }
        }

        private static bool IsSameTask(AgendaItem a, AgendaItem b)
        {
            if (!string.IsNullOrWhiteSpace(a.Id) && !string.IsNullOrWhiteSpace(b.Id))
                return string.Equals(a.Provider, b.Provider, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(a.Id, b.Id, StringComparison.Ordinal);

            return string.Equals(a.Provider, b.Provider, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.Title, b.Title, StringComparison.Ordinal)
                && string.Equals(a.DateKey, b.DateKey, StringComparison.Ordinal);
        }

        private void AccountToggle_Toggled(object sender, RoutedEventArgs e)
        {
            _syncManager?.AccountManager.Save();
            ReloadTasks();
            App.MyFlyoutWindow?.ReloadFilters();
        }

        private void BtnAddAccount_Click(object sender, RoutedEventArgs e)
        {
            App.MyMainWindow?.NavigateToAddAccount();
        }

        private async void BtnRemoveAccount_Click(object sender, RoutedEventArgs e)
        {
            if (_syncManager == null || sender is not Button btn || btn.Tag is not string providerName)
                return;

            var dialog = new ContentDialog
            {
                Title = "移除账户",
                Content = $"确定要移除 {providerName} 账户吗？",
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                XamlRoot = XamlRoot,
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            await _syncManager.RemoveAccountAsync(providerName);
            RefreshAccountList();
            LoadCache();
            ReloadTasks();
            App.MyFlyoutWindow?.ReloadFilters();
        }

        private void BtnForceSync_Click(object sender, RoutedEventArgs e)
        {
            ForceSync();
        }

        private void ToggleAccountPane_Click(object sender, RoutedEventArgs e)
        {
            _isAccountPaneCollapsed = !_isAccountPaneCollapsed;
            AccountColumn.Width = _isAccountPaneCollapsed ? new GridLength(0) : new GridLength(260);
            AccountPane.Visibility = _isAccountPaneCollapsed ? Visibility.Collapsed : Visibility.Visible;
            ToggleAccountPaneIcon.Glyph = _isAccountPaneCollapsed ? "\uE76C" : "\uE76B";
        }
    }

    internal static class ObservableCollectionSortExtensions
    {
        public static void SortInPlace<T>(this ObservableCollection<T> collection, Comparison<T> comparison)
        {
            var ordered = collection.ToList();
            ordered.Sort(comparison);
            collection.Clear();
            foreach (var item in ordered)
                collection.Add(item);
        }
    }
}
