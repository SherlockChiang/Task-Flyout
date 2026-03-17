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
        private GoogleAuthService _googleAuth;
        private AppWindow _appWindow;
        private AppCache _localCache = new();
        private DispatcherTimer _syncTimer;
        private DispatcherTimer _clockTimer;

        private readonly string CacheFilePath =
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TaskFlyout", "local_cache_winui3.json");

        public ObservableCollection<AgendaItem> AgendaItems { get; set; } = new();
        public HashSet<string> MarkedDates { get; set; } = new();
        public Dictionary<string, int> EventCounts { get; set; } = new();

        private DateTime _selectedDay = DateTime.Today;

        public FlyoutWindow()
        {
            InitializeComponent();
            _googleAuth = new GoogleAuthService();
            AgendaListControl.ItemsSource = AgendaItems;

            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);

            ConfigureFlyoutStyle();
            LoadCache();
            StartClock();

            // 初始化选中今天
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

        // ======================= 日历控制 =======================
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
                Color accentColor = Color.FromArgb(255, 0, 120, 215); // 默认蓝色

                // 获取系统的强调色 (SystemAccentColor)
                if (Application.Current.Resources.TryGetValue("SystemAccentColor", out var res) && res is Color sysColor)
                {
                    accentColor = sysColor;
                }

                // 👉 核心修改：设置一个低透明度的柔和背景 (透明度设为 40/255，约 15%)
                args.Item.Background = new SolidColorBrush(Color.FromArgb(40, accentColor.R, accentColor.G, accentColor.B));

                // 确保清除原生的横条提示
                args.Item.SetDensityColors(null);
            }
            else
            {
                // 如果没有日程，重置为透明背景
                args.Item.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                args.Item.SetDensityColors(null);
            }
        }

        private void MainCalendar_SelectedDatesChanged(CalendarView sender, CalendarViewSelectedDatesChangedEventArgs args)
        {
            if (args.AddedDates.Count == 0) return;

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
                tempAgenda.Add(new AgendaItem { Title = "这一天没有安排", Subtitle = "休息一下吧", IsEvent = true, IsTask = false });
            }

            AgendaItems = new ObservableCollection<AgendaItem>(tempAgenda);
            AgendaListControl.ItemsSource = AgendaItems;
            AdjustWindowHeight();
        }

        // ======================= Google 同步逻辑 =======================
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

                await Task.Run(async () =>
                {
                    // 日程同步
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

                    // 任务同步
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

                _localCache.DayItems = tempDayItems;
                MarkedDates = tempMarkedDates;
                EventCounts = tempEventCounts;
                await SaveCache();

                // 强制 CalendarView 刷新标记数据
                MainCalendar.UpdateLayout();

                ShowDataForDate(_selectedDay);
            }
            catch (Exception ex)
            {
                if (!silent) { AgendaItems.Clear(); AgendaItems.Add(new AgendaItem { Title = "同步失败", Subtitle = ex.Message, IsEvent = true }); }
            }
        }

        // ======================= 底层与交互控制 =======================
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

                // 如果没选中今天，重置为今天
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
            int baseHeight = 560; // 控制面板基础高度
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