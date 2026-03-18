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
using System.IO;
using System.Text.Json;

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
        // 👉 核心：无视层级的全局滚轮翻页
        private void Global_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            // 只有当鼠标处于没有垂直滚动条的区域，或者按住 Ctrl (一般用于缩放但不常用) 时才翻页
            // 如果右侧的 ListView 真的超长出现了滚动条，我们希望用户鼠标放在右侧时是滚动列表，而不是翻页
            // 所以我们可以加一个判断：如果鼠标在日历网格区域，才进行翻页

            var point = e.GetCurrentPoint(CalendarGrid);

            // 判断鼠标是否在左侧的日历网格区域内
            if (point.Position.X >= 0 && point.Position.X <= CalendarGrid.ActualWidth &&
                point.Position.Y >= 0 && point.Position.Y <= CalendarGrid.ActualHeight)
            {
                var delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;

                if (delta > 0) BtnPrevMonth_Click(null, null); // 向上滚，上个月
                else if (delta < 0) BtnNextMonth_Click(null, null); // 向下滚，下个月

                e.Handled = true; // 告诉系统：我翻页了，底下的控件别滚动了
            }
        }
        // 👉 核心：全局鼠标滚轮翻页
        private void Grid_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var delta = e.GetCurrentPoint((UIElement)sender).Properties.MouseWheelDelta;
            if (delta > 0) BtnPrevMonth_Click(null, null); // 向上滚，上个月
            else BtnNextMonth_Click(null, null);           // 向下滚，下个月
            e.Handled = true;
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
            // 每次点开弹出层时，初始化为当前的年份，并灌入 12 个月份
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
                // 把 "3月" 这种文字拆出数字，并直接跳转！
                int month = int.Parse(monthStr.Replace("月", ""));
                _viewDate = new DateTime(_flyoutYear, month, 1);
                LoadCalendar(_viewDate);
                MonthYearFlyout.Hide(); // 选完月份，自动收起弹窗，体验拉满
            }
        }

        private void BtnPrevMonth_Click(object sender, RoutedEventArgs e) => LoadCalendar(_viewDate = _viewDate.AddMonths(-1));
        private void BtnNextMonth_Click(object sender, RoutedEventArgs e) => LoadCalendar(_viewDate = _viewDate.AddMonths(1));
        private void BtnToday_Click(object sender, RoutedEventArgs e) => LoadCalendar(_viewDate = DateTime.Today);

        private void CalendarGrid_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is DayCellViewModel cell) UpdateSideBar(cell);
        }

        // 👉 核心：瀑布流侧边栏（抓取自选中日期起的所有事件）
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
                    // 为了保证列表里的副标题包含具体日期，克隆一个新对象做展示
                    var displayItem = new AgendaItem
                    {
                        Id = item.Id,
                        Title = item.Title,
                        Subtitle = $"{DateTime.Parse(dateKey):M月d日}\n{item.Subtitle}", // 第一行日期，第二行时间
                        Location = item.Location,
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

        // 👉 核心：呼出编辑面板
        private async void EditItem_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is AgendaItem item)
            {
                _itemBeingEdited = item;
                EditTxtTitle.Text = item.Title;
                EditTxtLocation.Text = item.Location;
                if (DateTime.TryParse(item.DateKey, out var d)) EditDatePicker.Date = d;

                await EditDialog.ShowAsync();
            }
        }

        // 👉 核心：应用编辑 (乐观 UI 更新机制，提供瞬时反馈)
        private void EditDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (_itemBeingEdited == null || string.IsNullOrWhiteSpace(EditTxtTitle.Text)) return;

            var newDateKey = EditDatePicker.Date.ToString("yyyy-MM-dd");

            // 1. 本地缓存大搬迁与更新
            if (_localCache.DayItems.TryGetValue(_itemBeingEdited.DateKey, out var oldList))
            {
                var orig = oldList.FirstOrDefault(x => x.Id == _itemBeingEdited.Id);
                if (orig != null)
                {
                    oldList.Remove(orig); // 从老日期移除

                    // 应用新属性
                    orig.Title = EditTxtTitle.Text;
                    orig.Location = EditTxtLocation.Text;
                    orig.DateKey = newDateKey;

                    // 加入新日期
                    if (!_localCache.DayItems.ContainsKey(newDateKey)) _localCache.DayItems[newDateKey] = new List<AgendaItem>();
                    _localCache.DayItems[newDateKey].Add(orig);
                }
            }

            // 2. 刷新界面
            LoadCalendar(_viewDate);

        }

        private async void DeleteItem_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is AgendaItem item)
            {
                if (_localCache.DayItems.TryGetValue(item.DateKey, out var list))
                {
                    list.RemoveAll(x => x.Id == item.Id);
                }
                LoadCalendar(_viewDate);

                if (_syncManager != null && !string.IsNullOrEmpty(item.Id))
                {
                    try
                    {
                        await _syncManager.DeleteItemAsync(item.Provider, item.Id, item.IsEvent);
                    }
                    catch
                    {
                    }
                }
            }
        }
    }
}