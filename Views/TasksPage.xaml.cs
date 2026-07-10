using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
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
        public SuppressableObservableCollection<AgendaItem> PendingTasks { get; } = new();
        public SuppressableObservableCollection<AgendaItem> CompletedTasks { get; } = new();

        private SyncManager? _syncManager;
        private AppCache _localCache = new();
        private ResourceLoader _loader = new();
        private bool _isAccountPaneCollapsed;
        private const int TaskWindowPastDays = 365;
        private const int TaskWindowFutureDays = 365 * 3;

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
                await SyncTasksAsync(forceRefresh: true, fullRange: false);
        }

        private void LoadCache()
        {
            _localCache = _syncManager?.GetTaskCacheSnapshot() ?? new AppCache();
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
            _ = SyncTasksAsync(forceRefresh: true, fullRange: true);
        }

        private async Task SyncTasksAsync(bool forceRefresh, bool fullRange)
        {
            if (_syncManager == null || SyncProgress == null) return;

            try
            {
                SetSyncProgress(true);
                var min = fullRange ? DateTime.Today.AddYears(-1) : DateTime.Today.AddDays(-30);
                var max = fullRange ? DateTime.Today.AddYears(3) : DateTime.Today.AddDays(365);
                await _syncManager.GetAllDataAsync(min, max, forceRefresh);
                LoadCache();
                ReloadTasks();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Task sync failed: {ex.Message}");
                TaskSummaryText.Text = _loader.GetStringOrDefault("TextSyncFailed") ?? "Sync failed. Please try again.";
            }
            finally
            {
                SetSyncProgress(false);
            }
        }

        private void SetSyncProgress(bool isActive)
        {
            SyncProgress.IsActive = isActive;
            SyncProgress.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
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

            PendingHeaderText.Text = string.Format(_loader.GetStringOrDefault("TextPendingTasks") ?? "Pending ({0})", PendingTasks.Count);
            CompletedHeaderText.Text = string.Format(_loader.GetStringOrDefault("TextCompletedTasks") ?? "Completed ({0})", CompletedTasks.Count);
            TaskSummaryText.Text = PendingTasks.Count == 0
                ? string.Format(_loader.GetStringOrDefault("TextAllDone") ?? "All done, {0} completed", CompletedTasks.Count)
                : string.Format(_loader.GetStringOrDefault("TextTaskSummary") ?? "{0} pending, {1} completed", PendingTasks.Count, CompletedTasks.Count);
            PendingTasksList.Visibility = PendingTasks.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            PendingEmptyText.Visibility = PendingTasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
                await _syncManager.SetCachedTaskCompletionAsync(item, newValue);
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
                await _syncManager.SetCachedTaskCompletionAsync(item, oldValue);
                ReloadTasks();
            }
            finally
            {
                cb.IsEnabled = true;
            }
        }

        private async void AddTaskButton_Click(object sender, RoutedEventArgs e)
        {
            if (_syncManager == null) return;

            var providerNames = _syncManager.Providers
                .Where(provider => _syncManager.AccountManager.IsConnected(provider.ProviderName))
                .Select(provider => provider.ProviderName)
                .ToList();

            if (providerNames.Count == 0)
            {
                await new ContentDialog
                {
                    Title = _loader.GetStringOrDefault("TextNoAccountAvailable") ?? "No Account Available",
                    Content = _loader.GetStringOrDefault("TextAddAccountForTasks") ?? "Please add and sign in to a task-supporting account first.",
                    CloseButtonText = _loader.GetStringOrDefault("TextConfirm") ?? "Confirm",
                    XamlRoot = XamlRoot
                }.ShowAsync();
                return;
            }

            var titleBox = new TextBox
            {
                Header = _loader.GetStringOrDefault("TextTitle") ?? "Title",
                PlaceholderText = _loader.GetStringOrDefault("TextTaskName") ?? "Task name"
            };
            var providerBox = new ComboBox
            {
                Header = _loader.GetStringOrDefault("TextAccount") ?? "Account",
                ItemsSource = providerNames,
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var datePicker = new DatePicker
            {
                Header = _loader.GetStringOrDefault("TextDate") ?? "Date",
                Date = DateTimeOffset.Now
            };
            var timePicker = new TimePicker
            {
                Header = _loader.GetStringOrDefault("TextTime") ?? "Time",
                Time = new TimeSpan(9, 0, 0)
            };
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(titleBox);
            panel.Children.Add(providerBox);
            panel.Children.Add(datePicker);
            panel.Children.Add(timePicker);

            var dialog = new ContentDialog
            {
                Title = _loader.GetStringOrDefault("TextAddTask") ?? "Add Task",
                Content = panel,
                PrimaryButtonText = _loader.GetStringOrDefault("CalendarDialog.PrimaryButtonText") ?? "Save",
                CloseButtonText = _loader.GetStringOrDefault("CalendarDialog.CloseButtonText") ?? "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            dialog.PrimaryButtonClick += async (_, args) =>
            {
                var title = titleBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(title))
                {
                    args.Cancel = true;
                    titleBox.PlaceholderText = _loader.GetStringOrDefault("TextEnterTaskName") ?? "Please enter task name";
                    titleBox.Focus(FocusState.Programmatic);
                    return;
                }

                var deferral = args.GetDeferral();
                try
                {
                    var providerName = providerBox.SelectedItem?.ToString();
                    var targetDate = datePicker.Date.DateTime.Date;
                    await _syncManager.CreateItemAsync(title, isEvent: false, isAllDay: false, targetDate, timePicker.Time, timePicker.Time, "", providerName);
                    await SyncTasksAsync(forceRefresh: true, fullRange: true);
                    App.MyFlyoutWindow?.ReloadFilters();
                }
                finally
                {
                    deferral.Complete();
                }
            };

            await dialog.ShowAsync();
        }

        private async void DeleteTaskButton_Click(object sender, RoutedEventArgs e)
        {
            if (_syncManager == null || sender is not Button button || button.DataContext is not AgendaItem item)
                return;

            var dialog = new ContentDialog
            {
                Title = _loader.GetStringOrDefault("TextDeleteTask") ?? "Delete Task",
                Content = string.Format(_loader.GetStringOrDefault("TextDeleteTaskContent") ?? "Delete \"{0}\"?", item.Title),
                PrimaryButtonText = _loader.GetStringOrDefault("CalendarDialog.SecondaryButtonText") ?? "Delete",
                CloseButtonText = _loader.GetStringOrDefault("CalendarDialog.CloseButtonText") ?? "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            try
            {
                button.IsEnabled = false;

                if (!string.IsNullOrWhiteSpace(item.Provider) && !string.IsNullOrWhiteSpace(item.Id))
                    await _syncManager.DeleteItemAsync(item.Provider, item.Id, isEvent: false);

                RemoveTaskFromCache(item);
                await _syncManager.RemoveCachedItemAsync(item);
                ReloadTasks();
                App.MyFlyoutWindow?.ReloadFilters();
            }
            finally
            {
                button.IsEnabled = true;
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

        private void RemoveTaskFromCache(AgendaItem target)
        {
            foreach (var key in _localCache.DayItems.Keys.ToList())
            {
                _localCache.DayItems[key].RemoveAll(item => item.IsTask && IsSameTask(item, target));
                if (_localCache.DayItems[key].Count == 0)
                    _localCache.DayItems.Remove(key);
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

        private static AppCache BuildTaskCache(IEnumerable<AgendaItem> items)
        {
            var dayItems = items
                .Where(item => item.IsTask && !string.IsNullOrWhiteSpace(item.DateKey))
                .GroupBy(item => item.DateKey)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToList(),
                    StringComparer.Ordinal);

            return new AppCache
            {
                DayItems = dayItems,
                MarkedDates = dayItems.Keys.ToHashSet(StringComparer.Ordinal)
            };
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
                Title = _loader.GetStringOrDefault("TextRemoveAccount") ?? "Remove Account",
                Content = string.Format(_loader.GetStringOrDefault("TextRemoveAccountConfirm") ?? "Remove {0} account?", providerName),
                PrimaryButtonText = _loader.GetStringOrDefault("TextConfirm") ?? "Confirm",
                CloseButtonText = _loader.GetStringOrDefault("CalendarDialog.CloseButtonText") ?? "Cancel",
                XamlRoot = XamlRoot,
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            try
            {
                await _syncManager.RemoveAccountAsync(providerName);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Account removal failed: {ex.Message}");
                var errorDialog = new ContentDialog
                {
                    Title = _loader.GetStringOrDefault("TextRemoveAccountFailedTitle") ?? "Account Not Removed",
                    Content = _loader.GetStringOrDefault("TextRemoveAccountFailedContent") ?? "Authorization data could not be completely removed. The account was kept so you can try again.",
                    CloseButtonText = _loader.GetStringOrDefault("CalendarDialog.CloseButtonText") ?? "Close",
                    XamlRoot = XamlRoot
                };
                await errorDialog.ShowAsync();
                return;
            }
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
            AccountColumn.MinWidth = _isAccountPaneCollapsed ? 0 : 200;
            AccountColumn.Width = _isAccountPaneCollapsed ? new GridLength(0) : new GridLength(2, GridUnitType.Star);
            AccountPane.Visibility = _isAccountPaneCollapsed ? Visibility.Collapsed : Visibility.Visible;
            ToggleAccountPaneIcon.Glyph = _isAccountPaneCollapsed ? "\uE76C" : "\uE76B";
        }
    }

    internal static class ObservableCollectionSortExtensions
    {
        public static void SortInPlace<T>(this ObservableCollection<T> collection, Comparison<T> comparison)
        {
            if (collection is SuppressableObservableCollection<T> sc)
            {
                sc.SuppressRange(() =>
                {
                    var ordered = collection.ToList();
                    ordered.Sort(comparison);
                    collection.Clear();
                    foreach (var item in ordered)
                        collection.Add(item);
                });
            }
            else
            {
                var ordered = collection.ToList();
                ordered.Sort(comparison);
                collection.Clear();
                foreach (var item in ordered)
                    collection.Add(item);
            }
        }
    }

    public class SuppressableObservableCollection<T> : ObservableCollection<T>
    {
        private bool _suppressNotification;

        public void SuppressRange(Action action)
        {
            _suppressNotification = true;
            try { action(); }
            finally
            {
                _suppressNotification = false;
                OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
            }
        }

        protected override void OnCollectionChanged(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (!_suppressNotification) base.OnCollectionChanged(e);
        }
    }
}
