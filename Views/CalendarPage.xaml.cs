using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Task_Flyout.Models;
using Task_Flyout.Services;

namespace Task_Flyout.Views
{
    public class BoolToTodayBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isToday && isToday)
                if (Application.Current.Resources.TryGetValue("SystemAccentColorLight3", out var res))
                    return new SolidColorBrush((Windows.UI.Color)res);
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

    public sealed partial class CalendarPage : Page
    {
        public ObservableCollection<DayCellViewModel> DayCells { get; set; } = new();
        public ObservableCollection<AgendaItem> SelectedDayItems { get; set; } = new();

        private DateTime _viewDate = DateTime.Today;
        private SyncManager _syncManager;
        private AppCache _localCache = new();
        private AgendaItem _itemBeingEdited;

        private readonly string CacheFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskFlyout", "local_cache_winui3.json");

        public CalendarPage()
        {
            this.InitializeComponent();
            if (Application.Current is App app) _syncManager = app.SyncManager;
            this.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(Global_PointerWheelChanged), handledEventsToo: true);
            LoadCache();
            LoadCalendar(_viewDate);
        }

        private void LoadCache()
        {
            try { if (File.Exists(CacheFilePath)) _localCache = JsonSerializer.Deserialize<AppCache>(File.ReadAllText(CacheFilePath)) ?? new(); }
            catch { _localCache = new(); }
        }

        private void LoadCalendar(DateTime date)
        {
            DayCells.Clear();
            BtnMonthYear.Content = date.ToString("yyyy年 M月");

            DateTime firstOfEntry = new DateTime(date.Year, date.Month, 1);
            int offset = (int)firstOfEntry.DayOfWeek;
            DateTime startDate = firstOfEntry.AddDays(-offset);

            for (int i = 0; i < 42; i++)
            {
                var current = startDate.AddDays(i);
                var cell = new DayCellViewModel { Date = current, IsCurrentMonth = current.Month == date.Month };

                string key = current.ToString("yyyy-MM-dd");
                if (_localCache.DayItems.ContainsKey(key))
                {
                    foreach (var item in _localCache.DayItems[key]) cell.Items.Add(item);
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

        private async Task SyncMonthDataAsync()
        {
            if (_syncManager == null) return;
            SyncProgress.IsActive = true;

            try
            {
                var start = DayCells.First().Date;
                var end = DayCells.Last().Date.AddDays(1);
                var allItems = await _syncManager.GetAllDataAsync(start, end);

                foreach (var cell in DayCells)
                {
                    cell.Items.Clear();
                    var dayItems = allItems.Where(it => it.DateKey == cell.Date.ToString("yyyy-MM-dd"));
                    foreach (var item in dayItems) cell.Items.Add(item);
                }

                if (CalendarGrid.SelectedItem is DayCellViewModel selectedCell) UpdateSideBar(selectedCell);
            }
            catch { }
            finally { SyncProgress.IsActive = false; }
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
            FlyoutYearText.Text = $"{_flyoutYear}年";
            FlyoutMonthGrid.ItemsSource = new string[] { "1月", "2月", "3月", "4月", "5月", "6月", "7月", "8月", "9月", "10月", "11月", "12月" };
        }
        private void FlyoutPrevYear_Click(object sender, RoutedEventArgs e) => FlyoutYearText.Text = $"{--_flyoutYear}年";
        private void FlyoutNextYear_Click(object sender, RoutedEventArgs e) => FlyoutYearText.Text = $"{++_flyoutYear}年";
        private void FlyoutMonthGrid_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is string monthStr)
            {
                int month = int.Parse(monthStr.Replace("月", ""));
                _viewDate = new DateTime(_flyoutYear, month, 1);
                LoadCalendar(_viewDate);
                MonthYearFlyout.Hide();
            }
        }

        private void BtnPrevMonth_Click(object sender, RoutedEventArgs e) => LoadCalendar(_viewDate = _viewDate.AddMonths(-1));
        private void BtnNextMonth_Click(object sender, RoutedEventArgs e) => LoadCalendar(_viewDate = _viewDate.AddMonths(1));
        private void BtnToday_Click(object sender, RoutedEventArgs e) => LoadCalendar(_viewDate = DateTime.Today);

        private void CalendarGrid_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is DayCellViewModel cell) UpdateSideBar(cell);
        }

        private void UpdateSideBar(DayCellViewModel cell)
        {
            TxtSideBarDate.Text = cell.Date.ToString("M月d日起");
            SelectedDayItems.Clear();

            string selectedDateKey = cell.Date.ToString("yyyy-MM-dd");
            var upcomingDates = _localCache.DayItems.Keys
                .Where(k => string.Compare(k, selectedDateKey) >= 0)
                .OrderBy(k => k);

            bool hasItems = false;
            foreach (var dateKey in upcomingDates)
            {
                var sortedItems = _localCache.DayItems[dateKey].OrderBy(i => i.Subtitle == "全天" ? 0 : 1).ThenBy(i => i.Subtitle);
                foreach (var item in sortedItems)
                {
                    var displayItem = new AgendaItem
                    {
                        Id = item.Id,
                        Title = item.Title,
                        Subtitle = $"{DateTime.Parse(dateKey):M月d日}\n{item.Subtitle}",
                        Location = item.Location,
                        Description = item.Description,
                        IsEvent = item.IsEvent,
                        IsTask = item.IsTask,
                        IsCompleted = item.IsCompleted,
                        Provider = item.Provider,
                        DateKey = item.DateKey
                    };
                    SelectedDayItems.Add(displayItem);
                    hasItems = true;
                }
            }

            if (!hasItems)
            {
                SelectedDayItems.Add(new AgendaItem { Title = "近期没有安排", Subtitle = "-", IsEvent = true });
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
                    _ = SyncMonthDataAsync();
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

        private async void AgendaCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (e.OriginalSource is FrameworkElement src && (src is CheckBox || src.Parent is CheckBox)) return;

            if ((sender as FrameworkElement)?.DataContext is AgendaItem item)
            {
                if (item.Title != null && item.Title.Contains("没有安排")) return;

                _itemBeingEdited = item;
                EditTxtTitle.Text = item.Title;
                EditTxtLocation.Text = item.Location;
                EditTxtDescription.Text = item.Description;

                if (DateTime.TryParse(item.DateKey, out var d)) EditDatePicker.Date = d;

                // 智能提取已设置的时间 (支持解析 "14:00 - 15:30")
                var timePart = item.Subtitle?.Split('\n').LastOrDefault()?.Trim();
                if (timePart == "全天" || string.IsNullOrEmpty(timePart))
                {
                    EditChkAllDay.IsChecked = true;
                    EditStartTimePicker.SelectedTime = null;
                    EditEndTimePicker.SelectedTime = null;
                }
                else
                {
                    EditChkAllDay.IsChecked = false;
                    var times = timePart.Split('-');

                    if (times.Length >= 1 && TimeSpan.TryParse(times[0].Trim(), out var st))
                        EditStartTimePicker.SelectedTime = st;

                    if (times.Length >= 2 && TimeSpan.TryParse(times[1].Trim(), out var et))
                        EditEndTimePicker.SelectedTime = et;
                    else if (EditStartTimePicker.SelectedTime.HasValue)
                        EditEndTimePicker.SelectedTime = EditStartTimePicker.SelectedTime.Value.Add(TimeSpan.FromHours(1));
                }

                EditDialog.XamlRoot = this.XamlRoot;
                await EditDialog.ShowAsync();
            }
        }

        private async void EditDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (_itemBeingEdited == null || string.IsNullOrWhiteSpace(EditTxtTitle.Text)) return;

            var newDateKey = EditDatePicker.Date.ToString("yyyy-MM-dd");

            TimeSpan? newStartTime = EditChkAllDay.IsChecked == true ? null : EditStartTimePicker.SelectedTime;
            TimeSpan? newEndTime = EditChkAllDay.IsChecked == true ? null : EditEndTimePicker.SelectedTime;

            string newSubtitleText = "全天";
            if (newStartTime.HasValue && newEndTime.HasValue)
                newSubtitleText = $"{newStartTime.Value:hh\\:mm} - {newEndTime.Value:hh\\:mm}";
            else if (newStartTime.HasValue)
                newSubtitleText = $"{newStartTime.Value:hh\\:mm}";

            // 更新本地缓存
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
                }
            }
            LoadCalendar(_viewDate);

            // 推送云端
            if (_syncManager != null && !string.IsNullOrEmpty(_itemBeingEdited.Id))
            {
                try
                {
                    await _syncManager.UpdateItemAsync(
                        _itemBeingEdited.Provider,
                        _itemBeingEdited.Id,
                        _itemBeingEdited.IsEvent,
                        EditTxtTitle.Text,
                        EditTxtLocation.Text,
                        EditTxtDescription.Text,
                        EditDatePicker.Date.DateTime,
                        newStartTime,
                        newEndTime // 👉 把结束时间传给云端
                    );

                    _ = SyncMonthDataAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"更新失败: {ex.Message}");
                }
            }
        }

        private async void EditDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (_itemBeingEdited != null)
            {
                if (_localCache.DayItems.TryGetValue(_itemBeingEdited.DateKey, out var list))
                {
                    list.RemoveAll(x => x.Id == _itemBeingEdited.Id);
                }
                LoadCalendar(_viewDate);

                if (_syncManager != null && !string.IsNullOrEmpty(_itemBeingEdited.Id))
                {
                    try
                    {
                        await _syncManager.DeleteItemAsync(_itemBeingEdited.Provider, _itemBeingEdited.Id, _itemBeingEdited.IsEvent);
                    }
                    catch { }
                }
            }
        }

        public void OpenEditDialogFromExternal(AgendaItem item)
        {
            Action showDialog = async () =>
            {
                _itemBeingEdited = item;

                EditTxtTitle.Text = item.Title;
                EditTxtLocation.Text = item.Location;
                EditTxtDescription.Text = item.Description;

                if (DateTime.TryParse(item.DateKey, out var d)) EditDatePicker.Date = d;

                var timePart = item.Subtitle?.Split('\n').LastOrDefault()?.Trim();
                if (timePart == "全天" || string.IsNullOrEmpty(timePart))
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
                }

                // 强制绑定图层并安全显示 (捕获异常防止重复弹窗崩溃)
                EditDialog.XamlRoot = this.XamlRoot;
                try { await EditDialog.ShowAsync(); } catch { }
            };

            // 如果页面还没挂载到屏幕上，就等它 Loaded 完毕再执行弹窗
            if (this.XamlRoot == null)
            {
                this.Loaded += (s, e) => showDialog();
            }
            else
            {
                showDialog();
            }
        }
    }
}