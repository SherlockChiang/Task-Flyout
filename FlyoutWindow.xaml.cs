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
using System.Threading.Tasks;
using Task_Flyout.Models;
using Task_Flyout.Services;
using Windows.Graphics;
using System.Text.Json;
using Microsoft.UI.Xaml.Media.Animation;

namespace Task_Flyout
{
    public class AppCache
    {
        public HashSet<string> MarkedDates { get; set; } = new();
        public Dictionary<string, List<AgendaItem>> DayItems { get; set; } = new();
    }

    public class BoolToBrushConverter : IValueConverter
    {
        // 👉 性能优化：静态只读实例缓存透明画刷，避免每次转换时生成内存垃圾
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

    // 视图模式枚举
    public enum CalendarViewMode
    {
        Days,   // 标准日历 (显示每一天)
        Months, // 月份选择器 (1-12月)
        Years   // 年份选择器 (16年跨度)
    }

    public sealed partial class FlyoutWindow : Window
    {
        private GoogleAuthService _googleAuth;
        private AppWindow _appWindow;
        private AppCache _localCache = new();
        private DispatcherTimer _syncTimer;
        private DispatcherTimer _clockTimer; // 实时时钟
        private readonly string CacheFilePath =
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TaskFlyout", "local_cache_winui3.json");

        public ObservableCollection<AgendaItem> AgendaItems { get; set; } = new();
        public ObservableCollection<CalendarDay> CalendarDays { get; set; } = new();

        // 钻取视图的数据源
        public ObservableCollection<string> MonthStrings { get; set; } = new();
        public ObservableCollection<string> YearStrings { get; set; } = new();

        public HashSet<string> MarkedDates { get; set; } = new();
        public Dictionary<string, int> EventCounts { get; set; } = new();

        // 状态控制
        private DateTime _currentDisplayMonth = DateTime.Today; // 控制日历当前翻到的月份
        private DateTime _selectedDay = DateTime.Today;         // 控制当前选中的具体日期
        private CalendarViewMode _currentViewMode = CalendarViewMode.Days;

        public FlyoutWindow()
        {
            InitializeComponent();
            _googleAuth = new GoogleAuthService();
            AgendaListControl.ItemsSource = AgendaItems;
            CustomCalendarGrid.ItemsSource = CalendarDays;
            MonthViewPanel.ItemsSource = MonthStrings;
            YearViewPanel.ItemsSource = YearStrings;

            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);

            ConfigureFlyoutStyle();
            LoadCache();

            // 启动时钟
            StartClock();

            // 生成默认日历并选中今天
            GenerateCalendarDays(_currentDisplayMonth);
            UpdateSelectedDateHeader();
            ShowDataForDate(_selectedDay);

            Activated += FlyoutWindow_Activated;
            RootGrid.Loaded += (s, e) => _ = SyncAllDataAsync(true);
            SetupPeriodicSync();
        }

        private void StartClock()
        {
            UpdateClock(); // 立即执行一次
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
            _syncTimer.Tick += async (s, e) => { if (_googleAuth.CalendarSvc != null) await SyncAllDataAsync(true); };
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

        // ======================= 缓存 =======================
        private void LoadCache() { try { if (File.Exists(CacheFilePath)) { _localCache = JsonSerializer.Deserialize<AppCache>(File.ReadAllText(CacheFilePath)) ?? new(); MarkedDates = new HashSet<string>(_localCache.MarkedDates); EventCounts.Clear(); foreach (var kvp in _localCache.DayItems) { int count = kvp.Value.Count(i => i.IsEvent || (i.IsTask && !i.IsCompleted)); if (count > 0) EventCounts[kvp.Key] = count; } } } catch { _localCache = new(); } }
        private async Task SaveCache()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CacheFilePath));
                _localCache.MarkedDates = MarkedDates;
                // 👉 性能优化：使用异步写入，不阻塞 UI 线程
                await File.WriteAllTextAsync(CacheFilePath, JsonSerializer.Serialize(_localCache));
            }
            catch { }
        }

        // ======================= 钻取视图控制 (日/月/年) =======================

        private void SwitchViewMode(CalendarViewMode mode)
        {
            _currentViewMode = mode;
            WeekHeaderGrid.Visibility = mode == CalendarViewMode.Days ? Visibility.Visible : Visibility.Collapsed;
            CustomCalendarGrid.Visibility = mode == CalendarViewMode.Days ? Visibility.Visible : Visibility.Collapsed;

            MonthViewPanel.Visibility = mode == CalendarViewMode.Months ? Visibility.Visible : Visibility.Collapsed;
            YearViewPanel.Visibility = mode == CalendarViewMode.Years ? Visibility.Visible : Visibility.Collapsed;

            if (mode == CalendarViewMode.Days)
            {
                BtnViewDrill.Content = _currentDisplayMonth.ToString("yyyy年M月");
                GenerateCalendarDays(_currentDisplayMonth);
            }
            else if (mode == CalendarViewMode.Months)
            {
                BtnViewDrill.Content = _currentDisplayMonth.ToString("yyyy年");
                MonthStrings.Clear();

                DateTime centerMonth = new DateTime(_currentDisplayMonth.Year, _currentDisplayMonth.Month, 1);
                DateTime startMonth = centerMonth.AddMonths(-7);

                for (int i = 0; i < 16; i++)
                {
                    DateTime m = startMonth.AddMonths(i);
                    MonthStrings.Add($"{m.Year}年{m.Month}月");
                }
            }
            else if (mode == CalendarViewMode.Years)
            {
                int startYear = _currentDisplayMonth.Year - 7;
                BtnViewDrill.Content = $"{startYear} - {startYear + 15}";
                YearStrings.Clear();
                for (int i = 0; i < 16; i++) YearStrings.Add((startYear + i).ToString());
            }
            AdjustWindowHeight();
        }

        private DateTime _lastScrollTime = DateTime.MinValue;

        private void CalendarArea_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint((UIElement)sender);
            var delta = point.Properties.MouseWheelDelta;

            // 节流阀：限制滚动频率（最高 150毫秒/次），防止触控板高频滚动导致动画疯狂乱跳
            if ((DateTime.Now - _lastScrollTime).TotalMilliseconds < 150)
            {
                e.Handled = true;
                return;
            }

            if (delta < 0) NavigateTime(1); // 往下滚，时间前进
            else if (delta > 0) NavigateTime(-1); // 往上滚，时间倒退

            _lastScrollTime = DateTime.Now;
            e.Handled = true;
        }

        private void NavigateTime(int direction)
        {
            PerformScrollUpdate(direction, () =>
            {
                if (_currentViewMode == CalendarViewMode.Days)
                {
                    if (CalendarDays.Count > 0)
                    {
                        DateTime nextStartDate = CalendarDays[0].Date.AddDays(7 * direction);
                        _currentDisplayMonth = nextStartDate.AddDays(14);
                        BtnViewDrill.Content = _currentDisplayMonth.ToString("yyyy年M月");
                        GenerateCalendarDaysFromStart(nextStartDate);
                    }
                }
                else if (_currentViewMode == CalendarViewMode.Months)
                {
                    _currentDisplayMonth = _currentDisplayMonth.AddMonths(4 * direction);
                    SwitchViewMode(CalendarViewMode.Months);
                }
                else if (_currentViewMode == CalendarViewMode.Years)
                {
                    _currentDisplayMonth = _currentDisplayMonth.AddYears(4 * direction);
                    SwitchViewMode(CalendarViewMode.Years);
                }
            });
        }

        private void GenerateCalendarDaysFromStart(DateTime startDate)
        {
            var tempDays = new List<CalendarDay>();

            for (int i = 0; i < 42; i++)
            {
                DateTime currentDate = startDate.AddDays(i);
                var day = new CalendarDay
                {
                    Date = currentDate,
                    IsCurrentMonth = currentDate.Month == _currentDisplayMonth.Month,
                    IsSelected = (currentDate.Date == _selectedDay.Date)
                };

                string dateStr = currentDate.ToString("yyyy-MM-dd");
                if (EventCounts.TryGetValue(dateStr, out int count)) day.EventCount = count;

                tempDays.Add(day);
            }

            // 一次性应用全部集合，拒绝单独Add重绘闪烁
            CalendarDays = new ObservableCollection<CalendarDay>(tempDays);
            CustomCalendarGrid.ItemsSource = CalendarDays;

            AdjustWindowHeight();
        }

        private void PerformScrollUpdate(int direction, Action updateData)
        {
            // 1. 先把容器“瞬间”偏移到动画起点！
            double exactRowHeight = _currentViewMode == CalendarViewMode.Days ? 46.0 : 64.0;
            CalendarTranslateTransform.Y = direction > 0 ? exactRowHeight : -exactRowHeight;

            // 2. 执行静默数据更新
            updateData();

            // 3. 构建并触发纯线性的缓慢优雅推拉动画
            var sb = new Storyboard();
            var slideIn = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(350)
            };
            Storyboard.SetTarget(slideIn, CalendarTranslateTransform);
            Storyboard.SetTargetProperty(slideIn, "Y");

            sb.Children.Add(slideIn);
            sb.Begin();
        }

        private void GenerateCalendarDays(DateTime targetDate)
        {
            DateTime firstDayOfMonth = new DateTime(targetDate.Year, targetDate.Month, 1);
            int offset = (int)firstDayOfMonth.DayOfWeek;
            DateTime startDate = firstDayOfMonth.AddDays(-offset);

            if (targetDate < startDate || targetDate >= startDate.AddDays(42))
            {
                int targetOffset = (int)targetDate.DayOfWeek;
                startDate = targetDate.AddDays(-targetOffset - 14);
            }

            GenerateCalendarDaysFromStart(startDate);
        }

        private void MonthViewPanel_ItemClick(object sender, ItemClickEventArgs e)
        {
            string clickedStr = e.ClickedItem.ToString();
            var parts = clickedStr.Split(new[] { '年', '月' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 2 && int.TryParse(parts[0], out int y) && int.TryParse(parts[1], out int m))
            {
                _currentDisplayMonth = new DateTime(y, m, 1);
                SwitchViewMode(CalendarViewMode.Days);
            }
        }

        private void BtnViewDrill_Click(object sender, RoutedEventArgs e)
        {
            if (_currentViewMode == CalendarViewMode.Days) SwitchViewMode(CalendarViewMode.Months);
            else if (_currentViewMode == CalendarViewMode.Months) SwitchViewMode(CalendarViewMode.Years);
        }

        private void YearViewPanel_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (int.TryParse(e.ClickedItem.ToString(), out int y))
            {
                _currentDisplayMonth = new DateTime(y, _currentDisplayMonth.Month, 1);
                SwitchViewMode(CalendarViewMode.Months);
            }
        }

        // ======================= 翻页与滚动控制 =======================

        private void BtnPrev_Click(object sender, RoutedEventArgs e) => NavigateByPage(-1);
        private void BtnNext_Click(object sender, RoutedEventArgs e) => NavigateByPage(1);
        private void NavigateByPage(int direction)
        {
            PerformScrollUpdate(direction, () =>
            {
                if (_currentViewMode == CalendarViewMode.Days)
                {
                    _currentDisplayMonth = _currentDisplayMonth.AddMonths(direction);
                    BtnViewDrill.Content = _currentDisplayMonth.ToString("yyyy年M月");
                    GenerateCalendarDays(_currentDisplayMonth);
                }
                else if (_currentViewMode == CalendarViewMode.Months)
                {
                    _currentDisplayMonth = _currentDisplayMonth.AddYears(direction);
                    SwitchViewMode(CalendarViewMode.Months);
                }
                else if (_currentViewMode == CalendarViewMode.Years)
                {
                    _currentDisplayMonth = _currentDisplayMonth.AddYears(16 * direction);
                    SwitchViewMode(CalendarViewMode.Years);
                }
            });
        }

        private void BtnGoToToday_Click(object sender, RoutedEventArgs e)
        {
            _currentDisplayMonth = DateTime.Today;
            SwitchViewMode(CalendarViewMode.Days);

            var todayItem = CalendarDays.FirstOrDefault(d => d.Date.Date == DateTime.Today);
            if (todayItem != null) SelectDay(todayItem);
        }

        // ======================= 日历网格生成 =======================

        private void CustomCalendarGrid_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is CalendarDay clickedDay) SelectDay(clickedDay);
        }

        private void SelectDay(CalendarDay day)
        {
            foreach (var d in CalendarDays) d.IsSelected = false;
            day.IsSelected = true;
            _selectedDay = day.Date;

            if (!day.IsCurrentMonth)
            {
                _currentDisplayMonth = day.Date;
                GenerateCalendarDays(_currentDisplayMonth);
                BtnViewDrill.Content = _currentDisplayMonth.ToString("yyyy年M月");

                var newDay = CalendarDays.FirstOrDefault(d => d.Date.Date == _selectedDay);
                if (newDay != null) newDay.IsSelected = true;
            }

            UpdateSelectedDateHeader();
            ShowDataForDate(day.Date);
        }

        private void UpdateSelectedDateHeader()
        {
            TxtSelectedDateHeader.Text = _selectedDay.Date == DateTime.Today ? "今天" : _selectedDay.ToString("yyyy年M月d日");
        }

        // ======================= 数据显示与同步 =======================

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
                tempAgenda.Add(new AgendaItem { Title = "这一天没有安排", Subtitle = "休息一下吧", IsEvent = true, IsTask = false });
            }

            // 👉 性能优化：直接使用新的 Collection 替换，防止 UI 单独重绘多次
            AgendaItems = new ObservableCollection<AgendaItem>(tempAgenda);
            AgendaListControl.ItemsSource = AgendaItems;

            AdjustWindowHeight();
        }

        private async Task SyncAllDataAsync(bool silent)
        {
            if (_googleAuth.CalendarSvc == null) await _googleAuth.AuthorizeAsync();
            if (_googleAuth.CalendarSvc == null) return;

            if (!silent)
            {
                AgendaItems.Clear();
                AgendaItems.Add(new AgendaItem { Title = "全量同步中...", Subtitle = "正在获取您的全部日程与任务", IsEvent = true });
            }

            try
            {
                var tempDayItems = new Dictionary<string, List<AgendaItem>>();
                var tempMarkedDates = new HashSet<string>();
                var tempEventCounts = new Dictionary<string, int>();

                var min = DateTime.Today.AddYears(-1);
                var max = DateTime.Today.AddYears(3);

                // 👉 性能优化：将庞大的云端数据获取、JSON拆解、实体构造放入后台线程，不卡顿主线程 UI！
                await Task.Run(async () =>
                {
                    string pageToken = null;
                    do
                    {
                        var req = _googleAuth.CalendarSvc.Events.List("primary");
                        req.TimeMinDateTimeOffset = min; req.TimeMaxDateTimeOffset = max; req.SingleEvents = true; req.MaxResults = 2500; req.PageToken = pageToken;
                        var events = await req.ExecuteAsync().ConfigureAwait(false);

                        if (events?.Items != null)
                        {
                            foreach (var ev in events.Items)
                            {
                                DateTime? date = ev.Start?.DateTime ?? (DateTime.TryParse(ev.Start?.Date, out var d) ? d : null);
                                if (date == null) continue;

                                string key = date.Value.ToString("yyyy-MM-dd");
                                if (!tempDayItems.ContainsKey(key)) tempDayItems[key] = new();

                                tempDayItems[key].Add(new AgendaItem
                                {
                                    Title = ev.Summary,
                                    Subtitle = ev.Start?.DateTime == null ? "全天" : ev.Start.DateTime.Value.ToString("HH:mm"),
                                    Location = ev.Location,
                                    IsEvent = true
                                });

                                tempMarkedDates.Add(key);
                                if (!tempEventCounts.ContainsKey(key)) tempEventCounts[key] = 0;
                                tempEventCounts[key]++;
                            }
                        }
                        pageToken = events?.NextPageToken;
                    } while (pageToken != null);

                    string taskPageToken = null;
                    do
                    {
                        var tasksReq = _googleAuth.TasksSvc.Tasks.List("@default");
                        tasksReq.ShowHidden = true; tasksReq.MaxResults = 100; tasksReq.PageToken = taskPageToken;
                        var tasks = await tasksReq.ExecuteAsync().ConfigureAwait(false);

                        if (tasks?.Items != null)
                        {
                            foreach (var t in tasks.Items)
                            {
                                bool isDone = t.Status == "completed";
                                DateTime taskDate = DateTime.Today;

                                if (!string.IsNullOrEmpty(t.Due) && DateTime.TryParse(t.Due, out var dueTime)) taskDate = dueTime.Date;
                                else if (isDone && !string.IsNullOrEmpty(t.Completed) && DateTime.TryParse(t.Completed, out var compTime)) taskDate = compTime.Date;
                                else if (isDone) continue;

                                string key = taskDate.ToString("yyyy-MM-dd");
                                if (!tempDayItems.ContainsKey(key)) tempDayItems[key] = new();

                                tempDayItems[key].Add(new AgendaItem
                                {
                                    Id = t.Id,
                                    Title = t.Title,
                                    Subtitle = "任务",
                                    IsEvent = false,
                                    IsTask = true,
                                    IsCompleted = isDone
                                });

                                if (!isDone) { tempMarkedDates.Add(key); if (!tempEventCounts.ContainsKey(key)) tempEventCounts[key] = 0; tempEventCounts[key]++; }
                            }
                        }
                        taskPageToken = tasks?.NextPageToken;
                    } while (taskPageToken != null);
                });

                // 计算完成后，将数据赋予UI层引用
                _localCache.DayItems = tempDayItems;
                MarkedDates = tempMarkedDates;
                EventCounts = tempEventCounts;
                await SaveCache(); // 配合异步方法，不再瞬间冻结界面

                foreach (var day in CalendarDays)
                {
                    string dateStr = day.Date.ToString("yyyy-MM-dd");
                    day.EventCount = EventCounts.TryGetValue(dateStr, out int count) ? count : 0;
                }

                ShowDataForDate(_selectedDay);
            }
            catch (Exception ex) { if (!silent) { AgendaItems.Clear(); AgendaItems.Add(new AgendaItem { Title = "同步失败", Subtitle = ex.Message, IsEvent = true }); } }
        }

        // ======================= 窗口生命周期与底层 =======================

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

                if (_currentDisplayMonth.Date != DateTime.Today)
                    BtnGoToToday_Click(null, null);

                _ = SyncAllDataAsync(true);
            }
        }

        private void AdjustWindowHeight()
        {
            if (_appWindow == null) return;

            int baseHeight = 560;
            int itemHeight = 65;
            int listHeight = AgendaItems.Count * itemHeight;

            if (AgendaItems.Count == 1 && !AgendaItems[0].IsTask && AgendaItems[0].Title == "这一天没有安排")
                listHeight = 52;

            if (AgendaItems.Count > 0) listHeight += 10;

            int targetHeight = Math.Min(baseHeight + listHeight, 820);
            var display = Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea;

            _appWindow.Resize(new SizeInt32(360, targetHeight));
            _appWindow.Move(new PointInt32(display.Width - 380, display.Height - targetHeight - 20));
        }

        private void FlyoutWindow_Activated(object sender, WindowActivatedEventArgs args) { if (args.WindowActivationState == WindowActivationState.Deactivated) _appWindow.Hide(); }

        private async void TaskCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is AgendaItem item && item.IsTask)
            {
                try
                {
                    cb.IsEnabled = false; item.IsCompleted = (cb.IsChecked == true);
                    var status = cb.IsChecked == true ? "completed" : "needsAction";
                    var updateRequest = _googleAuth.TasksSvc.Tasks.Patch(new Google.Apis.Tasks.v1.Data.Task { Id = item.Id, Status = status }, "@default", item.Id);
                    await updateRequest.ExecuteAsync();
                    _ = SyncAllDataAsync(silent: true);
                }
                catch { item.IsCompleted = !item.IsCompleted; }
                finally { cb.IsEnabled = true; }
            }
        }

        private void ChkAllDay_Checked(object sender, RoutedEventArgs e) { if (TimePickerNew != null) TimePickerNew.Visibility = Visibility.Collapsed; }
        private void ChkAllDay_Unchecked(object sender, RoutedEventArgs e) { if (TimePickerNew != null) TimePickerNew.Visibility = Visibility.Visible; }
        private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await SyncAllDataAsync(silent: false);
        private async void BtnSaveNewItem_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtNewTitle.Text)) return;

            AddTaskFlyout.Hide();
            string title = TxtNewTitle.Text; bool isEvent = RadioTypeEvent.IsChecked == true; bool isAllDay = ChkAllDay.IsChecked == true; TimeSpan selectedTime = TimePickerNew.Time;
            TxtNewTitle.Text = string.Empty;

            DateTime targetDate = _selectedDay; DateTime exactDateTime = targetDate.Add(selectedTime);

            try
            {
                AgendaItems.Add(new AgendaItem { Title = title, Subtitle = isAllDay ? "全天 (同步中...)" : exactDateTime.ToString("HH:mm") + " (同步中...)", IsTask = !isEvent, IsEvent = isEvent });
                AdjustWindowHeight();

                if (isEvent)
                {
                    var newEvent = new Google.Apis.Calendar.v3.Data.Event { Summary = title };
                    if (isAllDay) { newEvent.Start = new Google.Apis.Calendar.v3.Data.EventDateTime { Date = targetDate.ToString("yyyy-MM-dd") }; newEvent.End = new Google.Apis.Calendar.v3.Data.EventDateTime { Date = targetDate.AddDays(1).ToString("yyyy-MM-dd") }; }
                    else { newEvent.Start = new Google.Apis.Calendar.v3.Data.EventDateTime { DateTimeDateTimeOffset = exactDateTime }; newEvent.End = new Google.Apis.Calendar.v3.Data.EventDateTime { DateTimeDateTimeOffset = exactDateTime.AddHours(1) }; }
                    await _googleAuth.CalendarSvc.Events.Insert(newEvent, "primary").ExecuteAsync();
                }
                else
                {
                    var newTask = new Google.Apis.Tasks.v1.Data.Task { Title = title };
                    if (isAllDay) newTask.Due = targetDate.ToString("yyyy-MM-dd'T'00:00:00.000Z");
                    else newTask.Due = exactDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffK");
                    await _googleAuth.TasksSvc.Tasks.Insert(newTask, "@default").ExecuteAsync();
                }
                _ = SyncAllDataAsync(true);
            }
            catch { }
        }
    }
}