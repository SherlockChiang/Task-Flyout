using DesktopFlyouts;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Task_Flyout.Models;
using Task_Flyout.Services;
using Windows.UI;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Globalization;

namespace Task_Flyout
{
    public class AppCache
    {
        public HashSet<string> MarkedDates { get; set; } = new();
        public Dictionary<string, List<AgendaItem>> DayItems { get; set; } = new();
        public List<AgendaCacheRange> CachedRanges { get; set; } = new();
    }

    public class AgendaCacheRange
    {
        public string ProviderName { get; set; } = "";
        public string AccountId { get; set; } = "";
        public string StartDateKey { get; set; } = "";
        public string EndDateKey { get; set; } = "";
    }

    public partial class ColorHexToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var hex = value as string ?? string.Empty;
            if (hex.StartsWith('#'))
                return new SolidColorBrush(Services.ColorHelper.ParseHex(hex));

            if (Application.Current.Resources.TryGetValue("SystemAccentColor", out var res) && res is Color sysColor)
                return new SolidColorBrush(sysColor);

            return new SolidColorBrush(Color.FromArgb(255, 0, 120, 215));
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
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

    public sealed partial class FlyoutWindow : DesktopFlyout
    {
        private AppCache _localCache = new();
        private DispatcherTimer? _syncTimer;
        private DispatcherTimer? _clockTimer;
        private SyncManager _syncManager = null!;
        private ResourceLoader _loader;
        private ElementTheme? _appliedTheme;

        private DateTime _lastHideTime = DateTime.MinValue;
        private bool _isPinned = false;

        private DispatcherTimer? _dotRefreshTimer;
        private bool _isDotRefreshPending = false;

        private ScrollViewer? _activeScrollViewer;
        private readonly Dictionary<CalendarViewDayItem, DateTime> _realizedDayItems = new();
        private readonly Dictionary<CalendarViewDayItem, List<Color>> _semanticDotColors = new();
        private long _dayItemGeneration;
        private long _agendaCacheVersion;
        private DateTime _localCacheAnchorMonth = DateTime.MinValue;
        private long _filterVersion;
        private DateTime _displayedMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
        private CalendarDotRenderKey? _lastDotRenderKey;
        private string? _failedTaskMutationKey;
        private Func<Task>? _retryTaskMutationSucceeded;
        private readonly List<DotSpec> _dotSpecs = new();
        private readonly Dictionary<uint, SolidColorBrush> _dotBrushCache = new();

        public ObservableCollection<AgendaItem> AgendaItems { get; set; } = new();
        public HashSet<string> MarkedDates { get; set; } = new();
        public Dictionary<string, int> EventCounts { get; set; } = new();

        private DateTime _selectedDay = DateTime.Today;

        private const int VisibleCacheFutureDays = 45;
        private static readonly SemaphoreSlim _syncLock = new(1, 1);
        private bool _backgroundRefreshQueued;
        private bool _flyoutContentLoaded;
        private bool _contentInitialized;
        private bool _showPending;
        private bool _openRequestIssued;
        private bool _desiredOpen;
        private TaskCompletionSource? _mainWindowDismissal;
        private bool _focusNewItemOnOpen;
        private bool _isShuttingDown;
        private bool _suppressSelectedDateChanged;
        private DispatcherTimer? _activationHandoffTimer;
        private IntPtr _openingForegroundAnchor;
        private bool _openingFlyoutWasForeground;
        private int _activationHandoffAttempts;
        private long _isOpenChangedToken;
        private DateTimeOffset? _lastSyncSucceededAt;
        private FlyoutAgendaSurfaceKind _agendaSurfaceKind = FlyoutAgendaSurfaceKind.Content;
        private string _agendaStateTitle = "";
        private string _agendaStateSubtitle = "";
        private long _lastBackgroundRefreshStartedTimestamp;
        private CancellationTokenSource? _backgroundRefreshCts;
        private CancellationTokenSource? _weatherRefreshCts;
        private long _weatherRefreshGeneration;

        private static readonly TimeSpan BackgroundRefreshCooldown = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan ActivationHandoffInterval = TimeSpan.FromMilliseconds(16);
        private const int MaxActivationHandoffAttempts = 16;

        private readonly record struct DotSpec(double Left, double Top, SolidColorBrush Fill);

        public FlyoutWindow()
        {
            InitializeComponent();
            RootGrid.Language = Windows.Globalization.ApplicationLanguages.Languages[0];
            _loader = new ResourceLoader();

            if (Application.Current is App app)
            {
                _syncManager = app.SyncManager;
            }

            StartClock();
            SetupPeriodicSync();
            _syncTimer?.Stop();
            _isOpenChangedToken = RegisterPropertyChangedCallback(IsOpenProperty, OnIsOpenChanged);
            RootGrid.Loaded += RootGrid_Loaded;
            FlyoutIsland.ActualThemeChanged += FlyoutIsland_ActualThemeChanged;
            ApplyConfiguredTheme(App.GetConfiguredTheme());
            if (RootGrid.IsLoaded)
                RootGrid_Loaded(RootGrid, new RoutedEventArgs());
        }

        private void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            _flyoutContentLoaded = true;
            if (_contentInitialized)
            {
                if (_showPending && _desiredOpen)
                    DispatcherQueue.TryEnqueue(OpenPreparedFlyout);
                return;
            }
            _contentInitialized = true;
            if (MainCalendar.SelectedDates.Count == 0)
            {
                _suppressSelectedDateChanged = true;
                try { MainCalendar.SelectedDates.Add(DateTime.Today); }
                finally { _suppressSelectedDateChanged = false; }
            }

            UpdateSelectedDateHeader();
            LoadCacheForDate(_selectedDay);
            ShowDataForDate(_selectedDay);

            MainCalendar.RegisterPropertyChangedCallback(CalendarView.DisplayModeProperty, (s, args) =>
            {
                RequestDotRefresh();
            });

            if (_showPending && _desiredOpen)
                DispatcherQueue.TryEnqueue(OpenPreparedFlyout);
        }

        private void OnScrollViewerViewChanging(object? sender, ScrollViewerViewChangingEventArgs e) => ScheduleDotRefresh(TimeSpan.FromMilliseconds(80));
        private void OnScrollViewerViewChanged(object? sender, ScrollViewerViewChangedEventArgs e) => ScheduleDotRefresh(TimeSpan.FromMilliseconds(80));

        private void HookActiveScrollViewer()
        {
            if (_activeScrollViewer != null) return;
            var dayItem = _realizedDayItems.Keys.FirstOrDefault();
            if (dayItem == null) return;

            DependencyObject current = dayItem;
            while (current != null && current != MainCalendar)
            {
                if (current is ScrollViewer sv)
                {
                    _activeScrollViewer = sv;
                    _activeScrollViewer.ViewChanging += OnScrollViewerViewChanging;
                    _activeScrollViewer.ViewChanged += OnScrollViewerViewChanged;
                    break;
                }
                current = VisualTreeHelper.GetParent(current);
            }
        }

        private void StartClock()
        {
            _showSeconds = Windows.Storage.ApplicationData.Current.LocalSettings.Values["ShowSeconds"] as bool? ?? false;
            UpdateClock();
            _clockTimer = new DispatcherTimer { Interval = _showSeconds ? TimeSpan.FromSeconds(1) : TimeSpan.FromMinutes(1) };
            _clockTimer.Tick += (s, e) => UpdateClock();
        }

        private bool _showSeconds;

        private void UpdateClock()
        {
            string format = _showSeconds ? "HH:mm:ss" : "HH:mm";
            TxtRealTimeClock.Text = DateTime.Now.ToString(format);
            TxtRealTimeDate.Text = DateTime.Now.ToString("D", CultureInfo.CurrentUICulture);
        }

        public void UpdateClockFormat()
        {
            _showSeconds = Windows.Storage.ApplicationData.Current.LocalSettings.Values["ShowSeconds"] as bool? ?? false;
            if (_clockTimer != null)
                _clockTimer.Interval = _showSeconds ? TimeSpan.FromSeconds(1) : TimeSpan.FromMinutes(1);
            UpdateClock();
        }

        private void SetupPeriodicSync()
        {
            int intervalMin = Windows.Storage.ApplicationData.Current.LocalSettings.Values["SyncIntervalMinutes"] as int? ?? 15;
            _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(intervalMin) };
            _syncTimer.Tick += async (_, _) =>
            {
                try { await SyncAllDataAsync(true, forceRefresh: true); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Periodic sync tick failed: {ex.Message}"); }
            };
        }

        public void UpdateSyncInterval(int minutes)
        {
            if (_syncTimer != null)
            {
                bool wasEnabled = _syncTimer.IsEnabled;
                if (wasEnabled)
                    _syncTimer.Stop();

                _syncTimer.Interval = TimeSpan.FromMinutes(minutes);

                if (wasEnabled || IsOpen)
                    _syncTimer.Start();
            }
        }

        private void LoadCacheForDate(DateTime anchorDate)
        {
            try
            {
                if (_syncManager != null)
                {
                    var anchorMonth = new DateTime(anchorDate.Year, anchorDate.Month, 1);
                    long knownVersion = CalendarMonthRangePolicy.GetReusableSnapshotVersion(
                        _localCacheAnchorMonth,
                        anchorDate,
                        _agendaCacheVersion);
                    var snapshot = _syncManager.GetVersionedDayItemsSnapshotIfChanged(
                        knownVersion,
                        GetVisibleCacheDateKeys(anchorDate));
                    if (snapshot == null) return;

                    _localCache = new AppCache
                    {
                        DayItems = snapshot.DayItems,
                        MarkedDates = snapshot.DayItems.Keys.ToHashSet(StringComparer.Ordinal)
                    };
                    _agendaCacheVersion = snapshot.Version;
                    _localCacheAnchorMonth = anchorMonth;
                    MarkedDates = new HashSet<string>(_localCache.MarkedDates);
                    EventCounts.Clear();
                    foreach (var kvp in _localCache.DayItems)
                    {
                        int count = kvp.Value.Count(i => IsItemVisible(i));
                        if (count > 0) EventCounts[kvp.Key] = count;
                    }
                }
            }
            catch
            {
                _localCache = new();
                _agendaCacheVersion = -1;
                _localCacheAnchorMonth = DateTime.MinValue;
                _lastDotRenderKey = null;
            }
        }

        public void Prewarm()
        {
            if (_isShuttingDown) return;
            LoadCacheForDate(DateTime.Today);
            _selectedDay = DateTime.Today;
            ShowDataForDate(_selectedDay);
            UpdateClock();
        }

        private static IEnumerable<string> GetVisibleCacheDateKeys(DateTime anchorDate)
        {
            var firstOfMonth = new DateTime(anchorDate.Year, anchorDate.Month, 1);
            var visibleStart = LocalizationHelper.GetWeekStart(
                firstOfMonth,
                LocalizationHelper.AppCulture.DateTimeFormat.FirstDayOfWeek);
            var visibleEnd = visibleStart.AddDays(42 + VisibleCacheFutureDays);

            for (var day = visibleStart.Date; day < visibleEnd.Date; day = day.AddDays(1))
                yield return day.ToString("yyyy-MM-dd");
        }

        private async Task SaveCache()
        {
            try
            {
                if (_syncManager != null)
                    await _syncManager.SaveLocalCacheAsync();
            }
            catch { }
        }

        private void BtnGoToToday_Click(object sender, RoutedEventArgs e)
        {
            if (MainCalendar.DisplayMode != CalendarViewDisplayMode.Month)
            {
                MainCalendar.DisplayMode = CalendarViewDisplayMode.Month;
            }

            MainCalendar.SetDisplayDate(DateTime.Today);
            MainCalendar.SelectedDates.Clear();
            MainCalendar.SelectedDates.Add(DateTime.Today);

            RequestDotRefresh();
        }

        private void MainCalendar_CalendarViewDayItemChanging(CalendarView sender, CalendarViewDayItemChangingEventArgs args)
        {
            if (args.InRecycleQueue)
            {
                if (_realizedDayItems.Remove(args.Item))
                {
                    _semanticDotColors.Remove(args.Item);
                    _dayItemGeneration++;
                }
                args.Item.Loaded -= DayItem_Loaded;
                RequestDotRefresh();
                return;
            }

            if (args.Phase == 0)
            {
                var date = args.Item.Date.Date;
                if (!_realizedDayItems.TryGetValue(args.Item, out var previousDate) || previousDate != date)
                {
                    _realizedDayItems[args.Item] = date;
                    _semanticDotColors.Remove(args.Item);
                    _dayItemGeneration++;
                }

                args.Item.CornerRadius = new CornerRadius(16);
                args.Item.SetDensityColors(null);

                args.Item.Loaded -= DayItem_Loaded;
                args.Item.Loaded += DayItem_Loaded;
                RequestDotRefresh();
            }
        }

        private void DayItem_Loaded(object sender, RoutedEventArgs e)
        {
            ScheduleDotRefresh(TimeSpan.FromMilliseconds(200));
        }

        private void ScheduleDotRefresh(TimeSpan delay)
        {
            if (_dotRefreshTimer == null)
            {
                _dotRefreshTimer = new DispatcherTimer();
                _dotRefreshTimer.Tick += (s, args) =>
                {
                    _dotRefreshTimer.Stop();
                    RequestDotRefresh();
                };
            }

            _dotRefreshTimer.Interval = delay;
            _dotRefreshTimer.Stop();
            _dotRefreshTimer.Start();
        }

        private void RequestDotRefresh()
        {
            if (_isDotRefreshPending) return;
            _isDotRefreshPending = true;

            if (!DispatcherQueue.TryEnqueue(() =>
            {
                _isDotRefreshPending = false;
                RefreshAllDots();
            }))
            {
                _isDotRefreshPending = false;
            }
        }

        private void RefreshAllDots()
        {
            if (DotOverlay == null || MainCalendar == null) return;

            if (MainCalendar.DisplayMode != CalendarViewDisplayMode.Month)
            {
                var nonMonthKey = new CalendarDotRenderKey(
                    _displayedMonth.Year,
                    _displayedMonth.Month,
                    _agendaCacheVersion,
                    (int)MainCalendar.DisplayMode,
                    _dayItemGeneration,
                    _filterVersion);
                if (CalendarDotRenderPolicy.RequiresSemanticRender(_lastDotRenderKey, nonMonthKey))
                {
                    ClearDotOverlay();
                    _semanticDotColors.Clear();
                    _lastDotRenderKey = nonMonthKey;
                }
                return;
            }

            HookActiveScrollViewer();
            _dotSpecs.Clear();

            var dayItems = GetRealizedDayItems();
            UpdateDisplayedMonth(dayItems);

            var renderKey = new CalendarDotRenderKey(
                _displayedMonth.Year,
                _displayedMonth.Month,
                _agendaCacheVersion,
                (int)MainCalendar.DisplayMode,
                _dayItemGeneration,
                _filterVersion);
            if (CalendarDotRenderPolicy.RequiresSemanticRender(_lastDotRenderKey, renderKey))
            {
                RebuildSemanticDotCache(dayItems);
                _lastDotRenderKey = renderKey;
            }

            if (EventCounts == null || EventCounts.Count == 0)
            {
                ClearDotOverlay();
                return;
            }

            foreach (var item in dayItems)
            {
                if (!_semanticDotColors.TryGetValue(item, out var dotColors) || dotColors.Count == 0)
                    continue;

                try
                {
                    double dotSize = 4.5;
                    double spacing = 2.5;
                    double bottomMargin = 6;

                    if (_activeScrollViewer != null)
                    {
                        var transformToSv = item.TransformToVisual(_activeScrollViewer);
                        var posInSv = transformToSv.TransformPoint(new Windows.Foundation.Point(0, 0));

                        double dotYInSv = posInSv.Y + item.ActualHeight - bottomMargin;

                        if (dotYInSv < 0 || dotYInSv > _activeScrollViewer.ActualHeight)
                            continue;
                    }

                    var transformToCanvas = item.TransformToVisual(DotOverlay);
                    var posInCanvas = transformToCanvas.TransformPoint(new Windows.Foundation.Point(0, 0));

                    int dotsToShow = Math.Min(dotColors.Count, 3);
                    double totalWidth = (dotsToShow * dotSize) + ((dotsToShow - 1) * spacing);

                    double startX = posInCanvas.X + (item.ActualWidth - totalWidth) / 2;
                    double dotYInCanvas = posInCanvas.Y + item.ActualHeight - dotSize - bottomMargin;

                    for (int i = 0; i < dotsToShow; i++)
                    {
                        _dotSpecs.Add(new DotSpec(
                            startX + i * (dotSize + spacing),
                            dotYInCanvas,
                            GetDotBrush(dotColors[i])));
                    }
                }
                catch
                {
                }
            }

            ApplyDotOverlay(_dotSpecs);
        }

        private void UpdateDisplayedMonth(IReadOnlyList<CalendarViewDayItem> dayItems)
        {
            if (_activeScrollViewer == null || dayItems.Count == 0) return;

            var visibleDays = new List<RealizedDayVisibility>(dayItems.Count);
            foreach (var item in dayItems)
            {
                try
                {
                    var point = item.TransformToVisual(_activeScrollViewer)
                        .TransformPoint(new Windows.Foundation.Point(0, 0));
                    visibleDays.Add(new RealizedDayVisibility(item.Date.Date, point.Y, point.Y + item.ActualHeight));
                }
                catch { }
            }

            var detected = CalendarDotRenderPolicy.DetectDisplayedMonth(
                visibleDays,
                _activeScrollViewer.ActualHeight);
            if (detected == null || detected.Value == _displayedMonth) return;

            _displayedMonth = detected.Value;
            LoadCacheForDate(_displayedMonth);
        }

        private void RebuildSemanticDotCache(IReadOnlyList<CalendarViewDayItem> dayItems)
        {
            _semanticDotColors.Clear();
            var accountMgr = (App.Current as App)?.SyncManager?.AccountManager;

            foreach (var item in dayItems)
            {
                var dateStr = item.Date.Date.ToString("yyyy-MM-dd");
                bool hasEvent = EventCounts.TryGetValue(dateStr, out int count) && count > 0;
                item.FontWeight = hasEvent
                    ? Microsoft.UI.Text.FontWeights.Bold
                    : Microsoft.UI.Text.FontWeights.Normal;
                if (!hasEvent) continue;

                var colors = new List<Color>();
                if (_localCache.DayItems.TryGetValue(dateStr, out var agendaItems))
                {
                    foreach (var agendaItem in agendaItems)
                    {
                        if (accountMgr != null && !accountMgr.IsItemVisible(agendaItem)) continue;
                        accountMgr?.PopulateItemColor(agendaItem);
                        var color = !string.IsNullOrEmpty(agendaItem.ColorHex)
                            ? Services.ColorHelper.ParseHex(agendaItem.ColorHex)
                            : Color.FromArgb(255, 0, 120, 215);
                        if (!colors.Any(existing => existing.R == color.R && existing.G == color.G && existing.B == color.B))
                            colors.Add(color);
                    }
                }
                if (colors.Count == 0)
                    colors.Add(Color.FromArgb(255, 0, 120, 215));
                _semanticDotColors[item] = colors;
            }
        }

        private void ApplyDotOverlay(IReadOnlyList<DotSpec> dots)
        {
            while (DotOverlay.Children.Count > dots.Count)
                DotOverlay.Children.RemoveAt(DotOverlay.Children.Count - 1);

            while (DotOverlay.Children.Count < dots.Count)
            {
                DotOverlay.Children.Add(new Ellipse
                {
                    Width = 4.5,
                    Height = 4.5,
                    IsHitTestVisible = false
                });
            }

            for (int i = 0; i < dots.Count; i++)
            {
                var dot = (Ellipse)DotOverlay.Children[i];
                dot.Fill = dots[i].Fill;
                Canvas.SetLeft(dot, dots[i].Left);
                Canvas.SetTop(dot, dots[i].Top);
            }
        }

        private void ClearDotOverlay()
        {
            _dotSpecs.Clear();
            DotOverlay.Children.Clear();
        }

        private SolidColorBrush GetDotBrush(Color color)
        {
            uint key = ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
            if (_dotBrushCache.TryGetValue(key, out var brush))
                return brush;

            if (_dotBrushCache.Count > 64)
                _dotBrushCache.Clear();

            brush = new SolidColorBrush(color);
            _dotBrushCache[key] = brush;
            return brush;
        }

        private List<CalendarViewDayItem> GetRealizedDayItems()
            => _realizedDayItems.Keys.Where(item => item.IsLoaded).ToList();

        private void MainCalendar_SelectedDatesChanged(CalendarView sender, CalendarViewSelectedDatesChangedEventArgs args)
        {
            if (_suppressSelectedDateChanged || args.AddedDates.Count == 0) return;

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
                TxtSelectedDateHeader.Text = _loader.GetStringOrDefault("CalendarPage_BtnToday/Content") ?? "Today";
                if (TxtRelativeDate != null) TxtRelativeDate.Visibility = Visibility.Collapsed;
            }
            else
            {
                TxtSelectedDateHeader.Text = _selectedDay.ToString("D", CultureInfo.CurrentUICulture);

                if (TxtRelativeDate != null)
                {
                    int days = (_selectedDay.Date - DateTime.Today).Days;
                    string relativeStr = days > 0 ? string.Format(_loader.GetStringOrDefault("TextDaysLater") ?? "In {0} days", days) : string.Format(_loader.GetStringOrDefault("TextDaysAgo") ?? "{0} days ago", -days);

                    TxtRelativeDate.Text = $"({relativeStr})";
                    TxtRelativeDate.Visibility = Visibility.Visible;
                }
            }
        }

        private void ShowDataForDate(DateTime date)
        {
            string key = date.ToString("yyyy-MM-dd");
            var tempAgenda = new List<AgendaItem>();
            var surfaceKind = FlyoutAgendaSurfaceKind.Content;
            string stateTitle = "";
            string stateSubtitle = "";

            if (_localCache.DayItems.Count == 0)
            {
                if (HasConnectedAgendaAccounts())
                {
                    surfaceKind = FlyoutAgendaSurfaceKind.Empty;
                    stateTitle = _loader.GetStringOrDefault("TextNoAgendaTitle") ?? "No upcoming events";
                    stateSubtitle = _loader.GetStringOrDefault("TextNoAgendaSub") ?? "Take a break";
                }
                else
                {
                    surfaceKind = FlyoutAgendaSurfaceKind.Onboarding;
                    stateTitle = _loader.GetStringOrDefault("TextWelcomeTitle") ?? "Welcome to Task Flyout";
                    stateSubtitle = _loader.GetStringOrDefault("TextWelcomeSub") ?? "Click setting icon to link accounts";
                }
            }
            else if (_localCache.DayItems.ContainsKey(key) && _localCache.DayItems[key].Any(IsItemVisible))
            {
                var visibleItems = _localCache.DayItems[key].Where(IsItemVisible).ToList();
                foreach (var item in visibleItems) PopulateItemColor(item);
                tempAgenda.AddRange(visibleItems);
            }
            else
            {
                surfaceKind = FlyoutAgendaSurfaceKind.Empty;
                stateTitle = _loader.GetStringOrDefault("TextNoAgendaTitle") ?? "No upcoming events";
                stateSubtitle = _loader.GetStringOrDefault("TextNoAgendaSub") ?? "Take a break";

                var nextDayKey = _localCache.DayItems.Keys
                    .Where(k => string.Compare(k, key) > 0)
                    .OrderBy(k => k)
                    .FirstOrDefault(k => _localCache.DayItems[k].Any(IsItemVisible));

                if (nextDayKey != null)
                {
                    var nextItems = _localCache.DayItems[nextDayKey].Where(IsItemVisible).ToList();
                    DateTime nextDate = DateTime.Parse(nextDayKey);
                    int daysDiff = (nextDate - date).Days;
                    string daysLaterText = daysDiff == 1 ? (_loader.GetStringOrDefault("TextTomorrow") ?? "Tomorrow")
                                                         : (daysDiff == 2 ? (_loader.GetStringOrDefault("TextDayAfterTomorrow") ?? "The day after tomorrow")
                                                         : nextDate.ToString("M", CultureInfo.CurrentUICulture));
                    foreach (var item in nextItems)
                    {
                        PopulateItemColor(item);
                        tempAgenda.Add(new AgendaItem
                        {
                            Id = item.Id,
                            Title = item.Title,
                            Subtitle = $"{(_loader.GetStringOrDefault("TextUpcoming") ?? "Upcoming")} · {daysLaterText} {item.Subtitle}",
                            Location = item.Location,
                            IsEvent = item.IsEvent,
                            IsTask = item.IsTask,
                            IsCompleted = item.IsCompleted,
                            Provider = item.Provider,
                            AccountId = item.AccountId,
                            CalendarId = item.CalendarId,
                            ColorHex = item.ColorHex,
                            DateKey = item.DateKey
                        });
                    }
                }
            }

            bool itemsChanged = ReplaceAgendaItems(tempAgenda);
            bool surfaceChanged = ApplyAgendaSurface(surfaceKind, stateTitle, stateSubtitle, tempAgenda.Count > 0);

            if (itemsChanged || surfaceChanged)
                AdjustWindowHeight();
        }

        private bool HasConnectedAgendaAccounts()
            => (App.Current as App)?.SyncManager?.AccountManager.Accounts.Count > 0;

        private bool ReplaceAgendaItems(IReadOnlyList<AgendaItem> items)
        {
            if (AgendaItems.Count == items.Count && AgendaItems.Zip(items, AgendaItemsEqual).All(equal => equal))
                return false;

            AgendaItems.Clear();
            foreach (var item in items)
                AgendaItems.Add(item);
            return true;
        }

        private bool ApplyAgendaSurface(
            FlyoutAgendaSurfaceKind kind,
            string title,
            string subtitle,
            bool hasAgendaItems)
        {
            var presentation = FlyoutAgendaSurfacePolicy.GetPresentation(kind, hasAgendaItems);
            var stateVisibility = presentation.ShowState ? Visibility.Visible : Visibility.Collapsed;
            var listVisibility = presentation.ShowList ? Visibility.Visible : Visibility.Collapsed;
            var retryVisibility = presentation.ShowRetry ? Visibility.Visible : Visibility.Collapsed;
            var addAccountVisibility = presentation.ShowAddAccount ? Visibility.Visible : Visibility.Collapsed;
            bool changed = _agendaSurfaceKind != kind
                || _agendaStateTitle != title
                || _agendaStateSubtitle != subtitle
                || AgendaStatePanel.Visibility != stateVisibility
                || AgendaListControl.Visibility != listVisibility
                || AgendaSyncRetryButton.Visibility != retryVisibility
                || AgendaAddAccountButton.Visibility != addAccountVisibility;

            _agendaSurfaceKind = kind;
            _agendaStateTitle = title;
            _agendaStateSubtitle = subtitle;
            AgendaStateTitle.Text = title;
            AgendaStateSubtitle.Text = subtitle;
            AgendaStateSubtitle.Visibility = string.IsNullOrWhiteSpace(subtitle)
                ? Visibility.Collapsed
                : Visibility.Visible;
            AgendaStatePanel.Visibility = stateVisibility;
            AgendaListControl.Visibility = listVisibility;
            AgendaSyncRetryButton.Visibility = retryVisibility;
            AgendaAddAccountButton.Visibility = addAccountVisibility;
            return changed;
        }

        private static bool AgendaItemsEqual(AgendaItem left, AgendaItem right)
            => left.Id == right.Id
               && left.Title == right.Title
               && left.Subtitle == right.Subtitle
               && left.Location == right.Location
               && left.Description == right.Description
               && left.IsEvent == right.IsEvent
               && left.IsTask == right.IsTask
               && left.IsCompleted == right.IsCompleted
               && left.Provider == right.Provider
               && left.AccountId == right.AccountId
               && left.CalendarId == right.CalendarId
               && left.CalendarName == right.CalendarName
               && left.ColorHex == right.ColorHex
               && left.DateKey == right.DateKey
               && left.StartDateTime == right.StartDateTime
               && left.EndDateTime == right.EndDateTime
               && left.IsRecurring == right.IsRecurring
               && left.RecurringEventId == right.RecurringEventId
               && left.RecurrenceKind == right.RecurrenceKind;

        private async Task SyncAllDataAsync(bool silent, bool forceRefresh = false, bool fullSync = false)
        {
            var accountMgr = (App.Current as App)?.SyncManager?.AccountManager;

            if (accountMgr == null || accountMgr.Accounts.Count == 0)
            {
                if (!silent)
                {
                    SetSyncProgressVisible(false);
                    ReplaceAgendaItems(Array.Empty<AgendaItem>());
                    ApplyAgendaSurface(
                        FlyoutAgendaSurfaceKind.Onboarding,
                        _loader.GetStringOrDefault("TextWelcomeTitle") ?? "Welcome to Task Flyout",
                        _loader.GetStringOrDefault("TextWelcomeSub") ?? "Click setting icon to link accounts",
                        hasAgendaItems: false);
                    AdjustWindowHeight();
                }
                return;
            }

            if (!await _syncLock.WaitAsync(0)) return;

            try
            {
                forceRefresh |= !silent;
                var providerHealthBefore = CaptureConnectedProviderHealth(accountMgr);
                DateTimeOffset refreshStartedAt = DateTimeOffset.UtcNow;
                if (!silent)
                {
                    SetSyncProgressVisible(true);
                    ReplaceAgendaItems(Array.Empty<AgendaItem>());
                    ApplyAgendaSurface(
                        FlyoutAgendaSurfaceKind.Loading,
                        _loader.GetStringOrDefault("TextFullSyncTitle") ?? "Full Syncing...",
                        _loader.GetStringOrDefault("TextFullSyncSub") ?? "Fetching all your events and tasks",
                        hasAgendaItems: false);
                    AdjustWindowHeight();
                }

                DateTime min;
                DateTime max;
                if (fullSync)
                {
                    min = DateTime.Today.AddYears(-1);
                    max = DateTime.Today.AddYears(3);
                }
                else
                {
                    (min, max) = BackgroundRefreshSchedulePolicy.GetAgendaRefreshRange(DateTime.Today);
                }

                await _syncManager.GetAllDataAsync(min, max, forceRefresh);
                if (_isShuttingDown) return;

                LoadCacheForDate(_displayedMonth);
                RequestDotRefresh();
                ShowDataForDate(_selectedDay);

                var providerAttempts = CaptureProviderAttempts(
                    providerHealthBefore,
                    refreshStartedAt);
                var outcome = FlyoutAgendaSurfacePolicy.EvaluateSyncOutcome(
                    providerAttempts.Select(entry => entry.Attempt).ToList());
                if (!silent && outcome.CanRetry)
                {
                    ShowSyncFailureSurface(FormatUnavailableProviders(
                        providerAttempts
                            .Where(entry => entry.Attempt.Attempted && !entry.Attempt.Succeeded)
                            .Select(entry => entry.ProviderName)
                            .ToList()));
                    return;
                }

                if (outcome.Kind == FlyoutSyncOutcomeKind.Success)
                {
                    if (App.Current is App app) app.NotificationService?.CheckUpcomingEvents(forceCacheRefresh: true);
                    _lastSyncSucceededAt = DateTimeOffset.Now;
                }
            }
            catch (Exception ex)
            {
                if (!silent && !_isShuttingDown)
                {
                    LoadCacheForDate(_displayedMonth);
                    ShowDataForDate(_selectedDay);
                    ShowSyncFailureSurface(StatusMessageFormatter.Format(
                        UserSafeErrorMessage.FromException(
                            ex,
                            _loader.GetStringOrDefault("TextSyncFailed") ?? "Sync failed"),
                        _lastSyncSucceededAt,
                        includeLastSuccess: true,
                        _loader.GetStringOrDefault("TextLastSuccessFormat") ?? "Last success: {0:g}",
                        LocalizationHelper.AppCulture));
                }
            }
            finally
            {
                if (!silent && !_isShuttingDown)
                    SetSyncProgressVisible(false);
                _syncLock.Release();
            }
        }

        private Dictionary<string, ProviderHealthSnapshot> CaptureConnectedProviderHealth(
            AccountManager accountManager)
        {
            return accountManager.Accounts
                .Where(account => !string.IsNullOrWhiteSpace(account.ProviderName))
                .ToDictionary(
                    account => account.ProviderKey,
                    account => _syncManager.GetProviderHealth(account.ProviderName, account.AccountId),
                    StringComparer.OrdinalIgnoreCase);
        }

        private List<(string ProviderName, FlyoutProviderSyncAttempt Attempt)> CaptureProviderAttempts(
            IReadOnlyDictionary<string, ProviderHealthSnapshot> before,
            DateTimeOffset refreshStartedAt)
            => before
                .Select(entry =>
                {
                    var after = _syncManager.GetProviderHealth(
                        entry.Value.ProviderName,
                        entry.Value.AccountId);
                    bool attempted = HasProviderAttempted(entry.Value, after, refreshStartedAt);
                    bool succeeded = HasProviderSucceeded(entry.Value, after, attempted);
                    string accountTitle = _syncManager.AccountManager
                        .GetAccount(entry.Value.ProviderName, entry.Value.AccountId)
                        ?.DisplayTitle ?? entry.Value.ProviderName;
                    return (
                        ProviderName: accountTitle,
                        Attempt: new FlyoutProviderSyncAttempt(attempted, succeeded, after.HasCachedData));
                })
                .OrderBy(entry => entry.ProviderName, StringComparer.OrdinalIgnoreCase)
                .ToList();

        private static bool HasProviderAttempted(
            ProviderHealthSnapshot before,
            ProviderHealthSnapshot after,
            DateTimeOffset refreshStartedAt)
            => (before.Kind == ProviderHealthKind.Syncing && after.Kind != ProviderHealthKind.Syncing)
                || (after.LastAttemptUtc.HasValue
                    && (after.LastAttemptUtc > before.LastAttemptUtc
                        || after.LastAttemptUtc >= refreshStartedAt));

        private static bool HasProviderSucceeded(
            ProviderHealthSnapshot before,
            ProviderHealthSnapshot after,
            bool attempted)
            => attempted
                && after.Kind == ProviderHealthKind.Ready
                && after.LastSuccessUtc.HasValue
                && (!before.LastSuccessUtc.HasValue || after.LastSuccessUtc > before.LastSuccessUtc)
                && (!after.LastAttemptUtc.HasValue || after.LastSuccessUtc >= after.LastAttemptUtc);

        private string FormatUnavailableProviders(IReadOnlyList<string> providerNames)
        {
            string names = string.Join(", ", providerNames);
            string message = string.Format(
                LocalizationHelper.AppCulture,
                _loader.GetStringOrDefault("TextSyncUnavailableProvidersFormat")
                    ?? "Couldn't refresh {0}. Cached data may still be shown.",
                names);
            return StatusMessageFormatter.Format(
                message,
                _lastSyncSucceededAt,
                includeLastSuccess: true,
                _loader.GetStringOrDefault("TextLastSuccessFormat") ?? "Last success: {0:g}",
                LocalizationHelper.AppCulture);
        }

        private void ShowSyncFailureSurface(string subtitle)
        {
            ApplyAgendaSurface(
                FlyoutAgendaSurfaceKind.Error,
                _loader.GetStringOrDefault("TextSyncFailed") ?? "Sync failed",
                subtitle,
                AgendaItems.Count > 0);
            AdjustWindowHeight();
        }

        private void SetSyncProgressVisible(bool isVisible)
        {
            if (SyncProgress == null) return;
            SyncProgress.IsActive = isVisible;
            SyncProgress.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        public void ToggleFlyout()
        {
            if (_mainWindowDismissal != null) return;
            if ((DateTime.Now - _lastHideTime).TotalMilliseconds < 250) return;

            if (IsOpen)
            {
                _desiredOpen = false;
                HideFlyout(autoHide: false);
            }
            else if (_showPending)
            {
                if (_openRequestIssued) return;
                _desiredOpen = !_desiredOpen;
                if (_desiredOpen && _flyoutContentLoaded && !_openRequestIssued)
                {
                    PrepareOpeningActivationHandoff();
                    OpenPreparedFlyout();
                }
                else if (!_desiredOpen && !_openRequestIssued)
                {
                    _showPending = false;
                    ResetOpeningActivationHandoff();
                }
            }
            else
            {
                ShowFlyout();
            }
        }

        public Task DismissForMainWindowAsync()
        {
            if (_isShuttingDown) return Task.FromCanceled(new CancellationToken(true));
            _desiredOpen = false;
            _focusNewItemOnOpen = false;
            ResetOpeningActivationHandoff();
            if (_mainWindowDismissal != null) return _mainWindowDismissal.Task;

            if (!IsOpen && !_openRequestIssued)
            {
                _showPending = false;
                return Task.CompletedTask;
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _mainWindowDismissal = completion;
            // DesktopFlyouts ignores Hide during its opening animation. The
            // IsOpen callback below will finish that pending dismissal instead.
            if (IsOpen) HideFlyout(autoHide: false);
            return completion.Task;
        }

        private void ShowFlyout()
        {
            if (_isShuttingDown || IsOpen || _mainWindowDismissal != null) return;
            // A tray click can leave Explorer (or the previously active app) in
            // the foreground until the DesktopFlyouts activation handoff finishes.
            PrepareOpeningActivationHandoff();
            _desiredOpen = true;
            _showPending = true;
            if (!_flyoutContentLoaded) return;
            OpenPreparedFlyout();
        }

        private void OpenPreparedFlyout()
        {
            if (_isShuttingDown || IsOpen || !_showPending || !_desiredOpen) return;
            LoadCacheForDate(DateTime.Today);
            _selectedDay = DateTime.Today;
            ShowDataForDate(_selectedDay);
            UpdateClock();
            if (MainCalendar.SelectedDates.Count == 0 || MainCalendar.SelectedDates[0].Date != DateTime.Today)
            {
                MainCalendar.SelectedDates.Clear();
                MainCalendar.SelectedDates.Add(DateTime.Today);
                MainCalendar.SetDisplayDate(DateTime.Today);
            }
            UpdateSelectedDateHeader();
            ApplyConfiguredTheme(App.GetConfiguredTheme());
            _openRequestIssued = true;
            Show();
            StartActivationHandoffMonitor();
        }

        private void PrepareOpeningActivationHandoff()
        {
            ResetOpeningActivationHandoff();
            _openingForegroundAnchor = FlyoutForegroundProbe.CaptureForegroundAnchor();
        }

        private void StartActivationHandoffMonitor()
        {
            if (_activationHandoffTimer == null)
            {
                _activationHandoffTimer = new DispatcherTimer
                {
                    Interval = ActivationHandoffInterval
                };
                _activationHandoffTimer.Tick += ActivationHandoffTimer_Tick;
            }

            if (!_activationHandoffTimer.IsEnabled)
                _activationHandoffTimer.Start();
        }

        private void StopActivationHandoffMonitor()
        {
            _activationHandoffTimer?.Stop();
            _activationHandoffAttempts = 0;
        }

        private void ResetOpeningActivationHandoff()
        {
            StopActivationHandoffMonitor();
            _openingForegroundAnchor = IntPtr.Zero;
            _openingFlyoutWasForeground = false;
        }

        private void ActivationHandoffTimer_Tick(object? sender, object e)
        {
            if (_isShuttingDown || !_desiredOpen || (!IsOpen && !_openRequestIssued))
            {
                StopActivationHandoffMonitor();
                return;
            }

            bool focusStateKnown = FlyoutForegroundProbe.TryGetCurrentForegroundState(
                _openingForegroundAnchor,
                out bool isFlyoutForeground,
                out bool isOpeningForegroundStillActive);
            if (isFlyoutForeground)
            {
                _openingFlyoutWasForeground = true;
                StopActivationHandoffMonitor();
                return;
            }

            if (focusStateKnown &&
                (_openingFlyoutWasForeground || !isOpeningForegroundStillActive))
            {
                StopActivationHandoffMonitor();
                if (IsOpen && FlyoutDismissalPolicy.ShouldDismissAfterOpening(
                        _isPinned,
                        HideOnLostFocus,
                        focusStateKnown,
                        isFlyoutForeground,
                        isOpeningForegroundStillActive,
                        _openingFlyoutWasForeground))
                {
                    HideFlyout(autoHide: true);
                }
                return;
            }

            if (_activationHandoffAttempts >= MaxActivationHandoffAttempts)
            {
                StopActivationHandoffMonitor();
                return;
            }

            _activationHandoffAttempts++;
            if (FlyoutForegroundProbe.TryActivateCurrentProcessFlyoutHost())
            {
                NavigateFocus();
            }
        }

        private void OnIsOpenChanged(DependencyObject sender, DependencyProperty property)
        {
            if (IsOpen)
            {
                _showPending = false;
                _openRequestIssued = false;
                if (!_desiredOpen)
                {
                    StopActivationHandoffMonitor();
                    Hide();
                    return;
                }

                bool focusStateKnown = FlyoutForegroundProbe.TryGetCurrentForegroundState(
                    _openingForegroundAnchor,
                    out bool isFlyoutForeground,
                    out bool isOpeningForegroundStillActive);
                if (isFlyoutForeground)
                    _openingFlyoutWasForeground = true;
                if (FlyoutDismissalPolicy.ShouldDismissAfterOpening(
                        _isPinned,
                        HideOnLostFocus,
                        focusStateKnown,
                        isFlyoutForeground,
                        isOpeningForegroundStillActive,
                        _openingFlyoutWasForeground))
                {
                    StopActivationHandoffMonitor();
                    HideFlyout(autoHide: true);
                    return;
                }

                if (isFlyoutForeground ||
                    (focusStateKnown && !isOpeningForegroundStillActive))
                {
                    StopActivationHandoffMonitor();
                }
                else
                {
                    if (FlyoutForegroundProbe.TryActivateCurrentProcessFlyoutHost())
                        NavigateFocus();
                    StartActivationHandoffMonitor();
                }

                ApplyConfiguredTheme(App.GetConfiguredTheme());
                _clockTimer?.Start();
                _syncTimer?.Start();
                App.UpdateEfficiencyMode();
                NextRenderHelper.RunOnce(() =>
                    PerformanceDiagnostics.MarkOnce("flyout.visible", "flyout", "first_visible", source: "ui"));
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (!IsOpen) return;
                    if (_focusNewItemOnOpen)
                    {
                        _focusNewItemOnOpen = false;
                        TxtNewTitle.Focus(FocusState.Programmatic);
                    }
                    else
                    {
                        MainCalendar.Focus(FocusState.Programmatic);
                    }
                });
                if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (IsOpen) RequestDotRefresh();
                }))
                {
                    RequestDotRefresh();
                }
                QueueBackgroundRefresh();
                return;
            }

            _showPending = false;
            _openRequestIssued = false;
            _desiredOpen = false;
            ResetOpeningActivationHandoff();
            _clockTimer?.Stop();
            _syncTimer?.Stop();
            CancelBackgroundRefresh();
            CancelWeatherRefresh();
            _lastHideTime = DateTime.Now;
            App.UpdateEfficiencyMode();
            var dismissal = _mainWindowDismissal;
            _mainWindowDismissal = null;
            dismissal?.TrySetResult();
        }

        private void QueueBackgroundRefresh()
        {
            TimeSpan elapsedSinceLastStart = _lastBackgroundRefreshStartedTimestamp == 0
                ? TimeSpan.MaxValue
                : System.Diagnostics.Stopwatch.GetElapsedTime(_lastBackgroundRefreshStartedTimestamp);
            if (!FlyoutResidencyPolicy.ShouldQueueBackgroundRefresh(
                    Volatile.Read(ref _backgroundRefreshQueued),
                    elapsedSinceLastStart,
                    BackgroundRefreshCooldown))
                return;

            var cts = new CancellationTokenSource();
            Volatile.Write(ref _backgroundRefreshCts, cts);
            Volatile.Write(ref _backgroundRefreshQueued, true);
            _ = RunQueuedBackgroundRefreshAsync(cts);
        }

        private async Task RunQueuedBackgroundRefreshAsync(CancellationTokenSource cts)
        {
            try
            {
                await Task.Delay(1200, cts.Token);
                if (!IsBackgroundRefreshCurrent(cts))
                {
                    CompleteBackgroundRefresh(cts);
                    return;
                }

                bool enqueued = DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        if (!IsBackgroundRefreshCurrent(cts) ||
                            !IsOpen)
                            return;

                        _lastBackgroundRefreshStartedTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
                        await SyncAllDataAsync(true);
                        if (!IsBackgroundRefreshCurrent(cts))
                            return;

                        await RefreshWeatherAsync();
                    }
                    catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Queued flyout refresh failed: {ex.Message}");
                    }
                    finally
                    {
                        CompleteBackgroundRefresh(cts);
                    }
                });
                if (!enqueued)
                    CompleteBackgroundRefresh(cts);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                CompleteBackgroundRefresh(cts);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Queueing flyout refresh failed: {ex.Message}");
                CompleteBackgroundRefresh(cts);
            }
        }

        private void CancelBackgroundRefresh()
        {
            CancellationTokenSource? cts = Interlocked.Exchange(ref _backgroundRefreshCts, null);
            Volatile.Write(ref _backgroundRefreshQueued, false);
            try { cts?.Cancel(); } catch { }
        }

        private bool IsBackgroundRefreshCurrent(CancellationTokenSource cts)
            => !cts.IsCancellationRequested &&
               !Volatile.Read(ref _isShuttingDown) &&
               ReferenceEquals(Volatile.Read(ref _backgroundRefreshCts), cts);

        private void CompleteBackgroundRefresh(CancellationTokenSource cts)
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _backgroundRefreshCts, null, cts), cts))
                Volatile.Write(ref _backgroundRefreshQueued, false);
            cts.Dispose();
        }

        private void AdjustWindowHeight()
        {
            int logicalWidth = 360;
            double targetLogicalHeight = 0;
            double scaleFactor = Math.Max(1, RootGrid.XamlRoot?.RasterizationScale ?? 1);
            var workArea = DisplayArea.Primary.WorkArea;
            double availableLogicalHeight = workArea.Height / scaleFactor;
            double calendarHeight = ResponsiveLayoutPolicy.GetFlyoutCalendarHeight(availableLogicalHeight);
            if (CalendarHost != null) CalendarHost.Height = calendarHeight;
            if (AgendaContainer != null) AgendaContainer.MinHeight = availableLogicalHeight < 650 ? 0 : 80;

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

                // Measure the header area (Row 0) dynamically to account for weather strip visibility
                double headerHeight = 16; // top padding
                headerHeight += 50;       // clock area
                headerHeight += 12;       // clock bottom margin
                headerHeight += calendarHeight;
                headerHeight += 12;       // calendar bottom margin

                // Account for weather detail strip when visible
                if (WeatherDetailStrip != null && WeatherDetailStrip.Visibility == Visibility.Visible)
                {
                    WeatherDetailStrip.Measure(new Windows.Foundation.Size(logicalWidth, double.PositiveInfinity));
                    headerHeight += WeatherDetailStrip.DesiredSize.Height + 8; // strip height + margin
                }

                // Measure agenda list height
                int listHeight = 0;
                foreach (var item in AgendaItems)
                {
                    if (item.Subtitle != null && item.Subtitle.Contains(_loader.GetStringOrDefault("TextUpcoming") ?? "Upcoming")) listHeight += 65;
                    else listHeight += !string.IsNullOrEmpty(item.Location) ? 75 : 65;
                }

                // Bottom bar height (~56) + agenda header (~30) + agenda border padding (16) + margins
                double bottomAndAgendaChrome = 56 + 30 + 16 + 16;

                targetLogicalHeight = headerHeight + listHeight + bottomAndAgendaChrome;
                ContentRow.Height = GridLength.Auto;
                RootGrid.Measure(new Windows.Foundation.Size(logicalWidth, double.PositiveInfinity));
                targetLogicalHeight = Math.Max(targetLogicalHeight, RootGrid.DesiredSize.Height);
                ContentRow.Height = new GridLength(1, GridUnitType.Star);
            }

            double maxLogicalHeight = Math.Max(1, availableLogicalHeight - 24);
            if (targetLogicalHeight > maxLogicalHeight)
            {
                targetLogicalHeight = maxLogicalHeight;
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

            FlyoutHeight = new GridLength(Math.Ceiling(targetLogicalHeight));
        }

        private void HideFlyout(bool autoHide)
        {
            if (autoHide && _isPinned) return;
            _desiredOpen = false;
            Hide();
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

        private async void TaskCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is AgendaItem item && item.IsTask)
            {
                var newValue = cb.IsChecked == true;
                var oldValue = !newValue;
                try
                {
                    cb.IsEnabled = false;
                    item.IsCompleted = newValue;
                    if (App.Current is App app)
                    {
                        var key = $"{item.ProviderKey}|{item.CalendarId}|{item.Id}";
                        var result = await app.TaskMutations.ExecuteAsync(
                            key,
                            () => _syncManager.UpdateTaskStatusAsync(item.Provider, item.Id, newValue, item.CalendarId, item.AccountId),
                            ShowTaskMutationState);
                        if (result.Phase == TaskMutationPhase.Failed)
                        {
                            item.IsCompleted = oldValue;
                            _failedTaskMutationKey = key;
                            _retryTaskMutationSucceeded = async () =>
                            {
                                item.IsCompleted = newValue;
                                await _syncManager.SetCachedTaskCompletionAsync(item, newValue);
                                ReloadFilters();
                            };
                            RetryTaskMutationButton.Visibility = Visibility.Visible;
                            return;
                        }
                    }
                    _ = SyncAllDataAsync(silent: true);
                }
                catch
                {
                    item.IsCompleted = oldValue;
                    ShowTaskMutationState(new TaskMutationState("", TaskMutationPhase.Failed));
                }
                finally { cb.IsEnabled = true; }
            }
        }

        public void ShowNewItem()
        {
            if (_isShuttingDown || _mainWindowDismissal != null) return;
            SetupFlyoutProviderComboBox();
            TimePickerStart.Time = new TimeSpan(DateTime.Now.Hour, (DateTime.Now.Minute / 5) * 5, 0);
            TimePickerEnd.Time = TimePickerStart.Time.Add(TimeSpan.FromHours(1));
            AddPanel.Visibility = Visibility.Visible;
            if (AgendaContainer != null) AgendaContainer.Visibility = Visibility.Collapsed;
            AdjustWindowHeight();
            if (!IsOpen)
            {
                _focusNewItemOnOpen = true;
                _desiredOpen = true;
                ShowFlyout();
            }
            else
            {
                DispatcherQueue.TryEnqueue(() => TxtNewTitle.Focus(FocusState.Programmatic));
            }
        }

        public async Task<bool> RefreshNowAsync()
        {
            var previousSuccess = _lastSyncSucceededAt;
            await SyncAllDataAsync(silent: false, fullSync: true);
            return _lastSyncSucceededAt != previousSuccess;
        }

        private void ShowTaskMutationState(TaskMutationState state)
        {
            var status = TaskMutationStatusPolicy.Describe(state.Phase);
            TaskMutationStatusText.Text = _loader.GetStringOrDefault(status.ResourceKey) ?? status.FallbackText;
            TaskMutationStatusText.Foreground = status.IsError
                ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        }

        private async void RetryTaskMutationButton_Click(object sender, RoutedEventArgs e)
        {
            if (_failedTaskMutationKey == null || App.Current is not App app) return;
            RetryTaskMutationButton.IsEnabled = false;
            try
            {
                var result = await app.TaskMutations.RetryAsync(_failedTaskMutationKey, ShowTaskMutationState);
                if (result == null)
                {
                    _failedTaskMutationKey = null;
                    _retryTaskMutationSucceeded = null;
                    RetryTaskMutationButton.Visibility = Visibility.Collapsed;
                }
                else if (result.Phase == TaskMutationPhase.Succeeded && _retryTaskMutationSucceeded != null)
                {
                    await _retryTaskMutationSucceeded();
                    _failedTaskMutationKey = null;
                    _retryTaskMutationSucceeded = null;
                    RetryTaskMutationButton.Visibility = Visibility.Collapsed;
                    ShowTaskMutationState(result);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Flyout task retry refresh failed: {ex.Message}");
                ShowTaskMutationState(new TaskMutationState("", TaskMutationPhase.Failed));
            }
            finally
            {
                RetryTaskMutationButton.IsEnabled = true;
            }
        }

        private void RadioType_Changed(object sender, RoutedEventArgs e)
        {
            if (TxtLocation == null || TimePickerEnd == null || TimePickerStart == null) return;

            bool isEvent = RadioTypeEvent.IsChecked == true;

            TxtLocation.Visibility = isEvent ? Visibility.Visible : Visibility.Collapsed;
            TimePickerEnd.Visibility = isEvent ? Visibility.Visible : Visibility.Collapsed;
            if (CmbAddProvider != null)
                SetupFlyoutProviderComboBox();

            TimePickerStart.Header = isEvent ? (_loader.GetStringOrDefault("TextStartTime") ?? "Start time") : (_loader.GetStringOrDefault("TextDueTime") ?? "Due time");

            if (AddPanel != null && AddPanel.Visibility == Visibility.Visible)
            {
                RootGrid.UpdateLayout();
                AdjustWindowHeight();
            }
        }

        private void ChkAllDay_Checked(object sender, RoutedEventArgs e) { if (TimePanel != null) TimePanel.Visibility = Visibility.Collapsed; }
        private void ChkAllDay_Unchecked(object sender, RoutedEventArgs e) { if (TimePanel != null) TimePanel.Visibility = Visibility.Visible; }
        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await SyncAllDataAsync(silent: false, fullSync: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Manual refresh failed: {ex}");
            }
        }

        private async void AgendaSyncRetryButton_Click(object sender, RoutedEventArgs e)
        {
            if (!AgendaSyncRetryButton.IsEnabled) return;
            AgendaSyncRetryButton.IsEnabled = false;
            try
            {
                await SyncAllDataAsync(silent: false, forceRefresh: true, fullSync: true);
            }
            finally
            {
                AgendaSyncRetryButton.IsEnabled = true;
            }
        }

        private void AgendaAddAccountButton_Click(object sender, RoutedEventArgs e)
        {
            HideFlyout(autoHide: false);
            App.OpenMainWindowInternal(window => window.NavigateToAddAccount());
        }

        public void ReloadFilters()
        {
            _filterVersion++;
            EventCounts.Clear();
            if (_localCache?.DayItems != null)
            {
                foreach (var kvp in _localCache.DayItems)
                {
                    int count = kvp.Value.Count(i => IsItemVisible(i));
                    if (count > 0) EventCounts[kvp.Key] = count;
                }
            }

            RequestDotRefresh();

            ShowDataForDate(_selectedDay);
        }

        public async Task RefreshWeatherAsync(bool forceRefresh = false)
        {
            var cts = ReplaceWeatherRefreshCancellation();
            long generation = _weatherRefreshGeneration;
            var weatherService = (App.Current as App)?.WeatherService;
            if (weatherService == null || !weatherService.IsEnabled)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (generation != _weatherRefreshGeneration) return;
                    WeatherPanel.Visibility = Visibility.Collapsed;
                    WeatherDetailStrip.Visibility = Visibility.Collapsed;
                });
                if (ReferenceEquals(_weatherRefreshCts, cts))
                    _weatherRefreshCts = null;
                cts.Dispose();
                return;
            }

            WeatherInfo? info;
            try
            {
                info = await weatherService.GetWeatherAsync(forceRefresh, cts.Token);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                return;
            }
            finally
            {
                if (ReferenceEquals(_weatherRefreshCts, cts))
                    _weatherRefreshCts = null;
                cts.Dispose();
            }
            if (generation != _weatherRefreshGeneration) return;

            DispatcherQueue.TryEnqueue(() =>
            {
                if (generation != _weatherRefreshGeneration || !IsOpen) return;
                if (info != null)
                {
                    WeatherIcon.Text = info.Icon;
                    WeatherIcon.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(info.IconFont);
                    ApplyWeatherIconLayers(info.IconLayerUris);

                    TxtWeatherTemp.Text = info.Temperature;
                    TxtWeatherDesc.Text = info.Description;
                    WeatherPanel.Visibility = Visibility.Visible;

                    BuildWeatherDetailStrip(info, weatherService);
                    AdjustWindowHeight();
                }
                else
                {
                    WeatherPanel.Visibility = Visibility.Collapsed;
                    WeatherDetailStrip.Visibility = Visibility.Collapsed;
                    AdjustWindowHeight();
                }
            });
        }

        private CancellationTokenSource ReplaceWeatherRefreshCancellation()
        {
            _weatherRefreshCts?.Cancel();
            _weatherRefreshCts?.Dispose();
            _weatherRefreshCts = new CancellationTokenSource();
            _weatherRefreshGeneration++;
            return _weatherRefreshCts;
        }

        private void CancelWeatherRefresh()
        {
            _weatherRefreshGeneration++;
            _weatherRefreshCts?.Cancel();
            _weatherRefreshCts?.Dispose();
            _weatherRefreshCts = null;
        }

        private const int FlyoutWeatherIconDecodePixelWidth = 48;
        private const int MaxFlyoutWeatherIconCacheSize = 32;
        private readonly Dictionary<string, Microsoft.UI.Xaml.Media.Imaging.BitmapImage> _flyoutWeatherIconCache = new(StringComparer.Ordinal);

        // Decode weather icon-pack PNGs down to the ~48px display size and reuse them by URI,
        // mirroring the weather bar. Without this the flyout decoded full-size (often 256px)
        // bitmaps and rebuilt them on every open.
        private Microsoft.UI.Xaml.Media.Imaging.BitmapImage GetWeatherIconImage(string uri)
        {
            if (_flyoutWeatherIconCache.TryGetValue(uri, out var cached))
                return cached;

            if (_flyoutWeatherIconCache.Count >= MaxFlyoutWeatherIconCacheSize)
                _flyoutWeatherIconCache.Clear();

            var image = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(uri))
            {
                DecodePixelWidth = FlyoutWeatherIconDecodePixelWidth
            };
            _flyoutWeatherIconCache[uri] = image;
            return image;
        }

        private void ApplyWeatherIconLayers(string[] layers)
        {
            var images = new[] { WeatherIconImage, WeatherIconImage1, WeatherIconImage2, WeatherIconImage3 };
            bool useBitmap = layers != null && layers.Length > 0;
            WeatherIcon.Visibility = useBitmap ? Visibility.Collapsed : Visibility.Visible;
            for (int i = 0; i < images.Length; i++)
            {
                if (useBitmap && i < layers!.Length && !string.IsNullOrEmpty(layers[i]))
                {
                    try
                    {
                        images[i].Source = GetWeatherIconImage(layers[i]);
                        images[i].Visibility = Visibility.Visible;
                    }
                    catch
                    {
                        images[i].Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    images[i].Visibility = Visibility.Collapsed;
                }
            }
        }

        private void BuildWeatherDetailStrip(WeatherInfo info, WeatherService weatherService)
        {
            WeatherDetailStrip.Items.Clear();
            var fields = weatherService.GetEnabledFields();

            // Remove temperature and description since they're already shown in the weather button
            fields.Remove("temperature");
            fields.Remove("description");

            if (fields.Count == 0)
            {
                WeatherDetailStrip.Visibility = Visibility.Collapsed;
                return;
            }


            void AddChip(string glyph, string text)
            {
                if (string.IsNullOrEmpty(text)) return;
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 0, 12, 0) };
                panel.Children.Add(new FontIcon
                {
                    Glyph = glyph,
                    FontSize = 11,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    VerticalAlignment = VerticalAlignment.Center
                });
                panel.Children.Add(new TextBlock
                {
                    Text = text,
                    FontSize = 11,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    VerticalAlignment = VerticalAlignment.Center
                });
                WeatherDetailStrip.Items.Add(panel);
            }

            if (fields.Contains("feelslike") && !string.IsNullOrEmpty(info.FeelsLike))
                AddChip("\uE9CA", info.FeelsLike);
            if (fields.Contains("humidity") && !string.IsNullOrEmpty(info.Humidity))
                AddChip("\uE945", info.Humidity);
            if (fields.Contains("wind") && !string.IsNullOrEmpty(info.WindSpeed))
                AddChip("\uEBE7", info.WindSpeed);
            if (fields.Contains("uv") && !string.IsNullOrEmpty(info.UVIndex))
                AddChip("\uE706", $"UV {info.UVIndex}");
            if (fields.Contains("visibility") && !string.IsNullOrEmpty(info.Visibility))
                AddChip("\uE7B3", info.Visibility);
            if (fields.Contains("pressure") && !string.IsNullOrEmpty(info.Pressure))
                AddChip("\uEC49", info.Pressure);
            if (fields.Contains("airquality") && !string.IsNullOrEmpty(info.AirQuality))
                AddChip("\uE9CA", $"AQI {info.AirQuality}");
            if (fields.Contains("pollen") && !string.IsNullOrEmpty(info.Pollen))
                AddChip("\uE710", string.Format(
                    _loader.GetStringOrDefault("WeatherField_PollenValueFormat") ?? "Pollen {0}",
                    info.Pollen));
            if (fields.Contains("sun") && !string.IsNullOrEmpty(info.Sunrise))
                AddChip("\uE706", $"{info.Sunrise} / {info.Sunset}");
            if (fields.Contains("moon") && !string.IsNullOrEmpty(info.MoonPhase))
                AddChip("\uE708", info.MoonPhase);
            if (fields.Contains("precipitation"))
            {
                var hw = info.HourlyForecast?.FirstOrDefault();
                if (hw != null && !string.IsNullOrEmpty(hw.PrecipProbability))
                    AddChip("\uE790", hw.PrecipProbability);
            }

            WeatherDetailStrip.Visibility = WeatherDetailStrip.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            HideFlyout(autoHide: false);
            App.OpenMainWindowInternal(win => win.NavigateToSettings());
        }

        private void BtnWeather_Click(object sender, RoutedEventArgs e)
        {
            HideFlyout(autoHide: false);
            App.OpenMainWindowInternal(win => win.NavigateToWeather());
        }

        private void BtnPin_Click(object sender, RoutedEventArgs e)
        {
            _isPinned = !_isPinned;
            // E718 = Pin (pinned), E77A = Unpin (unpinned)
            PinIcon.Glyph = _isPinned ? "\uE718" : "\uE77A";
            HideOnLostFocus = !_isPinned;
        }

        public bool IsVisibleOrOpening => IsOpen || (_showPending && _desiredOpen);

        public void ApplyConfiguredTheme(ElementTheme theme)
        {
            var effectiveTheme = theme == ElementTheme.Default
                ? Application.Current.RequestedTheme == ApplicationTheme.Dark ? ElementTheme.Dark : ElementTheme.Light
                : theme;
            if (_appliedTheme == effectiveTheme) return;

            _appliedTheme = effectiveTheme;
            RootGrid.RequestedTheme = effectiveTheme;
            FlyoutIsland.RequestedTheme = effectiveTheme;
        }

        public void TrimMemoryCaches()
        {
            if (!FlyoutResidencyPolicy.ShouldTrimMemoryCaches(IsVisibleOrOpening)) return;
            _flyoutWeatherIconCache.Clear();
            _dotBrushCache.Clear();
            _lastDotRenderKey = null;
        }

        private void FlyoutIsland_ActualThemeChanged(FrameworkElement sender, object args)
        {
            _appliedTheme = null;
            ApplyConfiguredTheme(App.GetConfiguredTheme());
        }

        public void Shutdown()
        {
            if (_isShuttingDown) return;
            _isShuttingDown = true;
            _mainWindowDismissal?.TrySetCanceled();
            _mainWindowDismissal = null;
            _syncTimer?.Stop();
            _clockTimer?.Stop();
            _dotRefreshTimer?.Stop();
            if (_activationHandoffTimer != null)
            {
                _activationHandoffTimer.Stop();
                _activationHandoffTimer.Tick -= ActivationHandoffTimer_Tick;
                _activationHandoffTimer = null;
            }
            CancelBackgroundRefresh();
            CancelWeatherRefresh();
            if (_activeScrollViewer != null)
            {
                _activeScrollViewer.ViewChanging -= OnScrollViewerViewChanging;
                _activeScrollViewer.ViewChanged -= OnScrollViewerViewChanged;
                _activeScrollViewer = null;
            }
            RootGrid.Loaded -= RootGrid_Loaded;
            FlyoutIsland.ActualThemeChanged -= FlyoutIsland_ActualThemeChanged;
            if (_isOpenChangedToken != 0)
            {
                UnregisterPropertyChangedCallback(IsOpenProperty, _isOpenChangedToken);
                _isOpenChangedToken = 0;
            }
            Dispose();
        }

        private void AgendaListControl_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not AgendaItem item
                || !FlyoutAgendaSurfacePolicy.CanOpenEditor(item.IsEvent, item.IsTask))
                return;

            HideFlyout(autoHide: false);
            App.OpenMainWindowInternal(window => window.NavigateToCalendarAndEdit(item));
        }

        private void SetupFlyoutProviderComboBox()
        {
            CmbAddProvider.Items.Clear();
            var accountMgr = (App.Current as App)?.SyncManager?.AccountManager;
            if (accountMgr != null)
            {
                foreach (var acct in accountMgr.Accounts)
                {
                    var capabilities = SyncProviderCapabilityPolicy.ForProvider(acct.ProviderName);
                    if (RadioTypeTask?.IsChecked == true && !capabilities.SupportsTasks) continue;
                    if (RadioTypeEvent?.IsChecked == true && !capabilities.SupportsEvents) continue;
                    CmbAddProvider.Items.Add(new ComboBoxItem
                    {
                        Content = acct.DisplayTitle,
                        Tag = acct.AccountId,
                        DataContext = acct
                    });
                }
            }

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

                TimePickerStart.Time = new TimeSpan(DateTime.Now.Hour, (DateTime.Now.Minute / 5) * 5, 0);
                TimePickerEnd.Time = TimePickerStart.Time.Add(TimeSpan.FromHours(1));

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
            AddItemStatusText.Text = string.Empty;
            AddItemStatusText.Visibility = Visibility.Collapsed;
            AdjustWindowHeight();
        }

        private async void BtnSaveNewItem_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtNewTitle.Text)) return;

            string title = TxtNewTitle.Text;
            bool isEvent = RadioTypeEvent.IsChecked == true;
            bool isAllDay = ChkAllDay.IsChecked == true;
            string location = TxtLocation.Text;
            var selectedOption = CmbAddProvider.SelectedItem as ComboBoxItem;
            var selectedAccount = selectedOption?.DataContext as ConnectedAccountInfo;
            string providerName = selectedAccount?.ProviderName ?? "Google";
            string? accountId = selectedOption?.Tag?.ToString();

            TimeSpan startTime = TimePickerStart.Time;
            TimeSpan endTime = TimePickerEnd.Time;

            if (isEvent && endTime <= startTime) endTime = startTime.Add(TimeSpan.FromHours(1));

            DateTime targetDate = _selectedDay;

            try
            {
                SaveNewItemButton.IsEnabled = false;
                CancelAddButton.IsEnabled = false;
                AddItemProgress.IsActive = true;
                AddItemProgress.Visibility = Visibility.Visible;
                AddItemStatusText.Text = _loader.GetStringOrDefault("TextSyncing") ?? "Syncing...";
                AddItemStatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
                AddItemStatusText.Visibility = Visibility.Visible;
                AdjustWindowHeight();

                await _syncManager.CreateItemAsync(
                    title,
                    isEvent,
                    isAllDay,
                    targetDate,
                    startTime,
                    endTime,
                    location,
                    providerName,
                    accountId);
                TxtNewTitle.Text = string.Empty;
                TxtLocation.Text = string.Empty;
                AddItemStatusText.Text = string.Empty;
                AddItemStatusText.Visibility = Visibility.Collapsed;
                AddPanel.Visibility = Visibility.Collapsed;
                if (AgendaContainer != null) AgendaContainer.Visibility = Visibility.Visible;
                _ = SyncAllDataAsync(true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("============== Sync Error ==============");
                System.Diagnostics.Debug.WriteLine(ex.ToString());

                AddItemStatusText.Text = string.Format(
                    _loader.GetStringOrDefault("TextAddFailed2") ?? "Failed to add: {0}",
                    UserSafeErrorMessage.FromException(ex));
                AddItemStatusText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
                AddItemStatusText.Visibility = Visibility.Visible;
            }
            finally
            {
                AddItemProgress.IsActive = false;
                AddItemProgress.Visibility = Visibility.Collapsed;
                SaveNewItemButton.IsEnabled = true;
                CancelAddButton.IsEnabled = true;
                AdjustWindowHeight();
            }
        }
    }
}
