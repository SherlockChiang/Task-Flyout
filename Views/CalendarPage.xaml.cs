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
            string colorHex = value as string;
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
            string colorHex = value as string;
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
        private SyncManager _syncManager;
        private AppCache _localCache = new();
        private AgendaItem _itemBeingEdited;
        private ResourceLoader _loader;
        private bool _isAccountPaneCollapsed;
        private bool _isTimelinePaneCollapsed;

        private void TaskCheckBox_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
        }

        public CalendarPage()
        {
            this.InitializeComponent();
            _loader = new ResourceLoader();
            if (Application.Current is App app) _syncManager = app.SyncManager;
            this.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(Global_PointerWheelChanged), handledEventsToo: true);
            LoadCache();
            this.Loaded += (s, e) =>
            {
                RefreshAccountList();
                LoadCalendar(_viewDate);
            };
        }

        private void LoadCache()
        {
            _localCache = _syncManager?.GetLocalCache() ?? new AppCache();
        }

        private void LoadCalendar(DateTime date)
        {
            if (BtnMonthYear == null || CalendarGrid == null) return;

            DayCells.Clear();
            BtnMonthYear.Content = date.ToString("Y", CultureInfo.CurrentUICulture);

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

        private System.Threading.CancellationTokenSource _syncCts;

        private async Task SyncMonthDataAsync(bool forceRefresh = false)
        {
            if (_syncManager == null || SyncProgress == null) return;

            var accountMgr = (App.Current as App)?.SyncManager?.AccountManager;
            if (accountMgr == null || accountMgr.Accounts.Count == 0)
            {
                return;
            }

            _syncCts?.Cancel();
            _syncCts = new System.Threading.CancellationTokenSource();
            var token = _syncCts.Token;

            SyncProgress.IsActive = true;

            try
            {
                if (DayCells.Count == 0) return;

                var start = DayCells.First().Date;
                var end = DayCells.Last().Date.AddDays(1);

                var allItems = await _syncManager.GetAllDataAsync(start, end, forceRefresh);

                if (token.IsCancellationRequested) return;

                if (CalendarGrid == null) return;

                _localCache = _syncManager.GetLocalCache();

                foreach (var cell in DayCells)
                {
                    cell.Items.Clear();
                    var dayItems = allItems.Where(it => it.DateKey == cell.Date.ToString("yyyy-MM-dd") && IsItemVisible(it));
                    foreach (var item in dayItems)
                    {
                        PopulateItemColor(item);
                        cell.Items.Add(item);
                    }
                }

                if (CalendarGrid.SelectedItem is DayCellViewModel selectedCell)
                    UpdateSideBar(selectedCell);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"数据同步异常: {ex.Message}");
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
                if (delta > 0) BtnPrevMonth_Click(null, null);
                else if (delta < 0) BtnNextMonth_Click(null, null);
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
            FlyoutYearText.Text = _flyoutYear.ToString();

            FlyoutMonthGrid.ItemsSource = CultureInfo.CurrentUICulture.DateTimeFormat.MonthNames
                .Where(m => !string.IsNullOrEmpty(m)).ToArray();
        }
        private void FlyoutPrevYear_Click(object sender, RoutedEventArgs e) => FlyoutYearText.Text = $"{--_flyoutYear}年";
        private void FlyoutNextYear_Click(object sender, RoutedEventArgs e) => FlyoutYearText.Text = $"{++_flyoutYear}年";
        private void FlyoutMonthGrid_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is string monthStr)
            {
                var months = CultureInfo.CurrentUICulture.DateTimeFormat.MonthNames;
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
            AccountColumn.Width = _isAccountPaneCollapsed ? new GridLength(0) : new GridLength(260);
            AccountPane.Visibility = _isAccountPaneCollapsed ? Visibility.Collapsed : Visibility.Visible;
            ToggleAccountPaneIcon.Glyph = _isAccountPaneCollapsed ? "\uE76C" : "\uE76B";
        }

        private void ToggleTimelinePane_Click(object sender, RoutedEventArgs e)
        {
            _isTimelinePaneCollapsed = !_isTimelinePaneCollapsed;
            TimelineColumn.Width = _isTimelinePaneCollapsed ? new GridLength(0) : new GridLength(340);
            TimelinePane.Visibility = _isTimelinePaneCollapsed ? Visibility.Collapsed : Visibility.Visible;
            ToggleTimelinePaneIcon.Glyph = _isTimelinePaneCollapsed ? "\uE76B" : "\uE76C";
        }

        public async void RefreshAccountList()
        {
            var mgr = _syncManager?.AccountManager;
            if (mgr == null || AccountListRepeater == null) return;

            AccountListRepeater.ItemsSource = null;
            AccountListRepeater.ItemsSource = mgr.Accounts;

            await _syncManager.SyncAllCalendarsAsync();
            AccountListRepeater.ItemsSource = null;
            AccountListRepeater.ItemsSource = mgr.Accounts;
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
                Title = _loader.GetString("TextRemoveAccountTitle") ?? "移除账户",
                Content = string.Format(_loader.GetString("TextRemoveAccountContent") ?? "确定要移除 {0} 账户吗？", providerName),
                PrimaryButtonText = _loader.GetString("TextConfirm") ?? "确定",
                CloseButtonText = _loader.GetString("CalendarDialog/CloseButtonText") ?? "取消",
                XamlRoot = XamlRoot,
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            if (_syncManager != null)
                await _syncManager.RemoveAccountAsync(providerName);
            RefreshAccountList();
            LoadCache();
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
            TxtSideBarDate.Text = cell.Date.ToString("M", CultureInfo.CurrentUICulture) + " " + _loader.GetString("TextOnwards");
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
                    .OrderBy(i => i.Subtitle == "全天" ? 0 : 1)
                    .ThenBy(i => i.Subtitle);
                foreach (var item in sortedItems)
                {
                    var displayItem = new AgendaItem
                    {
                        Id = item.Id,
                        Title = item.Title,
                        Subtitle = $"{DateTime.Parse(dateKey).ToString("M", CultureInfo.CurrentUICulture)}\n{(item.Subtitle == "全天" ? _loader.GetString("TextAllDay") : item.Subtitle)}",
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
                    Title = _loader.GetString("TextNoAgendaTitle") ?? "近期没有安排",
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
            _ = SyncMonthDataAsync(forceRefresh: true);
        }

        private void EditRadioType_Changed(object sender, RoutedEventArgs e)
        {
            if (EditTxtLocation == null || EditEndTimePicker == null) return;

            bool isEvent = EditRadioEvent.IsChecked == true;
            EditTxtLocation.Visibility = isEvent ? Visibility.Visible : Visibility.Collapsed;
            EditEndTimePicker.Visibility = isEvent ? Visibility.Visible : Visibility.Collapsed;
            EditRecurrenceComboBox.Visibility = isEvent ? Visibility.Visible : Visibility.Collapsed;
            if (!isEvent) EditRecurrenceComboBox.SelectedIndex = 0;
            EditStartTimePicker.Header = isEvent ? _loader.GetString("TextStartTime") : _loader.GetString("TextDueTime");
        }

        private void SetupEditProviderComboBox(string forceSelectProvider = null)
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
            EditDialog.Title = _loader.GetString("TextNewItem") ?? "新建";
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
            EditDialog.Title = _loader.GetString("CalendarDialog/Title") ?? "编辑";
            EditDialog.SecondaryButtonText = _loader.GetString("CalendarDialog/SecondaryButtonText") ?? "删除";

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
            if (timePart == _loader.GetString("TextAllDay") || string.IsNullOrEmpty(timePart))
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
                if (item.Title != null && item.Title.Contains(_loader.GetString("TextNoAgendaTitle") ?? "无安排")) return;
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
            if (string.IsNullOrWhiteSpace(EditTxtTitle.Text)) return;

            var newDateKey = EditDatePicker.Date.ToString("yyyy-MM-dd");
            TimeSpan? newStartTime = EditChkAllDay.IsChecked == true ? null : EditStartTimePicker.SelectedTime;
            TimeSpan? newEndTime = EditChkAllDay.IsChecked == true ? null : EditEndTimePicker.SelectedTime;

            string newSubtitleText = _loader.GetString("TextAllDay") ?? "全天";
            if (newStartTime.HasValue && newEndTime.HasValue) newSubtitleText = $"{newStartTime.Value:hh\\:mm} - {newEndTime.Value:hh\\:mm}";
            else if (newStartTime.HasValue) newSubtitleText = $"{newStartTime.Value:hh\\:mm}";

            bool isEvent = EditRadioEvent.IsChecked == true;
            bool isAllDay = EditChkAllDay.IsChecked == true;
            string providerName = (EditCmbProvider.SelectedItem as ComboBoxItem)?.Tag.ToString() ?? "Google";
            var recurrence = GetSelectedRecurrence();

            if (_itemBeingEdited == null)
            {
                try
                {
                    await _syncManager.CreateItemAsync(
                        EditTxtTitle.Text, isEvent, isAllDay, EditDatePicker.Date.DateTime,
                        newStartTime ?? TimeSpan.Zero, newEndTime ?? TimeSpan.Zero, EditTxtLocation.Text, recurrence, providerName);
                    _ = SyncMonthDataAsync(forceRefresh: true);
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"新建失败: {ex.Message}"); }
            }
            else
            {
                if (_localCache.DayItems.TryGetValue(_itemBeingEdited.DateKey, out var oldList))
                {
                    var orig = oldList.FirstOrDefault(x => x.Id == _itemBeingEdited.Id);
                    if (orig != null)
                    {
                        oldList.Remove(orig);
                        orig.Title = EditTxtTitle.Text;
                        orig.Location = EditTxtLocation.Text;
                        orig.Description = EditTxtDescription.Text;
                        orig.DateKey = newDateKey;
                        orig.Subtitle = newSubtitleText;
                        if (!_localCache.DayItems.ContainsKey(newDateKey)) _localCache.DayItems[newDateKey] = new List<AgendaItem>();
                        _localCache.DayItems[newDateKey].Add(orig);
                        _ = _syncManager.SaveLocalCacheAsync();
                    }
                }
                LoadCalendar(_viewDate);

                if (_syncManager != null && !string.IsNullOrEmpty(_itemBeingEdited.Id))
                {
                    try
                    {
                        await _syncManager.UpdateItemAsync(
                            _itemBeingEdited.Provider, _itemBeingEdited.Id, _itemBeingEdited.IsEvent,
                            EditTxtTitle.Text, EditTxtLocation.Text, EditTxtDescription.Text,
                            EditDatePicker.Date.DateTime, newStartTime, newEndTime);
                        _ = SyncMonthDataAsync(forceRefresh: true);
                    }
                    catch { }
                }
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

            if (_localCache.DayItems.TryGetValue(itemToDelete.DateKey, out var list))
                {
                list.RemoveAll(x => x.Id == itemToDelete.Id);
                    _ = _syncManager.SaveLocalCacheAsync();
                }
                LoadCalendar(_viewDate);

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
                        _ = SyncMonthDataAsync(forceRefresh: true);
                    }
                    catch { }
                }
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
                return RecurringDeleteMode.Single;

            RecurringDeleteMode? selectedMode = null;
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "删除重复日程",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close
            };

            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock
            {
                Text = "请选择要删除的范围。",
                TextWrapping = TextWrapping.Wrap
            });

            var singleButton = CreateDeleteModeButton("仅删除此事件");
            singleButton.Click += (_, _) =>
            {
                selectedMode = RecurringDeleteMode.Single;
                dialog.Hide();
            };

            var followingButton = CreateDeleteModeButton("删除此事件和之后事件");
            followingButton.Click += (_, _) =>
            {
                selectedMode = RecurringDeleteMode.ThisAndFollowing;
                dialog.Hide();
            };

            var allButton = CreateDeleteModeButton("删除所有重复事件");
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
