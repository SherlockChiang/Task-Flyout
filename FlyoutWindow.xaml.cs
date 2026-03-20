using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
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
using Windows.Graphics;
using Windows.UI;

namespace Task_Flyout
{
    public class AppCache
    {
        public HashSet<string> MarkedDates { get; set; } = new();
        public Dictionary<string, List<AgendaItem>> DayItems { get; set; } = new();
    }

    public class BoolToBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush _transparentBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isSelected && isSelected)
                if (Application.Current.Resources.TryGetValue("SubtleFillColorSecondaryBrush", out var res))
                    return res;
            return _transparentBrush;
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class BoolToStrikethroughConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language) =>
            value is bool isCompleted && isCompleted ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None;
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public sealed partial class FlyoutWindow : Window
    {
        private AppWindow _appWindow;
        private AppCache _localCache = new();
        private DispatcherTimer _syncTimer;
        private DispatcherTimer _clockTimer;
        private SyncManager _syncManager;

        private readonly string CacheFilePath =
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TaskFlyout", "local_cache_winui3.json");

        public ObservableCollection<AgendaItem> AgendaItems { get; set; } = new();
        public HashSet<string> MarkedDates { get; set; } = new();
        public Dictionary<string, int> EventCounts { get; set; } = new();

        private DateTime _selectedDay = DateTime.Today;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        public FlyoutWindow()
        {
            InitializeComponent();
            AgendaListControl.ItemsSource = AgendaItems;

            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);

            _syncManager = new SyncManager();
            _syncManager.RegisterProvider(new GoogleSyncProvider());

            ConfigureFlyoutStyle();
            LoadCache();
            StartClock();

            MainCalendar.SelectedDates.Add(DateTime.Today);
            UpdateSelectedDateHeader();
            ShowDataForDate(_selectedDay);

            Activated += FlyoutWindow_Activated;
            RootGrid.Loaded += (s, e) => _ = SyncAllDataAsync(true);
            SetupPeriodicSync();
        }

        private void StartClock()
        {
            UpdateClock();
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (s, e) => UpdateClock();
            _clockTimer.Start();
        }

        private void UpdateClock()
        {
            TxtRealTimeClock.Text = DateTime.Now.ToString("HH:mm");
            TxtRealTimeDate.Text = DateTime.Now.ToString("yyyy年M月d日 dddd");
        }

        private void SetupPeriodicSync()
        {
            _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
            _syncTimer.Tick += async (s, e) => await SyncAllDataAsync(true);
            _syncTimer.Start();
        }

        private void ConfigureFlyoutStyle()
        {
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false; presenter.IsAlwaysOnTop = true; presenter.SetBorderAndTitleBar(false, false);
            }
            _appWindow.IsShownInSwitchers = false;
        }

        private void LoadCache()
        {
            try
            {
                if (File.Exists(CacheFilePath))
                {
                    _localCache = JsonSerializer.Deserialize<AppCache>(File.ReadAllText(CacheFilePath)) ?? new();
                    MarkedDates = new HashSet<string>(_localCache.MarkedDates);
                    EventCounts.Clear();
                    foreach (var kvp in _localCache.DayItems)
                    {
                        int count = kvp.Value.Count(i => i.IsEvent || (i.IsTask && !i.IsCompleted));
                        if (count > 0) EventCounts[kvp.Key] = count;
                    }
                }
            }
            catch { _localCache = new(); }
        }

        private async Task SaveCache()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CacheFilePath));
                _localCache.MarkedDates = MarkedDates;
                await File.WriteAllTextAsync(CacheFilePath, JsonSerializer.Serialize(_localCache));
            }
            catch { }
        }

        private void BtnGoToToday_Click(object sender, RoutedEventArgs e)
        {
            MainCalendar.SetDisplayDate(DateTime.Today);
            MainCalendar.SelectedDates.Clear();
            MainCalendar.SelectedDates.Add(DateTime.Today);
        }

        private void MainCalendar_CalendarViewDayItemChanging(CalendarView sender, CalendarViewDayItemChangingEventArgs args)
        {
            var dateStr = args.Item.Date.Date.ToString("yyyy-MM-dd");

            if (EventCounts.TryGetValue(dateStr, out int count) && count > 0)
            {
                Color accentColor = Color.FromArgb(255, 0, 120, 215);
                if (Application.Current.Resources.TryGetValue("SystemAccentColor", out var res) && res is Color sysColor)
                {
                    accentColor = sysColor;
                }
                args.Item.Background = new SolidColorBrush(Color.FromArgb(40, accentColor.R, accentColor.G, accentColor.B));
                args.Item.SetDensityColors(null);
            }
            else
            {
                args.Item.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                args.Item.SetDensityColors(null);
            }
        }

        private void MainCalendar_SelectedDatesChanged(CalendarView sender, CalendarViewSelectedDatesChangedEventArgs args)
        {
            if (args.AddedDates.Count == 0) return;

            // 👉 如果选择了新日期，自动收起新建面板，并恢复显示日程列表
            if (AddPanel != null && AddPanel.Visibility == Visibility.Visible)
            {
                AddPanel.Visibility = Visibility.Collapsed;
                if (AgendaContainer != null) AgendaContainer.Visibility = Visibility.Visible;
            }

            _selectedDay = args.AddedDates[0].Date;
            UpdateSelectedDateHeader();
            ShowDataForDate(_selectedDay);
        }

        private void UpdateSelectedDateHeader()
        {
            TxtSelectedDateHeader.Text = _selectedDay.Date == DateTime.Today ? "今天" : _selectedDay.ToString("yyyy年M月d日");
        }

        private void ShowDataForDate(DateTime date)
        {
            string key = date.ToString("yyyy-MM-dd");
            var tempAgenda = new List<AgendaItem>();

            if (_localCache.DayItems.ContainsKey(key) && _localCache.DayItems[key].Count > 0)
            {
                tempAgenda.AddRange(_localCache.DayItems[key]);
            }
            else
            {
                tempAgenda.Add(new AgendaItem { Title = "这一天没有安排", Subtitle = "休息一下吧", IsEvent = false, IsTask = false });

                var nextDayKey = _localCache.DayItems.Keys
                    .Where(k => string.Compare(k, key) > 0) 
                    .OrderBy(k => k)
                    .FirstOrDefault(k => _localCache.DayItems[k].Count > 0);

                if (nextDayKey != null)
                {
                    var nextItems = _localCache.DayItems[nextDayKey];
                    DateTime nextDate = DateTime.Parse(nextDayKey);
                    int daysDiff = (nextDate - date).Days;
                    string daysLaterText = daysDiff == 1 ? "明天" : (daysDiff == 2 ? "后天" : $"{nextDate:M月d日}");

                    foreach (var item in nextItems)
                    {
                        tempAgenda.Add(new AgendaItem
                        {
                            Id = item.Id, 
                            Title = item.Title,
                            Subtitle = $"即将到来 · {daysLaterText} {item.Subtitle}",
                            Location = item.Location,
                            IsEvent = item.IsEvent,
                            IsTask = item.IsTask,
                            IsCompleted = item.IsCompleted,
                            Provider = item.Provider,
                            DateKey = item.DateKey
                        });
                    }
                }
            }

            AgendaItems.Clear();
            foreach (var item in tempAgenda)
            {
                AgendaItems.Add(item);
            }

            AdjustWindowHeight();
        }

        private async Task SyncAllDataAsync(bool silent)
        {
            if (!silent)
            {
                AgendaItems.Clear();
                AgendaItems.Add(new AgendaItem { Title = "全量同步中...", Subtitle = "正在获取您的全部日程与任务", IsEvent = false, IsTask = false });
            }

            try
            {
                var min = DateTime.Today.AddYears(-1);
                var max = DateTime.Today.AddYears(3);

                var allItems = await Task.Run(async () => await _syncManager.GetAllDataAsync(min, max));

                var tempDayItems = new Dictionary<string, List<AgendaItem>>();
                var tempMarkedDates = new HashSet<string>();
                var tempEventCounts = new Dictionary<string, int>();

                foreach (var item in allItems)
                {
                    if (string.IsNullOrEmpty(item.DateKey)) continue;

                    if (!tempDayItems.ContainsKey(item.DateKey))
                        tempDayItems[item.DateKey] = new List<AgendaItem>();

                    tempDayItems[item.DateKey].Add(item);

                    if (item.IsEvent || (item.IsTask && !item.IsCompleted))
                    {
                        tempMarkedDates.Add(item.DateKey);
                        if (!tempEventCounts.ContainsKey(item.DateKey)) tempEventCounts[item.DateKey] = 0;
                        tempEventCounts[item.DateKey]++;
                    }
                }

                _localCache.DayItems = tempDayItems;
                MarkedDates = tempMarkedDates;
                EventCounts = tempEventCounts;
                await SaveCache();

                MainCalendar.UpdateLayout();
                ShowDataForDate(_selectedDay);
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    AgendaItems.Clear();
                    AgendaItems.Add(new AgendaItem { Title = "同步失败", Subtitle = ex.Message, IsEvent = false, IsTask = false });
                }
            }
        }

        public void ToggleFlyout()
        {
            if (_appWindow.IsVisible)
                _appWindow.Hide();
            else
            {
                AdjustWindowHeight();
                _appWindow.Show();
                Activate();
                UpdateClock();

                if (MainCalendar.SelectedDates.Count == 0 || MainCalendar.SelectedDates[0].Date != DateTime.Today)
                {
                    BtnGoToToday_Click(null, null);
                }

                _ = SyncAllDataAsync(true);
            }
        }

        private void AdjustWindowHeight()
        {
            if (_appWindow == null) return;

            // 基础高度：时钟区 + 日历区 + 底部工具栏 + 各种边距 ≈ 490
            int logicalBaseHeight = 490;
            int logicalTargetHeight = logicalBaseHeight;

            if (AddPanel != null && AddPanel.Visibility == Visibility.Visible)
            {
                // 👉 核心修复：把预留高度从 300 提升到 400 像素，给时间滚轮和地点输入框充足的伸展空间
                logicalTargetHeight += 400;
            }
            else
            {
                // 日程列表模式：精确计算每一项的真实高度
                int listHeight = 0;
                foreach (var item in AgendaItems)
                {
                    if (item.Title == "这一天没有安排")
                    {
                        listHeight += 50;
                    }
                    else if (item.Subtitle != null && item.Subtitle.Contains("即将到来"))
                    {
                        listHeight += 65;
                    }
                    else
                    {
                        listHeight += !string.IsNullOrEmpty(item.Location) ? 75 : 65;
                    }
                }

                // 加上 ListView 容器自身的上下 Padding 与 TextHeader 的高度
                logicalTargetHeight += listHeight + 45;
            }

            int logicalWidth = 360;

            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            double scaleFactor = GetDpiForWindow(hWnd) / 96.0;

            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var display = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(windowId, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);
            var workArea = display.WorkArea;

            int physicalWidth = (int)(logicalWidth * scaleFactor);
            int physicalHeight = (int)(logicalTargetHeight * scaleFactor);

            // 屏幕高度安全保护机制（如果屏幕极小或缩放极高，依然会触发流畅的内部滚动）
            int maxPhysicalHeight = workArea.Height - 80;
            if (physicalHeight > maxPhysicalHeight)
            {
                physicalHeight = maxPhysicalHeight;
            }

            _appWindow.Resize(new SizeInt32(physicalWidth, physicalHeight));

            int physicalMargin = (int)(12 * scaleFactor);
            int targetX = workArea.X + workArea.Width - physicalWidth - physicalMargin;
            int targetY = workArea.Y + workArea.Height - physicalHeight - physicalMargin;

            _appWindow.Move(new PointInt32(targetX, targetY));
        }

        private void FlyoutWindow_Activated(object sender, WindowActivatedEventArgs args) { if (args.WindowActivationState == WindowActivationState.Deactivated) _appWindow.Hide(); }

        private async void TaskCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is AgendaItem item && item.IsTask)
            {
                try
                {
                    cb.IsEnabled = false;
                    item.IsCompleted = (cb.IsChecked == true);
                    await _syncManager.UpdateTaskStatusAsync(item.Provider, item.Id, item.IsCompleted);
                    _ = SyncAllDataAsync(silent: true);
                }
                catch { item.IsCompleted = !item.IsCompleted; }
                finally { cb.IsEnabled = true; }
            }
        }

        private void RadioType_Changed(object sender, RoutedEventArgs e)
        {
            if (TxtLocation == null || TimePickerEnd == null || TxtTimeSeparator == null || TimePickerStart == null) return;

            bool isEvent = RadioTypeEvent.IsChecked == true;

            TxtLocation.Visibility = isEvent ? Visibility.Visible : Visibility.Collapsed;
            TimePickerEnd.Visibility = isEvent ? Visibility.Visible : Visibility.Collapsed;
            TxtTimeSeparator.Visibility = isEvent ? Visibility.Visible : Visibility.Collapsed;

            TimePickerStart.Header = isEvent ? "开始时间" : "截止时间";

            if (TimePanel != null && TimePanel.ColumnDefinitions.Count >= 3)
            {
                TimePanel.ColumnDefinitions[1].Width = isEvent ? new GridLength(10, GridUnitType.Star) : new GridLength(0);
                TimePanel.ColumnDefinitions[2].Width = isEvent ? new GridLength(45, GridUnitType.Star) : new GridLength(0);
            }
        }

        private void ChkAllDay_Checked(object sender, RoutedEventArgs e) { if (TimePanel != null) TimePanel.Visibility = Visibility.Collapsed; }
        private void ChkAllDay_Unchecked(object sender, RoutedEventArgs e) { if (TimePanel != null) TimePanel.Visibility = Visibility.Visible; }
        private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await SyncAllDataAsync(silent: false);

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            _appWindow.Hide();
            App.OpenMainWindowInternal(win => win.NavigateToSettings());
        }

        private void AgendaListControl_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is AgendaItem item)
            {
                if (item.Title != null && item.Title.Contains("没有安排")) return;

                _appWindow.Hide();
                App.OpenMainWindowInternal(win => win.NavigateToCalendarAndEdit(item));
            }
        }

        private void BtnToggleAddPanel_Click(object sender, RoutedEventArgs e)
        {
            if (AddPanel.Visibility == Visibility.Visible)
            {
                AddPanel.Visibility = Visibility.Collapsed;
                if (AgendaContainer != null) AgendaContainer.Visibility = Visibility.Visible;
            }
            else
            {
                AddPanel.Visibility = Visibility.Visible;
                if (AgendaContainer != null) AgendaContainer.Visibility = Visibility.Collapsed;
            }
            AdjustWindowHeight();
        }

        private void BtnCancelAdd_Click(object sender, RoutedEventArgs e)
        {
            AddPanel.Visibility = Visibility.Collapsed;
            if (AgendaContainer != null) AgendaContainer.Visibility = Visibility.Visible;

            TxtNewTitle.Text = string.Empty;
            TxtLocation.Text = string.Empty;
            AdjustWindowHeight();
        }

        private async void BtnSaveNewItem_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtNewTitle.Text)) return;

            // 保存后自动恢复日程视图
            AddPanel.Visibility = Visibility.Collapsed;
            if (AgendaContainer != null) AgendaContainer.Visibility = Visibility.Visible;

            string title = TxtNewTitle.Text;
            bool isEvent = RadioTypeEvent.IsChecked == true;
            bool isAllDay = ChkAllDay.IsChecked == true;
            string location = TxtLocation.Text;

            TimeSpan startTime = TimePickerStart.Time;
            TimeSpan endTime = TimePickerEnd.Time;

            if (isEvent && endTime <= startTime) endTime = startTime.Add(TimeSpan.FromHours(1));

            TxtNewTitle.Text = string.Empty;
            TxtLocation.Text = string.Empty;

            DateTime targetDate = _selectedDay;
            DateTime exactStartDateTime = targetDate.Add(startTime);
            DateTime exactEndDateTime = targetDate.Add(endTime);

            try
            {
                string timeDisplay = isAllDay ? "全天" : (isEvent ? $"{exactStartDateTime:HH:mm} - {exactEndDateTime:HH:mm}" : $"{exactStartDateTime:HH:mm}");
                AgendaItems.Add(new AgendaItem { Title = title, Subtitle = $"{timeDisplay} (同步中...)", Location = location, IsTask = !isEvent, IsEvent = isEvent });

                AdjustWindowHeight();

                await _syncManager.CreateItemAsync(title, isEvent, isAllDay, targetDate, startTime, endTime, location);
                _ = SyncAllDataAsync(true);
            }
            catch (Exception ex) // 👉 捕获真实的异常
            {
                // 1. 在 Visual Studio 的输出窗口打印完整报错堆栈
                System.Diagnostics.Debug.WriteLine("============== 谷歌同步报错 ==============");
                System.Diagnostics.Debug.WriteLine(ex.ToString());

                // 2. 直接在你的 UI 列表最上方显示错误信息，方便你一眼看到！
                AgendaItems.Insert(0, new AgendaItem
                {
                    Title = "❌ 任务添加失败",
                    Subtitle = ex.Message,
                    IsEvent = false,
                    IsTask = false
                });
                AdjustWindowHeight();
            }
        }
    }
}