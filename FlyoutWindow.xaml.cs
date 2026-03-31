using Microsoft.UI.Windowing;
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
using Windows.Graphics;
using Windows.UI;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Globalization;

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
        private ResourceLoader _loader;

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
            _loader = new ResourceLoader();

            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);

            if (Application.Current is App app)
            {
                _syncManager = app.SyncManager;
            }

            ConfigureFlyoutStyle();
            LoadCache();
            StartClock();
            SetupPeriodicSync();

            Activated += FlyoutWindow_Activated;

            RootGrid.Loaded += RootGrid_Loaded;
        }

        private void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            AgendaListControl.ItemsSource = AgendaItems;

            if (MainCalendar.SelectedDates.Count == 0)
            {
                MainCalendar.SelectedDates.Add(DateTime.Today);
            }

            UpdateSelectedDateHeader();
            ShowDataForDate(_selectedDay);

            _ = SyncAllDataAsync(true);
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
            TxtRealTimeDate.Text = DateTime.Now.ToString("D", CultureInfo.CurrentUICulture);
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
            if (_selectedDay.Date == DateTime.Today)
            {
                TxtSelectedDateHeader.Text = _loader.GetString("CalendarPage_BtnToday/Content") ?? "今天";
                if (TxtRelativeDate != null) TxtRelativeDate.Visibility = Visibility.Collapsed;
            }
            else
            {
                TxtSelectedDateHeader.Text = _selectedDay.ToString("D", CultureInfo.CurrentUICulture);

                if (TxtRelativeDate != null)
                {
                    int days = (_selectedDay.Date - DateTime.Today).Days;
                    string relativeStr = days > 0 ? string.Format(_loader.GetString("TextDaysLater") ?? "{0}天后", days) : string.Format(_loader.GetString("TextDaysAgo") ?? "{0}天前", -days);

                    TxtRelativeDate.Text = $"({relativeStr})";
                    TxtRelativeDate.Visibility = Visibility.Visible;
                }
            }
        }

        private void ShowDataForDate(DateTime date)
        {
            string key = date.ToString("yyyy-MM-dd");
            var tempAgenda = new List<AgendaItem>();

            if (_localCache.DayItems.Count == 0)
            {
                tempAgenda.Add(new AgendaItem
                {
                    Title = _loader.GetString("TextWelcomeTitle") ?? "未连接账户",
                    Subtitle = _loader.GetString("TextWelcomeSub") ?? "请点击设置绑定",
                    IsEvent = false,
                    IsTask = false
                });
            }
            else if (_localCache.DayItems.ContainsKey(key) && _localCache.DayItems[key].Count > 0)
            {
                tempAgenda.AddRange(_localCache.DayItems[key]);
            }
            else
            {
                tempAgenda.Add(new AgendaItem
                {
                    Title = _loader.GetString("TextNoAgendaTitle") ?? "近期没有安排",
                    Subtitle = _loader.GetString("TextNoAgendaSub") ?? "享受空闲时光",
                    IsEvent = false,
                    IsTask = false
                });

                var nextDayKey = _localCache.DayItems.Keys
                    .Where(k => string.Compare(k, key) > 0)
                    .OrderBy(k => k)
                    .FirstOrDefault(k => _localCache.DayItems[k].Count > 0);

                if (nextDayKey != null)
                {
                    var nextItems = _localCache.DayItems[nextDayKey];
                    DateTime nextDate = DateTime.Parse(nextDayKey);
                    int daysDiff = (nextDate - date).Days;
                    string daysLaterText = daysDiff == 1 ? (_loader.GetString("TextTomorrow") ?? "明天")
                                                         : (daysDiff == 2 ? (_loader.GetString("TextDayAfterTomorrow") ?? "后天")
                                                         : nextDate.ToString("M", CultureInfo.CurrentUICulture));
                    foreach (var item in nextItems)
                    {
                        tempAgenda.Add(new AgendaItem
                        {
                            Id = item.Id,
                            Title = item.Title,
                            Subtitle = $"{(_loader.GetString("TextUpcoming") ?? "即将到来")} · {daysLaterText} {item.Subtitle}",
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
            var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
            bool isGoogle = settings.Values["IsGoogleConnected"] as bool? ?? false;
            bool isMs = settings.Values["IsMSConnected"] as bool? ?? false;

            if (!isGoogle && !isMs)
            {
                if (!silent)
                {
                    AgendaItems.Clear();
                    AgendaItems.Add(new AgendaItem
                    {
                        Title = _loader.GetString("TextWelcomeTitle") ?? "未连接账户",
                        Subtitle = _loader.GetString("TextWelcomeSub") ?? "请点击左下角设置，绑定您的日历账号",
                        IsEvent = false,
                        IsTask = false
                    });
                    AdjustWindowHeight();
                }
                return;
            }

            if (!silent)
            {
                AgendaItems.Clear();
                AgendaItems.Add(new AgendaItem
                {
                    Title = _loader.GetString("TextFullSyncTitle") ?? "正在同步...",
                    Subtitle = _loader.GetString("TextFullSyncSub") ?? "拉取最新数据中",
                    IsEvent = false,
                    IsTask = false
                });
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
                    AgendaItems.Add(new AgendaItem
                    {
                        Title = _loader.GetString("TextSyncFailed") ?? "同步失败",
                        Subtitle = ex.Message,
                        IsEvent = false,
                        IsTask = false
                    });
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

            int logicalWidth = 360;
            double targetLogicalHeight = 0;

            if (AddPanel != null && AddPanel.Visibility == Visibility.Visible)
            {
                ContentRow.Height = GridLength.Auto;
                RootGrid.UpdateLayout();
                RootGrid.Measure(new Windows.Foundation.Size(logicalWidth, double.PositiveInfinity));
                targetLogicalHeight = RootGrid.DesiredSize.Height;
            }
            else
            {
                ContentRow.Height = new GridLength(1, GridUnitType.Star);
                int listHeight = 0;
                foreach (var item in AgendaItems)
                {
                    if (item.Title == (_loader.GetString("TextNoAgendaTitle") ?? "近期没有安排")) listHeight += 50;
                    else if (item.Subtitle != null && item.Subtitle.Contains(_loader.GetString("TextUpcoming") ?? "即将到来")) listHeight += 65;
                    else listHeight += !string.IsNullOrEmpty(item.Location) ? 75 : 65;
                }
                targetLogicalHeight = 500 + listHeight + 45;
            }

            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            double scaleFactor = GetDpiForWindow(hWnd) / 96.0;

            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var display = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(windowId, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);
            var workArea = display.WorkArea;

            int physicalWidth = (int)(logicalWidth * scaleFactor);
            int physicalHeight = (int)(targetLogicalHeight * scaleFactor);

            int maxPhysicalHeight = workArea.Height - 80;

            if (physicalHeight > maxPhysicalHeight)
            {
                physicalHeight = maxPhysicalHeight;
                AgendaListControl.SetValue(ScrollViewer.VerticalScrollModeProperty, ScrollMode.Enabled);

                if (AddPanelScrollViewer != null)
                {
                    AddPanelScrollViewer.VerticalScrollMode = ScrollMode.Enabled;
                }
            }
            else
            {
                AgendaListControl.SetValue(ScrollViewer.VerticalScrollModeProperty, ScrollMode.Disabled);

                if (AddPanelScrollViewer != null)
                {
                    AddPanelScrollViewer.VerticalScrollMode = ScrollMode.Disabled;
                }
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

            TimePickerStart.Header = isEvent ? (_loader.GetString("TextStartTime") ?? "开始时间") : (_loader.GetString("TextDueTime") ?? "截止时间");

            if (isEvent)
            {
                Grid.SetColumnSpan(TimePickerStart, 1);
            }
            else
            {
                Grid.SetColumnSpan(TimePickerStart, 3);
            }

            if (AddPanel != null && AddPanel.Visibility == Visibility.Visible)
            {
                RootGrid.UpdateLayout();
                AdjustWindowHeight();
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

        private void SetupFlyoutProviderComboBox()
        {
            CmbAddProvider.Items.Clear();
            var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
            bool isGoogle = settings.Values["IsGoogleConnected"] as bool? ?? false;
            bool isMs = settings.Values["IsMSConnected"] as bool? ?? false;

            if (isGoogle) CmbAddProvider.Items.Add(new ComboBoxItem { Content = "Google", Tag = "Google" });
            if (isMs) CmbAddProvider.Items.Add(new ComboBoxItem { Content = "Microsoft", Tag = "Microsoft" });

            if (CmbAddProvider.Items.Count > 1)
            {
                CmbAddProvider.Visibility = Visibility.Visible;
                CmbAddProvider.SelectedIndex = 0;
            }
            else
            {
                CmbAddProvider.Visibility = Visibility.Collapsed;
                if (CmbAddProvider.Items.Count > 0) CmbAddProvider.SelectedIndex = 0;
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
                SetupFlyoutProviderComboBox();
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

            AddPanel.Visibility = Visibility.Collapsed;
            if (AgendaContainer != null) AgendaContainer.Visibility = Visibility.Visible;

            string title = TxtNewTitle.Text;
            bool isEvent = RadioTypeEvent.IsChecked == true;
            bool isAllDay = ChkAllDay.IsChecked == true;
            string location = TxtLocation.Text;
            string providerName = (CmbAddProvider.SelectedItem as ComboBoxItem)?.Tag.ToString() ?? "Google";

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
                string timeDisplay = isAllDay ? (_loader.GetString("TextAllDay") ?? "全天") : (isEvent ? $"{exactStartDateTime:HH:mm} - {exactEndDateTime:HH:mm}" : $"{exactStartDateTime:HH:mm}");
                AgendaItems.Add(new AgendaItem
                {
                    Title = title,
                    Subtitle = $"{timeDisplay} ({_loader.GetString("TextSyncing") ?? "同步中..."})",
                    Location = location,
                    IsTask = !isEvent,
                    IsEvent = isEvent,
                    Provider = providerName
                });
                AdjustWindowHeight();

                await _syncManager.CreateItemAsync(title, isEvent, isAllDay, targetDate, startTime, endTime, location, providerName);
                _ = SyncAllDataAsync(true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("============== Sync Error ==============");
                System.Diagnostics.Debug.WriteLine(ex.ToString());

                AgendaItems.Insert(0, new AgendaItem
                {
                    Title = _loader.GetString("TextAddFailed") ?? "添加失败",
                    Subtitle = ex.Message,
                    IsEvent = false,
                    IsTask = false
                });
                AdjustWindowHeight();
            }
        }
    }
}