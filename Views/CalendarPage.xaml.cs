using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
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
    internal enum CalendarViewMode
    {
        Week,
        Month,
        Year
    }

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
        private const int MonthCellItemLimit = 3;
        private const double WeekTimelineHeaderHeight = 48;
        private const double WeekTimelineTimeAxisWidth = 56;

        public ObservableCollection<DayCellViewModel> DayCells { get; set; } = new();
        public ObservableCollection<AgendaItem> SelectedDayItems { get; set; } = new();
        public ObservableCollection<YearMonthViewModel> YearMonths { get; set; } = new();

        private DateTime _viewDate = DateTime.Today;
        private CalendarViewMode _viewMode = CalendarViewMode.Month;
        private SyncManager? _syncManager;
        private AppCache _localCache = new();
        private AppCache _upcomingCache = new();
        private AgendaItem? _itemBeingEdited;
        private ResourceLoader _loader;
        private bool _isAccountPaneCollapsed;
        private bool _isTimelinePaneCollapsed;
        private ResponsiveLayoutMode _layoutMode = ResponsiveLayoutMode.Wide;
        private DateTimeOffset? _lastCalendarSyncSucceededAt;
        private CalendarMonthRange _displayedRange;
        private readonly VersionedUiRefreshGate _cacheRefreshGate = new();
        private bool _isPageLoaded;

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
            this.Loaded += CalendarPage_Loaded;
            this.Unloaded += CalendarPage_Unloaded;
        }

        private void CalendarPage_Loaded(object sender, RoutedEventArgs e)
        {
            _isPageLoaded = true;
            if (_syncManager != null)
            {
                _syncManager.CachePublished -= SyncManager_CachePublished;
                _syncManager.CachePublished += SyncManager_CachePublished;
            }
            RefreshAccountList();
            LoadCalendar(_viewDate);
        }

        private void CalendarPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _isPageLoaded = false;
            if (_syncManager != null)
                _syncManager.CachePublished -= SyncManager_CachePublished;
            _syncCts?.Cancel();
            _syncCts?.Dispose();
            _syncCts = null;
        }

        private void SyncManager_CachePublished(object? sender, AgendaCachePublishedEventArgs e)
        {
            if (!_cacheRefreshGate.TryQueue(e.Version)) return;
            if (!DispatcherQueue.TryEnqueue(ApplyPublishedCacheUpdate))
                _cacheRefreshGate.CancelQueuedDispatch();
        }

        private void ApplyPublishedCacheUpdate()
        {
            if (!_cacheRefreshGate.TryBeginApply(out _) || !_isPageLoaded || CalendarGrid == null)
                return;

            LoadCache(_displayedRange);
            if (_viewMode == CalendarViewMode.Year)
            {
                PopulateYearMonths();
            }
            else if (_viewMode == CalendarViewMode.Week)
            {
                BuildWeekTimeline();
            }
            else
            {
                foreach (var cell in DayCells)
                {
                    if (_localCache.DayItems.TryGetValue(cell.Date.ToString("yyyy-MM-dd"), out var dayItems))
                        SetCellItems(cell, dayItems);
                    else
                        SetCellItems(cell, Array.Empty<AgendaItem>());
                }
            }
            UpdateSideBar();
        }

        private void SetWeekdayHeaders()
        {
            var headers = new[] { WeekHeader0, WeekHeader1, WeekHeader2, WeekHeader3, WeekHeader4, WeekHeader5, WeekHeader6 };
            var dateFormat = LocalizationHelper.AppCulture.DateTimeFormat;
            for (int i = 0; i < headers.Length; i++)
            {
                int dayIndex = ((int)dateFormat.FirstDayOfWeek + i) % 7;
                headers[i].Text = dateFormat.AbbreviatedDayNames[dayIndex];
            }
        }

        private void LoadCache(CalendarMonthRange range)
        {
            _localCache = _syncManager?.GetRangeCacheSnapshot(range.Start, range.EndExclusive) ?? new AppCache();
            var upcomingRange = GetUpcomingRange();
            _upcomingCache = _syncManager?.GetRangeCacheSnapshot(upcomingRange.Start, upcomingRange.EndExclusive) ?? new AppCache();
        }

        private void LoadCalendar(DateTime date)
        {
            if (BtnMonthYear == null || CalendarGrid == null) return;

            _displayedRange = GetDisplayedRange(date);
            LoadCache(_displayedRange);
            BtnMonthYear.Content = GetCalendarTitle(date, _displayedRange);
            CalendarGrid.Visibility = _viewMode == CalendarViewMode.Month ? Visibility.Visible : Visibility.Collapsed;
            WeekHeaderGrid.Visibility = _viewMode == CalendarViewMode.Month ? Visibility.Visible : Visibility.Collapsed;
            WeekTimelineView.Visibility = _viewMode == CalendarViewMode.Week ? Visibility.Visible : Visibility.Collapsed;
            YearGrid.Visibility = _viewMode == CalendarViewMode.Year ? Visibility.Visible : Visibility.Collapsed;
            DayCells.Clear();
            YearMonths.Clear();

            if (_viewMode == CalendarViewMode.Year)
            {
                PopulateYearMonths();
            }
            else if (_viewMode == CalendarViewMode.Week)
            {
                BuildWeekTimeline();
            }
            else
            {
                for (var current = _displayedRange.Start; current < _displayedRange.EndExclusive; current = current.AddDays(1))
                {
                    var cell = new DayCellViewModel { Date = current, IsCurrentMonth = IsPrimaryDate(current, date) };

                    string key = current.ToString("yyyy-MM-dd");
                    if (_localCache.DayItems.TryGetValue(key, out var cachedItems))
                        SetCellItems(cell, cachedItems.Where(IsItemVisible));
                    DayCells.Add(cell);
                }

                var targetCell = DayCells.FirstOrDefault(c => c.IsToday)
                    ?? DayCells.FirstOrDefault(c => IsPrimaryDate(c.Date, date));
                if (targetCell != null)
                    CalendarGrid.SelectedItem = targetCell;
            }
            UpdateSideBar();

            NextRenderHelper.RunOnce(() =>
                PerformanceDiagnostics.MarkOnce("calendar.cache.display", "calendar", "first_cached_display", source: "cache"));

            _ = SyncMonthDataAsync();
        }

        private CalendarMonthRange GetDisplayedRange(DateTime date)
        {
            var firstDayOfWeek = LocalizationHelper.AppCulture.DateTimeFormat.FirstDayOfWeek;
            return _viewMode switch
            {
                CalendarViewMode.Week => CalendarMonthRangePolicy.GetWeekRange(date, firstDayOfWeek),
                CalendarViewMode.Year => CalendarMonthRangePolicy.GetYearRange(date.Year),
                _ => CalendarMonthRangePolicy.GetRange(date, firstDayOfWeek)
            };
        }

        private string GetCalendarTitle(DateTime date, CalendarMonthRange range)
        {
            return _viewMode switch
            {
                CalendarViewMode.Week => $"{range.Start.ToString("M", LocalizationHelper.AppCulture)} - {range.EndExclusive.AddDays(-1).ToString("M", LocalizationHelper.AppCulture)}",
                CalendarViewMode.Year => string.Format(_loader.GetStringOrDefault("TextYearFormat") ?? "{0}", date.Year),
                _ => date.ToString("Y", LocalizationHelper.AppCulture)
            };
        }

        private bool IsPrimaryDate(DateTime date, DateTime viewDate)
        {
            return _viewMode switch
            {
                CalendarViewMode.Week => _displayedRange.Contains(date),
                CalendarViewMode.Year => date.Year == viewDate.Year,
                _ => date.Month == viewDate.Month && date.Year == viewDate.Year
            };
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
                if (_viewMode == CalendarViewMode.Month && DayCells.Count == 0) return;

                // A rapid mouse wheel/month navigation should settle before it starts a
                // provider request. Providers do not all expose cancellation yet.
                await Task.Delay(TimeSpan.FromMilliseconds(200), token);
                if (token.IsCancellationRequested) return;

                var range = _displayedRange;
                var upcomingRange = GetUpcomingRange();
                var allItemsTask = _syncManager.GetAllDataAsync(range.Start, range.EndExclusive, forceRefresh, token);
                var upcomingItemsTask = range.Start == upcomingRange.Start && range.EndExclusive == upcomingRange.EndExclusive
                    ? allItemsTask
                    : _syncManager.GetAllDataAsync(upcomingRange.Start, upcomingRange.EndExclusive, forceRefresh, token);

                await Task.WhenAll(allItemsTask, upcomingItemsTask);
                var allItems = await allItemsTask;
                var upcomingItems = await upcomingItemsTask;

                if (token.IsCancellationRequested) return;

                if (CalendarGrid == null) return;

                _localCache = BuildVisibleCache(allItems);
                if (_viewMode == CalendarViewMode.Year)
                {
                    PopulateYearMonths();
                }
                else if (_viewMode == CalendarViewMode.Week)
                {
                    BuildWeekTimeline();
                }
                else
                {
                    var itemsByDate = _localCache.DayItems;

                    foreach (var cell in DayCells)
                    {
                        if (itemsByDate.TryGetValue(cell.Date.ToString("yyyy-MM-dd"), out var dayItems))
                            SetCellItems(cell, dayItems);
                        else
                            SetCellItems(cell, Array.Empty<AgendaItem>());
                    }
                }

                _upcomingCache = BuildVisibleCache(upcomingItems);
                UpdateSideBar();

                _lastCalendarSyncSucceededAt = DateTimeOffset.Now;
                SetCalendarStatus(string.Format(_loader.GetStringOrDefault("TextLastSync") ?? "Last sync: {0}", _lastCalendarSyncSucceededAt.Value.LocalDateTime.ToString("g")));
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested) return;
                System.Diagnostics.Debug.WriteLine($"Sync error: {ex.Message}");
                SetCalendarStatus(StatusMessageFormatter.Format(
                    _loader.GetStringOrDefault("TextSyncFailed") ?? "Sync failed",
                    _lastCalendarSyncSucceededAt,
                    includeLastSuccess: true,
                    _loader.GetStringOrDefault("TextLastSuccessFormat") ?? "Last success: {0:g}",
                    LocalizationHelper.AppCulture), isError: true);
            }
            finally
            {
                if (SyncProgress != null && !token.IsCancellationRequested)
                    SyncProgress.IsActive = false;
            }
        }

        private void SetCellItems(DayCellViewModel cell, IEnumerable<AgendaItem> items)
        {
            var visibleItems = items
                .Where(IsItemVisible)
                .OrderBy(i => IsAllDaySubtitle(i.Subtitle) ? 0 : 1)
                .ThenBy(i => i.Subtitle)
                .ToList();

            cell.Items.Clear();
            foreach (var item in visibleItems.Take(MonthCellItemLimit))
            {
                PopulateItemColor(item);
                cell.Items.Add(item);
            }
            cell.HiddenItemCount = Math.Max(0, visibleItems.Count - MonthCellItemLimit);
        }

        private void BuildWeekTimeline()
        {
            if (WeekTimelineCanvas == null || WeekTimeAxisCanvas == null || WeekTimelineHeaderCanvas == null)
                return;

            WeekTimelineCanvas.Children.Clear();
            WeekTimeAxisCanvas.Children.Clear();
            WeekTimelineHeaderCanvas.Children.Clear();

            double timelineHeight = Math.Max(1, WeekTimelineView.ActualHeight - WeekTimelineHeaderHeight);
            double hourHeight = timelineHeight / 24.0;
            double availableWidth = Math.Max(1, WeekTimelineView.ActualWidth - WeekTimelineTimeAxisWidth);
            double dayWidth = availableWidth / 7;

            WeekTimeAxisCanvas.Height = timelineHeight;
            WeekTimeAxisCanvas.Width = WeekTimelineTimeAxisWidth;
            WeekTimelineCanvas.Width = availableWidth;
            WeekTimelineCanvas.Height = timelineHeight;
            WeekTimelineHeaderCanvas.Width = availableWidth;

            var dividerBrush = GetApplicationBrush("TaskFlyoutDividerBrush", Microsoft.UI.Colors.LightGray);
            var secondaryBrush = GetApplicationBrush("TextFillColorSecondaryBrush", Microsoft.UI.Colors.Gray);
            var primaryBrush = GetApplicationBrush("TextFillColorPrimaryBrush", Microsoft.UI.Colors.Black);

            for (int day = 0; day < 7; day++)
            {
                var date = _displayedRange.Start.AddDays(day);
                var header = new Border
                {
                    Width = dayWidth,
                    Height = 48,
                    BorderBrush = dividerBrush,
                    BorderThickness = new Thickness(day == 0 ? 0 : 1, 0, 0, 1),
                    Child = new StackPanel
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = date.ToString("ddd", LocalizationHelper.AppCulture),
                                FontSize = 12,
                                Foreground = secondaryBrush,
                                HorizontalAlignment = HorizontalAlignment.Center
                            },
                            new TextBlock
                            {
                                Text = date.ToString("M/d", LocalizationHelper.AppCulture),
                                FontSize = 15,
                                FontWeight = date.Date == DateTime.Today ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                                Foreground = primaryBrush,
                                HorizontalAlignment = HorizontalAlignment.Center
                            }
                        }
                    }
                };
                Canvas.SetLeft(header, day * dayWidth);
                WeekTimelineHeaderCanvas.Children.Add(header);

                var verticalLine = new Border
                {
                    Width = 1,
                    Height = timelineHeight,
                    Background = dividerBrush,
                    Opacity = day == 0 ? 0.9 : 0.55
                };
                Canvas.SetLeft(verticalLine, day * dayWidth);
                WeekTimelineCanvas.Children.Add(verticalLine);
            }

            var endLine = new Border { Width = 1, Height = timelineHeight, Background = dividerBrush, Opacity = 0.55 };
            Canvas.SetLeft(endLine, availableWidth - 1);
            WeekTimelineCanvas.Children.Add(endLine);

            for (int hour = 0; hour <= 24; hour++)
            {
                double top = hour * hourHeight;
                bool isMajorHour = CalendarWeekLayoutPolicy.ShouldShowTimeLabel(hour);
                var line = new Border
                {
                    Width = availableWidth,
                    Height = 1,
                    Background = dividerBrush,
                    Opacity = isMajorHour ? 0.72 : 0.22
                };
                Canvas.SetTop(line, top);
                WeekTimelineCanvas.Children.Add(line);

                if (isMajorHour)
                {
                    var label = new TextBlock
                    {
                        Text = $"{hour:00}:00",
                        FontSize = 10,
                        Foreground = secondaryBrush
                    };
                    double labelTop = hour == 24
                        ? Math.Max(0, timelineHeight - 14)
                        : Math.Max(0, top - 7);
                    Canvas.SetTop(label, labelTop);
                    Canvas.SetLeft(label, 2);
                    WeekTimeAxisCanvas.Children.Add(label);
                }
            }

            for (int day = 0; day < 7; day++)
            {
                var date = _displayedRange.Start.AddDays(day);
                string key = date.ToString("yyyy-MM-dd");
                if (!_localCache.DayItems.TryGetValue(key, out var items))
                    continue;

                foreach (var item in items.Where(IsItemVisible)
                             .OrderBy(i => GetWeekTimelineStart(i, date))
                             .ThenBy(i => i.Title))
                {
                    AddWeekTimelineEvent(item, date, day, dayWidth, hourHeight);
                }
            }
        }

        private void AddWeekTimelineEvent(AgendaItem item, DateTime date, int dayIndex, double dayWidth, double hourHeight)
        {
            if (!TryGetWeekTimelineBounds(item, date, hourHeight, out var top, out var height))
                return;

            PopulateItemColor(item);
            var background = !string.IsNullOrWhiteSpace(item.ColorHex) && item.ColorHex.StartsWith("#", StringComparison.Ordinal)
                ? new SolidColorBrush(Services.ColorHelper.ParseHex(item.ColorHex))
                : GetApplicationBrush("SystemAccentColor", Microsoft.UI.Colors.SteelBlue);
            var foreground = !string.IsNullOrWhiteSpace(item.ColorHex) && Services.ColorHelper.ShouldUseWhiteText(item.ColorHex)
                ? new SolidColorBrush(Microsoft.UI.Colors.White)
                : new SolidColorBrush(Microsoft.UI.Colors.Black);
            bool allDay = IsAllDaySubtitle(item.Subtitle);
            string allDayText = _loader.GetStringOrDefault("TextAllDay") ?? "All Day";
            string timeText = CalendarWeekLayoutPolicy.FormatEventTime(
                item.StartDateTime,
                item.EndDateTime,
                allDay,
                allDayText);
            bool useExpandedLayout = height >= 40;

            var title = new TextBlock
            {
                Text = useExpandedLayout
                    ? item.Title
                    : CalendarWeekLayoutPolicy.FormatCompactEventText(item.Title, timeText),
                FontSize = height >= 24 ? 12 : 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = foreground,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                MaxLines = 1
            };
            var subtitle = new TextBlock
            {
                Text = timeText,
                FontSize = 11,
                Foreground = foreground,
                Opacity = 0.82,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Visibility = useExpandedLayout && !string.IsNullOrWhiteSpace(timeText)
                    ? Visibility.Visible
                    : Visibility.Collapsed
            };
            var block = new Border
            {
                Width = Math.Max(16, dayWidth - 8),
                Height = height,
                CornerRadius = new CornerRadius(5),
                Padding = height >= 30 ? new Thickness(6, 4, 6, 4) : new Thickness(5, 2, 5, 2),
                Background = background,
                Opacity = item.IsCompleted ? 0.55 : 0.94,
                DataContext = item,
                Child = new StackPanel
                {
                    Spacing = 1,
                    Children = { title, subtitle }
                }
            };
            string accessibleText = CalendarWeekLayoutPolicy.FormatCompactEventText(item.Title, timeText);
            AutomationProperties.SetName(block, accessibleText);
            ToolTipService.SetToolTip(block, accessibleText);
            block.Tapped += WeekTimelineEvent_Tapped;
            Canvas.SetLeft(block, dayIndex * dayWidth + 4);
            Canvas.SetTop(block, top);
            Canvas.SetZIndex(block, 10);
            WeekTimelineCanvas.Children.Add(block);
        }

        private bool TryGetWeekTimelineBounds(AgendaItem item, DateTime date, double hourHeight, out double top, out double height)
        {
            DateTime dayStart = date.Date;
            DateTime dayEnd = dayStart.AddDays(1);
            bool allDay = IsAllDaySubtitle(item.Subtitle);
            DateTime start = item.StartDateTime ?? dayStart;
            DateTime end = item.EndDateTime ?? (allDay ? dayStart.AddMinutes(30) : start.AddHours(1));

            if (allDay || item.StartDateTime == null)
            {
                top = 2;
                height = Math.Max(18, hourHeight - 4);
                return true;
            }

            if (end <= start)
                end = start.AddMinutes(30);

            DateTime clampedStart = start < dayStart ? dayStart : start;
            DateTime clampedEnd = end > dayEnd ? dayEnd : end;
            if (clampedEnd <= dayStart || clampedStart >= dayEnd)
            {
                top = 0;
                height = 0;
                return false;
            }

            top = clampedStart.TimeOfDay.TotalHours * hourHeight + 2;
            height = Math.Max(18, (clampedEnd - clampedStart).TotalHours * hourHeight - 4);
            return true;
        }

        private DateTime GetWeekTimelineStart(AgendaItem item, DateTime date)
            => item.StartDateTime ?? date.Date;

        private void PopulateYearMonths()
        {
            YearMonths.Clear();
            var culture = LocalizationHelper.AppCulture;
            var firstDayOfWeek = culture.DateTimeFormat.FirstDayOfWeek;
            for (int month = 1; month <= 12; month++)
            {
                var monthStart = new DateTime(_viewDate.Year, month, 1);
                var monthEnd = monthStart.AddMonths(1);
                var itemsByDate = _localCache.DayItems
                    .Where(pair => DateKeyInRange(pair.Key, monthStart, monthEnd))
                    .ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value
                            .Where(IsItemVisible)
                            .OrderBy(i => IsAllDaySubtitle(i.Subtitle) ? 0 : 1)
                            .ThenBy(i => i.Subtitle)
                            .ToList(),
                        StringComparer.Ordinal);
                int itemCount = itemsByDate.Values.Sum(items => items.Count);

                var model = new YearMonthViewModel
                {
                    MonthStart = monthStart,
                    Title = monthStart.ToString("MMM", culture),
                    CountText = itemCount == 0
                        ? string.Empty
                        : string.Format(_loader.GetStringOrDefault("CalendarPage_YearItemCount") ?? "{0}", itemCount),
                    EmptyVisibility = itemCount == 0 ? Visibility.Visible : Visibility.Collapsed
                };

                int leadingBlankCount = LocalizationHelper.GetDayOffset(monthStart.DayOfWeek, firstDayOfWeek);
                for (int i = 0; i < leadingBlankCount; i++)
                    model.Days.Add(new YearDayViewModel());

                int daysInMonth = DateTime.DaysInMonth(monthStart.Year, monthStart.Month);
                for (int day = 1; day <= daysInMonth; day++)
                {
                    var date = new DateTime(monthStart.Year, monthStart.Month, day);
                    string key = date.ToString("yyyy-MM-dd");
                    string colorHex = "";
                    string toolTip = date.ToString("d", culture);
                    if (itemsByDate.TryGetValue(key, out var dayItems) && dayItems.Count > 0)
                    {
                        foreach (var agendaItem in dayItems)
                            PopulateItemColor(agendaItem);
                        colorHex = dayItems.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.ColorHex))?.ColorHex ?? "";
                        toolTip = $"{toolTip}\n{string.Join("\n", dayItems.Take(4).Select(item => item.Title))}";
                        if (dayItems.Count > 4)
                            toolTip += string.Format("\n+{0}", dayItems.Count - 4);
                    }

                    model.Days.Add(new YearDayViewModel
                    {
                        Date = date,
                        ColorHex = colorHex,
                        ToolTip = toolTip
                    });
                }

                while (model.Days.Count < 42)
                    model.Days.Add(new YearDayViewModel());
                YearMonths.Add(model);
            }
        }

        private static bool DateKeyInRange(string dateKey, DateTime startInclusive, DateTime endExclusive)
            => DateTime.TryParseExact(
                   dateKey,
                   "yyyy-MM-dd",
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.None,
                   out var date)
               && date >= startInclusive
               && date < endExclusive;

        private bool IsAllDaySubtitle(string subtitle)
            => subtitle == "全天"
               || subtitle == "All Day"
               || subtitle == (_loader.GetStringOrDefault("TextAllDay") ?? "All Day");

        private static Brush GetApplicationBrush(string key, Color fallback)
        {
            if (Application.Current.Resources.TryGetValue(key, out var resource))
            {
                if (resource is Brush brush)
                    return brush;
                if (resource is Color color)
                    return new SolidColorBrush(color);
            }
            return new SolidColorBrush(fallback);
        }

        private void Global_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(CalendarContent);
            if (point.Position.X >= 0 && point.Position.X <= CalendarContent.ActualWidth &&
                point.Position.Y >= 0 && point.Position.Y <= CalendarContent.ActualHeight)
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
                int rowCount = _viewMode == CalendarViewMode.Week ? 1 : 6;
                wrapGrid.ItemWidth = Math.Max(44, e.NewSize.Width / 7.0);
                wrapGrid.ItemHeight = Math.Max(
                    ResponsiveLayoutPolicy.GetCalendarCellMinimumHeight(e.NewSize.Height),
                    e.NewSize.Height / rowCount);
            }
        }

        private void WeekTimelineView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_viewMode == CalendarViewMode.Week)
                BuildWeekTimeline();
        }

        private async void WeekTimelineEvent_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: AgendaItem item }) return;
            e.Handled = true;
            await ShowEditDialogAsync(item);
        }

        private void BtnPrevMonth_Click(object sender, RoutedEventArgs e) => LoadCalendar(_viewDate = ShiftViewDate(-1));
        private void BtnNextMonth_Click(object sender, RoutedEventArgs e) => LoadCalendar(_viewDate = ShiftViewDate(1));
        private void BtnToday_Click(object sender, RoutedEventArgs e) => LoadCalendar(_viewDate = DateTime.Today);
        private void BtnMonthYear_Click(object sender, RoutedEventArgs e) => SwitchCalendarViewMode(CalendarViewMode.Year, updateSelector: true);

        private DateTime ShiftViewDate(int direction)
        {
            return _viewMode switch
            {
                CalendarViewMode.Week => _viewDate.AddDays(7 * direction),
                CalendarViewMode.Year => _viewDate.AddYears(direction),
                _ => _viewDate.AddMonths(direction)
            };
        }

        private void CalendarViewModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CalendarViewModeBox.SelectedItem is not ComboBoxItem item || item.Tag is not string tag)
                return;

            SwitchCalendarViewMode(tag switch
            {
                "Week" => CalendarViewMode.Week,
                "Year" => CalendarViewMode.Year,
                _ => CalendarViewMode.Month
            });
        }

        private void SwitchCalendarViewMode(CalendarViewMode nextMode, bool updateSelector = false)
        {
            if (_viewMode == nextMode) return;
            _viewMode = nextMode;
            if (updateSelector && CalendarViewModeBox != null)
                CalendarViewModeBox.SelectedIndex = nextMode switch
                {
                    CalendarViewMode.Week => 0,
                    CalendarViewMode.Year => 2,
                    _ => 1
                };
            if (CalendarContent != null)
                LoadCalendar(_viewDate);
        }

        private void YearGrid_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not YearMonthViewModel month) return;
            _viewDate = month.MonthStart;
            SwitchCalendarViewMode(CalendarViewMode.Month, updateSelector: true);
        }

        private void ToggleAccountPane_Click(object sender, RoutedEventArgs e)
        {
            _isAccountPaneCollapsed = !_isAccountPaneCollapsed;
            if (_layoutMode != ResponsiveLayoutMode.Wide && !_isAccountPaneCollapsed)
                _isTimelinePaneCollapsed = true;
            ApplyResponsiveLayout();
        }

        private void LayoutRoot_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var mode = ResponsiveLayoutPolicy.GetCalendarMode(e.NewSize.Width);
            if (_layoutMode != mode)
            {
                _layoutMode = mode;
                if (mode == ResponsiveLayoutMode.Wide)
                {
                    _isAccountPaneCollapsed = false;
                    _isTimelinePaneCollapsed = false;
                }
                else
                    _isAccountPaneCollapsed = true;
                if (mode == ResponsiveLayoutMode.Narrow)
                    _isTimelinePaneCollapsed = true;
            }
            ApplyResponsiveLayout();
        }

        private void ApplyResponsiveLayout()
        {
            bool agendaOnly = ResponsiveLayoutPolicy.ShouldShowCalendarAgendaOnly(
                LayoutRoot.ActualWidth,
                LayoutRoot.ActualHeight);
            bool showAccounts = !_isAccountPaneCollapsed;
            bool showCalendar = !agendaOnly;
            bool showTimeline = agendaOnly ? !showAccounts : !_isTimelinePaneCollapsed;

            AccountColumn.MinWidth = showAccounts && _layoutMode == ResponsiveLayoutMode.Wide ? 240 : 0;
            AccountColumn.Width = showAccounts
                ? (agendaOnly ? new GridLength(1, GridUnitType.Star) : new GridLength(2, GridUnitType.Star))
                : new GridLength(0);
            CalendarColumn.MinWidth = 0;
            CalendarColumn.Width = showCalendar ? new GridLength(5, GridUnitType.Star) : new GridLength(0);
            TimelineColumn.MinWidth = showTimeline && !agendaOnly ? 280 : 0;
            TimelineColumn.Width = showTimeline
                ? new GridLength(agendaOnly ? 1 : 3, GridUnitType.Star)
                : new GridLength(0);
            AccountPane.Visibility = showAccounts ? Visibility.Visible : Visibility.Collapsed;
            CalendarContent.Visibility = showCalendar ? Visibility.Visible : Visibility.Collapsed;
            TimelinePane.Visibility = showTimeline ? Visibility.Visible : Visibility.Collapsed;
            double padding = ResponsiveLayoutPolicy.GetPagePadding(
                LayoutRoot.ActualWidth,
                LayoutRoot.ActualHeight);
            CalendarContent.Padding = new Thickness(padding);
            AccountPane.Margin = agendaOnly
                ? new Thickness(padding)
                : new Thickness(24, 28, 12, 28);
            TimelinePane.Margin = agendaOnly
                ? new Thickness(padding)
                : new Thickness(12, 28, 24, 28);
            ToggleAccountPaneIcon.Glyph = showAccounts ? "\uE76B" : "\uE76C";
            ToggleTimelinePaneIcon.Glyph = showTimeline ? "\uE76C" : "\uE76B";
            ToggleTimelinePaneButton.Visibility = agendaOnly
                ? Visibility.Collapsed : Visibility.Visible;
            TimelineAccountsButton.Visibility = agendaOnly
                ? Visibility.Visible : Visibility.Collapsed;
            CollapseTimelinePaneButton.Visibility = agendaOnly
                ? Visibility.Collapsed : Visibility.Visible;
            Grid.SetRow(CalendarStatusPanel, 1);
            Grid.SetColumn(CalendarStatusPanel, 0);
            Grid.SetColumnSpan(CalendarStatusPanel, 2);
            Grid.SetRow(RetryTaskMutationButton, 1);
            Grid.SetColumn(RetryTaskMutationButton, 2);
        }

        private void ToggleTimelinePane_Click(object sender, RoutedEventArgs e)
        {
            _isTimelinePaneCollapsed = !_isTimelinePaneCollapsed;
            if (_layoutMode == ResponsiveLayoutMode.Medium && !_isTimelinePaneCollapsed)
                _isAccountPaneCollapsed = true;
            ApplyResponsiveLayout();
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
                if (mgr.Accounts.Count > 0)
                    LoadCalendar(_viewDate);
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
            WeekTimelineView.Opacity = hasAccounts ? 1.0 : 0.25;
            WeekTimelineView.IsHitTestVisible = hasAccounts;
            YearGrid.Opacity = hasAccounts ? 1.0 : 0.25;
            YearGrid.IsHitTestVisible = hasAccounts;
        }

        private void BtnAddAccount_Click(object sender, RoutedEventArgs e)
        {
            App.MyMainWindow?.NavigateToAddAccount();
        }

        private async void BtnRemoveAccount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string providerName) return;

            bool hasSharedAuthorization = ProviderAuthorizationLifecycle.HasSharedAuthorization(providerName);

            var dialog = new ContentDialog
            {
                Title = _loader.GetStringOrDefault("TextRemoveAccountTitle") ?? "Remove Account",
                Content = hasSharedAuthorization
                    ? string.Format(_loader.GetStringOrDefault("TextProviderRemovalContent") ?? "Remove {0} from Calendar and Tasks only, or disconnect it completely from Calendar, Tasks, and Mail?", providerName)
                    : string.Format(_loader.GetStringOrDefault("TextRemoveAccountContent") ?? "Are you sure you want to remove the {0} account?", providerName),
                PrimaryButtonText = hasSharedAuthorization
                    ? _loader.GetStringOrDefault("TextRemoveAgendaOnly") ?? "Remove Calendar/Tasks only"
                    : _loader.GetStringOrDefault("TextRemoveAccount") ?? "Remove Account",
                SecondaryButtonText = hasSharedAuthorization
                    ? _loader.GetStringOrDefault("TextDisconnectProvider") ?? "Disconnect completely"
                    : "",
                CloseButtonText = _loader.GetStringOrDefault("CalendarDialog.CloseButtonText") ?? "Cancel",
                XamlRoot = XamlRoot,
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result is not (ContentDialogResult.Primary or ContentDialogResult.Secondary)) return;

            try
            {
                if ((!hasSharedAuthorization && result == ContentDialogResult.Primary) && App.Current is App app)
                    await app.DisconnectProviderCompletelyAsync(providerName);
                else if (result == ContentDialogResult.Secondary && App.Current is App app2)
                    await app2.DisconnectProviderCompletelyAsync(providerName);
                else if (_syncManager != null)
                    await _syncManager.RemoveAgendaAccountAsync(providerName);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Account removal failed: {ex.Message}");
                var errorDialog = new ContentDialog
                {
                    Title = _loader.GetStringOrDefault("TextRemoveAccountFailedTitle") ?? "Account Not Removed",
                    Content = _loader.GetStringOrDefault("TextDisconnectProviderFailed") ?? "The account change could not be completed. Existing account data was preserved so you can try again.",
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
            if (e.ClickedItem is DayCellViewModel) UpdateSideBar();
        }

        private void UpdateSideBar()
        {
            TxtSideBarDate.Text = _loader.GetStringOrDefault("TextUpcomingMonth") ?? "Next month";
            var nextItems = new List<AgendaItem>();

            var upcomingRange = GetUpcomingRange();
            var upcomingDates = _upcomingCache.DayItems.Keys
                .Where(upcomingRange.ContainsDateKey)
                .OrderBy(k => k, StringComparer.Ordinal);

            bool hasItems = false;
            foreach (var dateKey in upcomingDates)
            {
                if (!_upcomingCache.DayItems.TryGetValue(dateKey, out var dateItems)
                    || !DateTime.TryParseExact(dateKey, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var itemDate))
                    continue;

                var sortedItems = dateItems
                    .Where(IsItemVisible)
                    .OrderBy(i => IsAllDaySubtitle(i.Subtitle) ? 0 : 1)
                    .ThenBy(i => i.Subtitle);
                foreach (var item in sortedItems)
                {
                    bool isAllDay = IsAllDaySubtitle(item.Subtitle);
                    string allDayText = _loader.GetStringOrDefault("TextAllDay") ?? "All Day";
                    string timeText = item.IsEvent
                        ? CalendarEventTimePolicy.FormatTimeRange(
                            item.StartDateTime,
                            item.EndDateTime,
                            isAllDay,
                            allDayText,
                            item.Subtitle)
                        : item.Subtitle;
                    var displayItem = new AgendaItem
                    {
                        Id = item.Id,
                        Title = item.Title,
                        Subtitle = $"{itemDate.ToString("M", LocalizationHelper.AppCulture)}\n{timeText}",
                        Location = item.Location,
                        Description = item.Description,
                        IsEvent = item.IsEvent,
                        IsTask = item.IsTask,
                        IsCompleted = item.IsCompleted,
                        Provider = item.Provider,
                        AccountId = item.AccountId,
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
                    nextItems.Add(displayItem);
                    hasItems = true;
                }
            }

            if (!hasItems)
            {
                nextItems.Add(new AgendaItem
                {
                    Title = _loader.GetStringOrDefault("TextNoAgendaTitle") ?? "No upcoming events",
                    Subtitle = "-",
                    IsEvent = true
                });
            }

            if (SelectedDayItems.Count == nextItems.Count
                && SelectedDayItems.Zip(nextItems, SideBarItemsEqual).All(equal => equal))
                return;

            SelectedDayItems.Clear();
            foreach (var item in nextItems)
                SelectedDayItems.Add(item);
        }

        private static CalendarMonthRange GetUpcomingRange()
        {
            var start = DateTime.Today.Date;
            var endExclusive = start.AddMonths(1).AddDays(1);
            return new CalendarMonthRange(start, endExclusive, start, endExclusive);
        }

        private AppCache BuildVisibleCache(IEnumerable<AgendaItem> items)
        {
            var itemsByDate = items
                .Where(IsItemVisible)
                .GroupBy(it => it.DateKey)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            return new AppCache
            {
                DayItems = itemsByDate.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.ToList(),
                    StringComparer.Ordinal),
                MarkedDates = itemsByDate.Keys.ToHashSet(StringComparer.Ordinal)
            };
        }

        private static bool SideBarItemsEqual(AgendaItem left, AgendaItem right)
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
                    if (_syncManager != null && App.Current is App app)
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
                                _ = SyncMonthDataAsync(forceRefresh: true);
                            };
                            RetryTaskMutationButton.Visibility = Visibility.Visible;
                            return;
                        }
                    }
                    _ = SyncMonthDataAsync(forceRefresh: true);
                }
                catch
                {
                    item.IsCompleted = oldValue;
                    ShowTaskMutationState(new TaskMutationState("", TaskMutationPhase.Failed));
                }
                finally { cb.IsEnabled = true; }
            }
        }

        private string? _failedTaskMutationKey;
        private Func<Task>? _retryTaskMutationSucceeded;

        private void ShowTaskMutationState(TaskMutationState state)
        {
            var status = TaskMutationStatusPolicy.Describe(state.Phase);
            SetCalendarStatus(_loader.GetStringOrDefault(status.ResourceKey) ?? status.FallbackText, status.IsError);
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
                System.Diagnostics.Debug.WriteLine($"Calendar task retry refresh failed: {ex.Message}");
                ShowTaskMutationState(new TaskMutationState("", TaskMutationPhase.Failed));
            }
            finally
            {
                RetryTaskMutationButton.IsEnabled = true;
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
                LoadCalendar(_viewDate);
                SetCalendarStatus(string.Format(_loader.GetStringOrDefault("TextLastSync") ?? "Last sync: {0}", _lastCalendarSyncSucceededAt.Value.LocalDateTime.ToString("g")));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Force sync error: {ex.Message}");
                SetCalendarStatus(StatusMessageFormatter.Format(
                    _loader.GetStringOrDefault("TextSyncFailed") ?? "Sync failed",
                    _lastCalendarSyncSucceededAt,
                    includeLastSuccess: true,
                    _loader.GetStringOrDefault("TextLastSuccessFormat") ?? "Last success: {0:g}",
                    LocalizationHelper.AppCulture), isError: true);
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

            if (_itemBeingEdited == null && EditCmbProvider != null)
            {
                string? selectedProvider = (EditCmbProvider.SelectedItem as ComboBoxItem)?.Tag?.ToString();
                SetupEditProviderComboBox();
                var previous = EditCmbProvider.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), selectedProvider, StringComparison.Ordinal));
                if (previous != null) EditCmbProvider.SelectedItem = previous;
            }
        }

        private void SetupEditProviderComboBox(string? forceSelectProvider = null)
        {
            EditCmbProvider.Items.Clear();
            var accountMgr = (App.Current as App)?.SyncManager?.AccountManager;

            if (accountMgr != null)
            {
                foreach (var acct in accountMgr.Accounts)
                {
                    var capabilities = SyncProviderCapabilityPolicy.ForProvider(acct.ProviderName);
                    if (EditRadioTask?.IsChecked == true && !capabilities.SupportsTasks) continue;
                    if (EditRadioEvent?.IsChecked == true && !capabilities.SupportsEvents) continue;
                    EditCmbProvider.Items.Add(new ComboBoxItem { Content = acct.ProviderName, Tag = acct.ProviderName });
                }
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
            => OpenNewDialog(isTask: false);

        public void OpenNewDialog(bool isTask)
        {
            _itemBeingEdited = null;
            EditDialog.Title = _loader.GetStringOrDefault("TextNewItem") ?? "New Event / Task";
            EditDialog.SecondaryButtonText = "";
            EditCmbProvider.IsEnabled = true;
            EditRadioEvent.IsEnabled = true;
            bool hasTaskProvider = (App.Current as App)?.SyncManager.AccountManager.Accounts
                .Any(account => SyncProviderCapabilityPolicy.ForProvider(account.ProviderName).SupportsTasks) == true;
            EditRadioTask.IsEnabled = hasTaskProvider;
            EditRadioTask.IsChecked = isTask && hasTaskProvider;
            EditRadioEvent.IsChecked = !isTask || !hasTaskProvider;
            SetupEditProviderComboBox();
            EditRadioType_Changed(null, null);
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
            PrepareEditDialogSize();
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

            bool isAllDay = IsAllDaySubtitle(item.Subtitle);
            var editorTime = CalendarEventTimePolicy.CreateEditorState(
                item.StartDateTime,
                item.EndDateTime,
                isAllDay,
                item.Subtitle);
            EditChkAllDay.IsChecked = editorTime.IsAllDay;
            EditStartTimePicker.SelectedTime = editorTime.StartTime;
            EditEndTimePicker.SelectedTime = editorTime.EndTime;

            EditRadioType_Changed(null, null);
        }

        private async void SideBarAgendaList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not AgendaItem item) return;
            if (item.Title != null && item.Title.Contains(_loader.GetStringOrDefault("TextNoAgendaTitle") ?? "No upcoming events")) return;

            await ShowEditDialogAsync(item);
        }

        private async Task ShowEditDialogAsync(AgendaItem item)
        {
            PrepareDialogForEdit(item);
            EditDialog.XamlRoot = this.XamlRoot;
            PrepareEditDialogSize();
            await EditDialog.ShowAsync();
        }

        public void OpenEditDialogFromExternal(AgendaItem item)
        {
            Action showDialog = async () =>
            {
                PrepareDialogForEdit(item);
                EditDialog.XamlRoot = this.XamlRoot;
                PrepareEditDialogSize();
                try { await EditDialog.ShowAsync(); } catch { }
            };
            if (this.XamlRoot == null) this.Loaded += (s, e) => showDialog(); else showDialog();
        }

        private void PrepareEditDialogSize()
        {
            if (XamlRoot == null) return;
            EditDialogScrollViewer.MaxHeight = Math.Clamp(XamlRoot.Size.Height - 180, 120, 560);
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
                         EditDatePicker.Date.DateTime, newStartTime, newEndTime, _itemBeingEdited.CalendarId, _itemBeingEdited.AccountId);

                    if (_localCache.DayItems.TryGetValue(_itemBeingEdited.DateKey, out var oldList))
                    {
                        var original = oldList.FirstOrDefault(x => x.Id == _itemBeingEdited.Id);
                        if (original != null)
                        {
                            var oldDateKey = original.DateKey;
                            var updatedTime = SyncEventTimePolicy.Create(
                                EditDatePicker.Date.DateTime,
                                newStartTime,
                                newEndTime);
                            oldList.Remove(original);
                            original.Title = EditTxtTitle.Text;
                            original.Location = EditTxtLocation.Text;
                            original.Description = EditTxtDescription.Text;
                            original.DateKey = newDateKey;
                            if (original.IsEvent)
                            {
                                original.Subtitle = newSubtitleText;
                                original.StartDateTime = updatedTime.Start;
                                original.EndDateTime = updatedTime.End;
                            }
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
                        itemToDelete.RecurringEventId,
                        itemToDelete.CalendarId,
                        itemToDelete.AccountId);
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
