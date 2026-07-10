using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Task_Flyout.Models;
using Task_Flyout.Services;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Globalization;
using Windows.UI; // 统一引入 Color

namespace Task_Flyout.Views
{
    public class ProviderToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string? colorHex = value as string;
            if (!string.IsNullOrEmpty(colorHex) && colorHex.StartsWith("#"))
                return new SolidColorBrush(Services.ColorHelper.ParseHex(colorHex));

            return new SolidColorBrush(Color.FromArgb(255, 150, 150, 150));
        }
        public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
    }

    public class ProviderToTextBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string? colorHex = value as string;
            if (!string.IsNullOrEmpty(colorHex) && colorHex.StartsWith("#"))
            {
                return Services.ColorHelper.ShouldUseWhiteText(colorHex)
                    ? new SolidColorBrush(Microsoft.UI.Colors.White)
                    : new SolidColorBrush(Microsoft.UI.Colors.Black);
            }
            return new SolidColorBrush(Microsoft.UI.Colors.White);
        }
        public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
    }

    public class BoolToTodayBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isToday && isToday)
            {
                if (Application.Current.Resources.TryGetValue("SystemAccentColorLight3", out var res))
                    return new SolidColorBrush((Color)res);
            }
            return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
        public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
    }

    public class BoolToOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language) => (value is bool b && b) ? 1.0 : 0.3;
        public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
    }

    public class BoolToStrikethroughConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language) =>
            value is bool isCompleted && isCompleted ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None;
        public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
    }

    public class TaskCompletedOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language) =>
            (value is bool isCompleted && isCompleted) ? 0.5 : 1.0; // 完成后透明度变为50%(变灰)
        public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
    }

    public sealed partial class CalendarPage : Page
    {
        public ObservableCollection<DayCellViewModel> DayCells { get; set; } = new();
        public ObservableCollection<AgendaItem> SelectedDayItems { get; set; } = new();

        private DateTime _viewDate = DateTime.Today;
        private SyncManager? _syncManager;
        private AppCache _localCache = new();
        private AgendaItem? _itemBeingEdited;
        private ResourceLoader _loader;
        private bool _isAccountPaneCollapsed;
        private bool _isTimelinePaneCollapsed;
        private DateTimeOffset? _lastCalendarSyncSucceededAt;

        private void TaskCheckBox_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
        }

        public CalendarPage()
        {
            this.InitializeComponent();
            this.Language = Windows.Globalization.ApplicationLanguages.Languages[0];
            _loader = new ResourceLoader();
            SetWeekdayHeaders();
            if (Application.Current is App app) _syncManager = app.SyncManager;
            this.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(Global_PointerWheelChanged), handledEventsToo: true);
            this.Loaded += (s, e) =>
            {
                RefreshAccountList();
                LoadCalendar(_viewDate);
            };
            this.Unloaded += (_, _) =>
            {
                _syncCts?.Cancel();
                _syncCts?.Dispose();
                _syncCts = null;
            };
        }

        // Fill the column headers (Sunday-first, matching the grid layout) from the
        // app-language culture's abbreviated day names so they follow the in-app
        // language rather than the OS locale or a missing resw entry.
        private void SetWeekdayHeaders()
        {
            var headers = new[] { WeekHeader0, WeekHeader1, WeekHeader2, WeekHeader3, WeekHeader4, WeekHeader5, WeekHeader6 };
            var dayNames = LocalizationHelper.AppCulture.DateTimeFormat.AbbreviatedDayNames; // index 0 == Sunday
            for (int i = 0; i < headers.Length && i < dayNames.Length; i++)
                headers[i].Text = dayNames[i];
        }

        private void LoadCache(DateTime date)
        {
            var firstOfMonth = new DateTime(date.Year, date.Month, 1);
            var start = firstOfMonth.AddDays(-(int)firstOfMonth.DayOfWeek);
            var end = start.AddDays(42 + 90);
            _localCache = _syncManager?.GetRangeCacheSnapshot(start, end) ?? new AppCache();
        }

        private void LoadCalendar(DateTime date)
        {
            if (BtnMonthYear == null || CalendarGrid == null) return;

            LoadCache(date);
            DayCells.Clear();
            BtnMonthYear.Content = date.ToString("Y", LocalizationHelper.AppCulture);

            DateTime firstOfEntry = new DateTime(date.Year, date.Month, 1);
            int offset = (int)firstOfEntry.DayOfWeek;
            DateTime startDate = firstOfEntry.AddDays(-offset);

            int daysInMonth = DateTime.DaysInMonth(date.Year, date.Month);
            int totalCells = offset + daysInMonth;

            int cellsToGenerate = (int)Math.Ceiling(totalCells / 7.0) * 7;

            for (int i = 0; i < cellsToGenerate; i++)
            {
                var current = startDate.AddDays(i);
                var cell = new DayCellViewModel { Date = current, IsCurrentMonth = current.Month == date.Month };

                string key = current.ToString("yyyy-MM-dd");
                if (_localCache.DayItems.ContainsKey(key))
                {
                    foreach (var item in _localCache.DayItems[key].Where(IsItemVisible))
                    {
                        PopulateItemColor(item);
                        cell.Items.Add(item);
                    }
                }
                DayCells.Add(cell);
            }

            var targetCell = DayCells.FirstOrDefault(c => c.IsToday) ?? DayCells.FirstOrDefault(c => c.Date.Month == date.Month && c.Date.Day == 1);
            if (targetCell != null)
            {
                CalendarGrid.SelectedItem = targetCell;
                UpdateSideBar(targetCell);
            }

            _ = SyncMonthDataAsync();
        }

        private System.Threading.CancellationTokenSource? _syncCts;

        private async Task SyncMonthDataAsync(bool forceRefresh = false)
        {
            if (_syncManager == null || SyncProgress == null) return;

            var accountMgr = (App.Current as App)?.SyncManager?.AccountManager;
            if (accountMgr == null || accountMgr.Accounts.Count == 0)
            {
                UpdateAccountEmptyState();
                return;
            }

            _syncCts?.Cancel();
            _syncCts = new System.Threading.CancellationTokenSource();
            var token = _syncCts.Token;

            SyncProgress.IsActive = true;
            SetCalendarStatus(_loader.GetStringOrDefault("TextLoading") ?? "Loading");

            try
            {
                if (DayCells.Count == 0) return;

                // A rapid mouse wheel/month navigation should settle before it starts a
                // provider request. Providers do not all expose cancellation yet.
                await Task.Delay(TimeSpan.FromMilliseconds(200), token);
                if (token.IsCancellationRequested) return;

                var start = DayCells.First().Date;
                var end = DayCells.Last().Date.AddDays(1);

                var allItems = await _syncManager.GetAllDataAsync(start, end, forceRefresh, token);

                if (token.IsCancellationRequested) return;

                if (CalendarGrid == null) return;

                var itemsByDate = allItems
                    .Where(IsItemVisible)
                    .GroupBy(it => it.DateKey)
                    .ToDictionary(g => g.Key, g => g.ToList());
                _localCache = new AppCache
                {
                    DayItems = itemsByDate.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value.ToList(),
                        StringComparer.Ordinal),
                    MarkedDates = itemsByDate.Keys.ToHashSet(StringComparer.Ordinal)
                };

                foreach (var cell in DayCells)
                {
                    cell.Items.Clear();
                    if (itemsByDate.TryGetValue(cell.Date.ToString("yyyy-MM-dd"), out var dayItems))
                    {
                        foreach (var item in dayItems)
                        {
                            PopulateItemColor(item);
                            cell.Items.Add(item);
                        }
                    }
                }

                if (CalendarGrid.SelectedItem is DayCellViewModel selectedCell)
                    UpdateSideBar(selectedCell);

                _lastCalendarSyncSucceededAt = DateTimeOffset.Now;
                SetCalendarStatus(string.Format(_loader.GetStringOrDefault("TextLastSync") ?? "Last sync: {0}", _lastCalendarSyncSucceededAt.Value.LocalDateTime.ToString("g")));
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested) return;
                System.Diagnostics.Debug.WriteLine($"Sync error: {ex.Message}");
                SetCalendarStatus(StatusMessageFormatter.Format(_loader.GetStringOrDefault("TextSyncFailed") ?? "Sync failed", _lastCalendarSyncSucceededAt, includeLastSuccess: true), isError: true);
            }
            finally
            {
                if (SyncProgress != null && !token.IsCancellationRequested)
                    SyncProgress.IsActive = false;
            }
        }

        private void Global_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(CalendarGrid);
            if (point.Position.X >= 0 && point.Position.X <= CalendarGrid.ActualWidth &&
                point.Position.Y >= 0 && point.Position.Y <= CalendarGrid.ActualHeight)
            {
                var delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
                if (delta > 0) BtnPrevMonth_Click(sender, new RoutedEventArgs());
                else if (delta < 0) BtnNextMonth_Click(sender, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void CalendarGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (CalendarGrid.ItemsPanelRoot is ItemsWrapGrid wrapGrid)
            {
                wrapGrid.ItemWidth = Math.Max(80, e.NewSize.Width / 7.0);
                wrapGrid.ItemHeight = Math.Max(80, e.NewSize.Height / 6.0);
            }
        }

        private int _flyoutYear;
        private void MonthYearFlyout_Opened(object sender, object e)
        {
            _flyoutYear = _viewDate.Year;
            FlyoutYearText.Text = string.Format(_loader.GetStringOrDefault("TextYearFormat") ?? "{0}", _flyoutYear);

            FlyoutMonthGrid.ItemsSource = LocalizationHelper.AppCulture.DateTimeFormat.MonthNames
                .Where(m => !string.IsNullOrEmpty(m)).ToArray();
        }
        private void FlyoutPrevYear_Click(object sender, RoutedEventArgs e) => FlyoutYearText.Text = string.Format(_loader.GetStringOrDefault("TextYearFormat") ?? "{0}", --_flyoutYear);
        private void FlyoutNextYear_Click(object sender, RoutedEventArgs e) => FlyoutYearText.Text = string.Format(_loader.GetStringOrDefault("TextYearFormat") ?? "{0}", ++_flyoutYear);
        private void FlyoutMonthGrid_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is string monthStr)
            {
                var months = LocalizationHelper.AppCulture.DateTimeFormat.MonthNames;
                int month = Array.IndexOf(months, monthStr) + 1;
                _viewDate = new DateTime(_flyoutYear, month, 1);
                LoadCalendar(_viewDate);
                MonthYearFlyout.Hide();
            }
        }

        private void BtnPrevMonth_Click(object sender, RoutedEventArgs e) => LoadCalendar(_viewDate = _viewDate.AddMonths(-1));
        private void BtnNextMonth_Click(object sender, RoutedEventArgs e) => LoadCalendar(_viewDate = _viewDate.AddMonths(1));
        private void BtnToday_Click(object sender, RoutedEventArgs e) => LoadCalendar(_viewDate = DateTime.Today);

        private void ToggleAccountPane_Click(object sender, RoutedEventArgs e)
        {
            _isAccountPaneCollapsed = !_isAccountPaneCollapsed;
            AccountColumn.MinWidth = _isAccountPaneCollapsed ? 0 : 200;
            AccountColumn.Width = _isAccountPaneCollapsed ? new GridLength(0) : new GridLength(2, GridUnitType.Star);
            AccountPane.Visibility = _isAccountPaneCollapsed ? Visibility.Collapsed : Visibility.Visible;
            ToggleAccountPaneIcon.Glyph = _isAccountPaneCollapsed ? "\uE76C" : "\uE76B";
        }

        private void ToggleTimelinePane_Click(object sender, RoutedEventArgs e)
        {
            _isTimelinePaneCollapsed = !_isTimelinePaneCollapsed;
            TimelineColumn.MinWidth = _isTimelinePaneCollapsed ? 0 : 280;
            TimelineColumn.Width = _isTimelinePaneCollapsed ? new GridLength(0) : new GridLength(3, GridUnitType.Star);
            TimelinePane.Visibility = _isTimelinePaneCollapsed ? Visibility.Collapsed : Visibility.Visible;
            ToggleTimelinePaneIcon.Glyph = _isTimelinePaneCollapsed ? "\uE76B" : "\uE76C";
        }

        public void RefreshAccountList()
        {
            _ = RefreshAccountListAsync();
        }

        private async Task RefreshAccountListAsync()
        {
            try
            {
                if (_syncManager == null || AccountListRepeater == null) return;
                var mgr = _syncManager.AccountManager;
                UpdateAccountEmptyState();

                if (!ReferenceEquals(AccountListRepeater.ItemsSource, mgr.Accounts))
                    AccountListRepeater.ItemsSource = mgr.Accounts;

                await _syncManager.SyncAllCalendarsAsync();
                if (!ReferenceEquals(AccountListRepeater.ItemsSource, mgr.Accounts))
                    AccountListRepeater.ItemsSource = mgr.Accounts;
                UpdateAccountEmptyState();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Refresh account list failed: {ex.Message}");
            }
        }

        private void UpdateAccountEmptyState()
        {
            if (NoAccountEmptyState == null || CalendarGrid == null) return;

            var accountMgr = (App.Current as App)?.SyncManager?.AccountManager;
            bool hasAccounts = accountMgr != null && accountMgr.Accounts.Count > 0;
            NoAccountEmptyState.Visibility = hasAccounts ? Visibility.Collapsed : Visibility.Visible;
            CalendarGrid.Opacity = hasAccounts ? 1.0 : 0.25;
            CalendarGrid.IsHitTestVisible = hasAccounts;
        }

        private void BtnAddAccount_Click(object sender, RoutedEventArgs e)
        {
            App.MyMainWindow?.NavigateToAddAccount();
        }

        private async void BtnRemoveAccount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string providerName) return;

            var dialog = new ContentDialog
            {
                Title = _loader.GetStringOrDefault("TextRemoveAccountTitle") ?? "Remove Account",
                Content = string.Format(_loader.GetStringOrDefault("TextRemoveAccountContent") ?? "Are you sure you want to remove the {0} account?", providerName),
                PrimaryButtonText = _loader.GetStringOrDefault("TextConfirm") ?? "Confirm",
                CloseButtonText = _loader.GetStringOrDefault("CalendarDialog.CloseButtonText") ?? "Cancel",
                XamlRoot = XamlRoot,
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            try
            {
                if (_syncManager != null)
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
            LoadCalendar(_viewDate);
            App.MyFlyoutWindow?.ReloadFilters();
        }

        private void AccountToggle_Toggled(object sender, RoutedEventArgs e)
        {
            _syncManager?.AccountManager.Save();
            ReloadFilters();
            App.MyFlyoutWindow?.ReloadFilters();
        }

        private void CalendarToggle_Toggled(object sender, RoutedEventArgs e)
        {
            _syncManager?.AccountManager.Save();
            ReloadFilters();
            App.MyFlyoutWindow?.ReloadFilters();
        }

        private void BtnForceSync_Click(object sender, RoutedEventArgs e)
        {
            ForceSync();
        }

        private void CalendarGrid_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is DayCellViewModel cell) UpdateSideBar(cell);
        }

        private void UpdateSideBar(DayCellViewModel cell)
        {
            TxtSideBarDate.Text = cell.Date.ToString("M", LocalizationHelper.AppCulture) + " " + (_loader.GetStringOrDefault("TextOnwards") ?? "");
            SelectedDayItems.Clear();

            string selectedDateKey = cell.Date.ToString("yyyy-MM-dd");
            var upcomingDates = _localCache.DayItems.Keys
                .Where(k => string.Compare(k, selectedDateKey) >= 0)
                .OrderBy(k => k);

            bool hasItems = false;
            foreach (var dateKey in upcomingDates)
            {
                var sortedItems = _localCache.DayItems[dateKey]
                    .Where(IsItemVisible)
                    .OrderBy(i => (i.Subtitle == "全天" || i.Subtitle == (_loader.GetStringOrDefault("TextAllDay") ?? "All Day")) ? 0 : 1)
                    .ThenBy(i => i.Subtitle);
                foreach (var item in sortedItems)
                {
                    var displayItem = new AgendaItem
                    {
                        Id = item.Id,
                        Title = item.Title,
                        Subtitle = $"{DateTime.Parse(dateKey).ToString("M", LocalizationHelper.AppCulture)}\n{(item.Subtitle == "全天" || item.Subtitle == "All Day" ? (_loader.GetStringOrDefault("TextAllDay") ?? "All Day") : item.Subtitle)}",
                        Location = item.Location,
                        Description = item.Description,
                        IsEvent = item.IsEvent,
                        IsTask = item.IsTask,
                        IsCompleted = item.IsCompleted,
                        Provider = item.Provider,
                        CalendarId = item.CalendarId,
                        CalendarName = item.CalendarName,
                        ColorHex = item.ColorHex,
                        DateKey = item.DateKey,
                        StartDateTime = item.StartDateTime,
                        EndDateTime = item.EndDateTime,
                        IsRecurring = item.IsRecurring,
                        RecurringEventId = item.RecurringEventId,
                        RecurrenceKind = item.RecurrenceKind
                    };
                    PopulateItemColor(displayItem);
                    SelectedDayItems.Add(displayItem);
                    hasItems = true;
                }
            }

            if (!hasItems)
            {
                SelectedDayItems.Add(new AgendaItem
                {
                    Title = _loader.GetStringOrDefault("TextNoAgendaTitle") ?? "No upcoming events",
                    Subtitle = "-",
                    IsEvent = true
                });
            }
        }

        private async void TaskCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is AgendaItem item && item.IsTask)
            {
                try
                {
                    cb.IsEnabled = false;
                    item.IsCompleted = (cb.IsChecked == true);
                    if (_syncManager != null)
                        await _syncManager.UpdateTaskStatusAsync(item.Provider, item.Id, item.IsCompleted);
                    _ = SyncMonthDataAsync(forceRefresh: true);
                }
                catch { item.IsCompleted = !item.IsCompleted; }
                finally { cb.IsEnabled = true; }
            }
        }

        private void EditChkAllDay_Changed(object sender, RoutedEventArgs e)
        {
            if (EditTimePanel != null)
            {
                EditTimePanel.Visibility = EditChkAllDay.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private bool IsItemVisible(AgendaItem item)
        {
            var accountMgr = (App.Current as App)?.SyncManager?.AccountManager;
            if (accountMgr == null) return true;
            return accountMgr.IsItemVisible(item);
        }

        private void PopulateItemColor(AgendaItem item)
        {
            var accountMgr = (App.Current as App)?.SyncManager?.AccountManager;
            accountMgr?.PopulateItemColor(item);
        }

        public void ReloadFilters()
        {
            LoadCalendar(_viewDate);
        }

        public void ForceSync()
        {
            _ = ForceSyncAllDataAsync();
        }

        private async Task ForceSyncAllDataAsync()
        {
            if (_syncManager == null || SyncProgress == null) return;

            SyncProgress.IsActive = true;
            SetCalendarStatus(_loader.GetStringOrDefault("TextLoading") ?? "Loading");
            try
            {
                var min = DateTime.Today.AddYears(-1);
                var max = DateTime.Today.AddYears(3);
                await _syncManager.GetAllDataAsync(min, max, forceRefresh: true);
                _lastCalendarSyncSucceededAt = DateTimeOffset.Now;
                LoadCache(_viewDate);
                LoadCalendar(_viewDate);
                SetCalendarStatus(string.Format(_loader.GetStringOrDefault("TextLastSync") ?? "Last sync: {0}", _lastCalendarSyncSucceededAt.Value.LocalDateTime.ToString("g")));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Force sync error: {ex.Message}");
                SetCalendarStatus(StatusMessageFormatter.Format(_loader.GetStringOrDefault("TextSyncFailed") ?? "Sync failed", _lastCalendarSyncSucceededAt, includeLastSuccess: true), isError: true);
            }
            finally
            {
                SyncProgress.IsActive = false;
            }
        }

        private void SetCalendarStatus(string message, bool isError = false)
        {
            if (CalendarStatusText == null) return;
            CalendarStatusText.Text = message;
            CalendarStatusText.Foreground = isError
                ? new SolidColorBrush(Microsoft.UI.Colors.IndianRed)
                : (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        }

        private void EditRadioType_Changed(object? sender, RoutedEventArgs? e)
        {
            if (EditTxtLocation == null || EditEndTimePicker == null) return;

            bool isEvent = EditRadioEvent.IsChecked == true;
            EditTxtLocation.Visibility = isEvent ? Visibility.Visible : Visibility.Collapsed;
            EditEndTimePicker.Visibility = isEvent ? Visibility.Visible : Visibility.Collapsed;
            EditRecurrenceComboBox.Visibility = isEvent ? Visibility.Visible : Visibility.Collapsed;
            if (!isEvent) EditRecurrenceComboBox.SelectedIndex = 0;
            EditStartTimePicker.Header = isEvent ? (_loader.GetStringOrDefault("TextStartTime") ?? "Start time") : (_loader.GetStringOrDefault("TextDueTime") ?? "Due time");
        }

        private void SetupEditProviderComboBox(string? forceSelectProvider = null)
        {
            EditCmbProvider.Items.Clear();
            var accountMgr = (App.Current as App)?.SyncManager?.AccountManager;

            if (accountMgr != null)
            {
                foreach (var acct in accountMgr.Accounts)
                    EditCmbProvider.Items.Add(new ComboBoxItem { Content = acct.ProviderName, Tag = acct.ProviderName });
            }
            if (forceSelectProvider != null && !EditCmbProvider.Items.OfType<ComboBoxItem>().Any(i => i.Tag.ToString() == forceSelectProvider))
                EditCmbProvider.Items.Add(new ComboBoxItem { Content = forceSelectProvider, Tag = forceSelectProvider });

            if (EditCmbProvider.Items.Count > 1)
            {
                EditCmbProvider.Visibility = Visibility.Visible;
                if (forceSelectProvider != null)
                {
                    var item = EditCmbProvider.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag.ToString() == forceSelectProvider);
                    if (item != null) EditCmbProvider.SelectedItem = item;
                }
                else EditCmbProvider.SelectedIndex = 0;
            }
            else
            {
                EditCmbProvider.Visibility = Visibility.Collapsed;
                if (EditCmbProvider.Items.Count > 0) EditCmbProvider.SelectedIndex = 0;
            }
        }

        private void BtnAddNew_Click(object sender, RoutedEventArgs e)
        {
            _itemBeingEdited = null;
            EditDialog.Title = _loader.GetStringOrDefault("TextNewItem") ?? "New Event / Task";
            EditDialog.SecondaryButtonText = "";
            SetupEditProviderComboBox();
            EditCmbProvider.IsEnabled = true;
            EditRadioEvent.IsEnabled = true;
            EditRadioTask.IsEnabled = true;
            EditRecurrenceComboBox.SelectedIndex = 0;
            EditRecurrenceComboBox.IsEnabled = true;

            EditTxtTitle.Text = "";
            EditTxtLocation.Text = "";
            EditTxtDescription.Text = "";
            EditDatePicker.Date = _viewDate.Year == DateTime.Today.Year && _viewDate.Month == DateTime.Today.Month ? DateTime.Today : new DateTime(_viewDate.Year, _viewDate.Month, 1);

            EditChkAllDay.IsChecked = false;
            EditStartTimePicker.SelectedTime = null;
            EditEndTimePicker.SelectedTime = null;

            EditDialog.XamlRoot = this.XamlRoot;
            _ = EditDialog.ShowAsync();
        }

        private void PrepareDialogForEdit(AgendaItem item)
        {
            _itemBeingEdited = item;
            EditDialog.Title = _loader.GetStringOrDefault("CalendarDialog.Title") ?? "Edit Event / Task";
            EditDialog.SecondaryButtonText = _loader.GetStringOrDefault("CalendarDialog.SecondaryButtonText") ?? "Delete";

            EditTxtTitle.Text = item.Title;
            EditTxtLocation.Text = item.Location;
            EditTxtDescription.Text = item.Description;

            SetupEditProviderComboBox(item.Provider);
            EditCmbProvider.IsEnabled = false;

            EditRadioEvent.IsChecked = item.IsEvent;
            EditRadioTask.IsChecked = item.IsTask;
            EditRadioEvent.IsEnabled = false;
            EditRadioTask.IsEnabled = false;
            SelectRecurrenceKind(item.RecurrenceKind);
            EditRecurrenceComboBox.IsEnabled = false;

            if (DateTime.TryParse(item.DateKey, out var d)) EditDatePicker.Date = d;

            var timePart = item.Subtitle?.Split('\n').LastOrDefault()?.Trim();
            if (timePart == _loader.GetStringOrDefault("TextAllDay") || string.IsNullOrEmpty(timePart))
            {
                EditChkAllDay.IsChecked = true;
                EditStartTimePicker.SelectedTime = null;
                EditEndTimePicker.SelectedTime = null;
            }
            else
            {
                EditChkAllDay.IsChecked = false;
                var times = timePart.Split('-');
                if (times.Length >= 1 && TimeSpan.TryParse(times[0].Trim(), out var st)) EditStartTimePicker.SelectedTime = st;
                if (times.Length >= 2 && TimeSpan.TryParse(times[1].Trim(), out var et)) EditEndTimePicker.SelectedTime = et;
                else if (EditStartTimePicker.SelectedTime.HasValue) EditEndTimePicker.SelectedTime = EditStartTimePicker.SelectedTime.Value.Add(TimeSpan.FromHours(1));
            }

            EditRadioType_Changed(null, null);
        }

        private async void AgendaCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (e.OriginalSource is FrameworkElement src && (src is CheckBox || src.Parent is CheckBox)) return;
            if ((sender as FrameworkElement)?.DataContext is AgendaItem item)
            {
                if (item.Title != null && item.Title.Contains(_loader.GetStringOrDefault("TextNoAgendaTitle") ?? "No upcoming events")) return;
                PrepareDialogForEdit(item);
                EditDialog.XamlRoot = this.XamlRoot;
                await EditDialog.ShowAsync();
            }
        }

        public void OpenEditDialogFromExternal(AgendaItem item)
        {
            Action showDialog = async () =>
            {
                PrepareDialogForEdit(item);
                EditDialog.XamlRoot = this.XamlRoot;
                try { await EditDialog.ShowAsync(); } catch { }
            };
            if (this.XamlRoot == null) this.Loaded += (s, e) => showDialog(); else showDialog();
        }

        private async void EditDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (string.IsNullOrWhiteSpace(EditTxtTitle.Text))
            {
                args.Cancel = true;
                EditValidationText.Text = _loader.GetStringOrDefault("TextTitleRequired") ?? "A title is required.";
                EditValidationText.Visibility = Visibility.Visible;
                EditTxtTitle.Focus(FocusState.Programmatic);
                return;
            }

            args.Cancel = true;
            var deferral = args.GetDeferral();
            try
            {
                sender.IsPrimaryButtonEnabled = false;
                sender.IsSecondaryButtonEnabled = false;
                EditValidationText.Visibility = Visibility.Collapsed;

                var newDateKey = EditDatePicker.Date.ToString("yyyy-MM-dd");
                TimeSpan? newStartTime = EditChkAllDay.IsChecked == true ? null : EditStartTimePicker.SelectedTime;
                TimeSpan? newEndTime = EditChkAllDay.IsChecked == true ? null : EditEndTimePicker.SelectedTime;
                string newSubtitleText = _loader.GetStringOrDefault("TextAllDay") ?? "All Day";
                if (newStartTime.HasValue && newEndTime.HasValue) newSubtitleText = $"{newStartTime.Value:hh\\:mm} - {newEndTime.Value:hh\\:mm}";
                else if (newStartTime.HasValue) newSubtitleText = $"{newStartTime.Value:hh\\:mm}";

                if (_itemBeingEdited == null)
                {
                    if (_syncManager != null)
                    {
                        await _syncManager.CreateItemAsync(
                            EditTxtTitle.Text, EditRadioEvent.IsChecked == true, EditChkAllDay.IsChecked == true,
                            EditDatePicker.Date.DateTime, newStartTime ?? TimeSpan.Zero, newEndTime ?? TimeSpan.Zero,
                            EditTxtLocation.Text, GetSelectedRecurrence(),
                            (EditCmbProvider.SelectedItem as ComboBoxItem)?.Tag.ToString() ?? "Google");
                    }
                }
                else if (_syncManager != null && !string.IsNullOrEmpty(_itemBeingEdited.Id))
                {
                    // Update the cloud item first; failed requests must not make the UI imply success.
                    await _syncManager.UpdateItemAsync(
                        _itemBeingEdited.Provider, _itemBeingEdited.Id, _itemBeingEdited.IsEvent,
                        EditTxtTitle.Text, EditTxtLocation.Text, EditTxtDescription.Text,
                        EditDatePicker.Date.DateTime, newStartTime, newEndTime);

                    if (_localCache.DayItems.TryGetValue(_itemBeingEdited.DateKey, out var oldList))
                    {
                        var original = oldList.FirstOrDefault(x => x.Id == _itemBeingEdited.Id);
                        if (original != null)
                        {
                            var oldDateKey = original.DateKey;
                            oldList.Remove(original);
                            original.Title = EditTxtTitle.Text;
                            original.Location = EditTxtLocation.Text;
                            original.Description = EditTxtDescription.Text;
                            original.DateKey = newDateKey;
                            original.Subtitle = newSubtitleText;
                            if (!_localCache.DayItems.ContainsKey(newDateKey)) _localCache.DayItems[newDateKey] = new List<AgendaItem>();
                            _localCache.DayItems[newDateKey].Add(original);
                            await _syncManager.UpsertCachedItemAsync(original, oldDateKey);
                        }
                    }
                }

                LoadCalendar(_viewDate);
                _ = SyncMonthDataAsync(forceRefresh: true);
                sender.Hide();
            }
            catch (Exception ex)
            {
                EditValidationText.Text = UserSafeErrorMessage.FromException(ex, _loader.GetStringOrDefault("TextSaveFailed") ?? "Unable to save. Please try again.");
                EditValidationText.Visibility = Visibility.Visible;
                SetCalendarStatus(EditValidationText.Text, isError: true);
            }
            finally
            {
                sender.IsPrimaryButtonEnabled = true;
                sender.IsSecondaryButtonEnabled = _itemBeingEdited != null;
                deferral.Complete();
            }
        }

        private void EditDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (_itemBeingEdited == null) return;

            var itemToDelete = _itemBeingEdited;
            args.Cancel = true;
            sender.Hide();

            _ = DispatcherQueue.TryEnqueue(async () =>
            {
                await Task.Delay(120);
                await DeleteAgendaItemWithPromptAsync(itemToDelete);
            });
        }

        private async Task DeleteAgendaItemWithPromptAsync(AgendaItem itemToDelete)
        {
            var deleteMode = await GetRecurringDeleteModeAsync(itemToDelete);
            if (deleteMode == null)
            {
                    return;
            }

            if (_syncManager != null && !string.IsNullOrEmpty(itemToDelete.Id))
            {
                try
                {
                    DateTime? occurrenceDate = itemToDelete.StartDateTime;
                    if (!occurrenceDate.HasValue && DateTime.TryParse(itemToDelete.DateKey, out var parsedDate))
                        occurrenceDate = parsedDate;

                    await _syncManager.DeleteItemAsync(
                        itemToDelete.Provider,
                        itemToDelete.Id,
                        itemToDelete.IsEvent,
                        deleteMode.Value,
                        occurrenceDate,
                        itemToDelete.RecurringEventId);
                }
                catch (Exception ex)
                {
                    SetCalendarStatus(UserSafeErrorMessage.FromException(ex, _loader.GetStringOrDefault("TextDeleteFailed") ?? "Unable to delete. Please try again."), isError: true);
                    return;
                }
            }

            if (_localCache.DayItems.TryGetValue(itemToDelete.DateKey, out var list))
            {
                list.RemoveAll(x => x.Id == itemToDelete.Id);
                if (_syncManager != null)
                    await _syncManager.RemoveCachedItemAsync(itemToDelete);
            }
            LoadCalendar(_viewDate);
            _ = SyncMonthDataAsync(forceRefresh: true);
        }

        private EventRecurrenceKind GetSelectedRecurrence()
        {
            if (EditRadioEvent.IsChecked != true) return EventRecurrenceKind.None;
            if (EditRecurrenceComboBox.SelectedItem is ComboBoxItem item &&
                Enum.TryParse<EventRecurrenceKind>(item.Tag?.ToString(), out var recurrence))
                return recurrence;
            return EventRecurrenceKind.None;
        }

        private void SelectRecurrenceKind(string recurrenceKind)
        {
            var selected = EditRecurrenceComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), recurrenceKind, StringComparison.OrdinalIgnoreCase));
            EditRecurrenceComboBox.SelectedItem = selected ?? EditRecurrenceComboBox.Items.FirstOrDefault();
        }

        private async Task<RecurringDeleteMode?> GetRecurringDeleteModeAsync(AgendaItem item)
        {
            if (!item.IsEvent || !item.IsRecurring)
                return await ConfirmSingleDeleteAsync() ? RecurringDeleteMode.Single : null;

            RecurringDeleteMode? selectedMode = null;
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = _loader.GetStringOrDefault("TextDeleteRecurringTitle") ?? "Delete Recurring Event",
                CloseButtonText = _loader.GetStringOrDefault("CalendarDialog.CloseButtonText") ?? "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock
            {
                Text = _loader.GetStringOrDefault("TextDeleteRecurringPrompt") ?? "Select the scope of deletion.",
                TextWrapping = TextWrapping.Wrap
            });

            var singleButton = CreateDeleteModeButton(_loader.GetStringOrDefault("TextDeleteThisEvent") ?? "Delete this event only");
            singleButton.Click += (_, _) =>
            {
                selectedMode = RecurringDeleteMode.Single;
                dialog.Hide();
            };

            var followingButton = CreateDeleteModeButton(_loader.GetStringOrDefault("TextDeleteThisAndFollowing") ?? "Delete this and following events");
            followingButton.Click += (_, _) =>
            {
                selectedMode = RecurringDeleteMode.ThisAndFollowing;
                dialog.Hide();
            };

            var allButton = CreateDeleteModeButton(_loader.GetStringOrDefault("TextDeleteAllRecurring") ?? "Delete all recurring events");
            allButton.Click += (_, _) =>
            {
                selectedMode = RecurringDeleteMode.All;
                dialog.Hide();
            };

            panel.Children.Add(singleButton);
            panel.Children.Add(followingButton);
            panel.Children.Add(allButton);
            dialog.Content = panel;

            await dialog.ShowAsync();
            return selectedMode;
        }

        private async Task<bool> ConfirmSingleDeleteAsync()
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = _loader.GetStringOrDefault("TextDeleteAgendaItem") ?? "Delete item?",
                Content = _loader.GetStringOrDefault("TextDeleteAgendaItemContent") ?? "This item will be removed from the connected account.",
                PrimaryButtonText = _loader.GetStringOrDefault("TextDelete") ?? "Delete",
                CloseButtonText = _loader.GetStringOrDefault("CalendarDialog.CloseButtonText") ?? "Cancel",
                DefaultButton = ContentDialogButton.Close
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        private static Button CreateDeleteModeButton(string text)
        {
            return new Button
            {
                Content = text,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
        }
    }
}
