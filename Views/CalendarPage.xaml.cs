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
using Microsoft.Windows.ApplicationModel.Resources;
using System.Globalization;

namespace Task_Flyout.Views
{
    // 莫奈背景色转换器
    public class ProviderToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string provider = (value as string)?.ToLower() ?? "";

            if (provider.Contains("google"))
                return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 89, 118, 186));

            if (provider.Contains("microsoft") || provider.Contains("todo"))
                return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 230, 175, 115));

            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 150, 150));
        }
        public object ConvertBack(object v, Type t, object p, string l) => throw new NotImplementedException();
    }

    public class ProviderToTextBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string provider = (value as string)?.ToLower() ?? "";

            // Google 深蓝色底 -> 返回白色文字
            if (provider.Contains("google"))
                return new SolidColorBrush(Microsoft.UI.Colors.White);

            // Microsoft 浅黄色底 -> 返回黑色文字
            if (provider.Contains("microsoft") || provider.Contains("todo"))
                return new SolidColorBrush(Microsoft.UI.Colors.Black);

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
                // 给“今天”一个轻微的底色强调，如取系统淡强调色
                if (Application.Current.Resources.TryGetValue("SystemAccentColorLight3", out var res))
                    return new SolidColorBrush((Windows.UI.Color)res);
            }
            // 正常格子返回透明，消除空色块
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

        private readonly string CacheFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskFlyout", "local_cache_winui3.json");

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
                LoadCalendar(_viewDate);
            };
        }

        private void LoadCache()
        {
            try { if (File.Exists(CacheFilePath)) _localCache = JsonSerializer.Deserialize<AppCache>(File.ReadAllText(CacheFilePath)) ?? new(); }
            catch { _localCache = new(); }
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
                        cell.Items.Add(item);
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

        private async Task SyncMonthDataAsync()
        {
            if (_syncManager == null || SyncProgress == null) return;

            _syncCts?.Cancel();
            _syncCts = new System.Threading.CancellationTokenSource();
            var token = _syncCts.Token;

            SyncProgress.IsActive = true;

            try
            {
                if (DayCells.Count == 0) return;

                var start = DayCells.First().Date;
                var end = DayCells.Last().Date.AddDays(1);

                var allItems = await _syncManager.GetAllDataAsync(start, end);

                if (token.IsCancellationRequested) return;

                if (CalendarGrid == null) return;

                for (var d = start; d < end; d = d.AddDays(1))
                {
                    _localCache.DayItems.Remove(d.ToString("yyyy-MM-dd"));
                }

                foreach (var item in allItems)
                {
                    if (!_localCache.DayItems.ContainsKey(item.DateKey))
                        _localCache.DayItems[item.DateKey] = new List<AgendaItem>();

                    var list = _localCache.DayItems[item.DateKey];
                    list.RemoveAll(x => x.Id == item.Id);
                    list.Add(item);
                }

                try { File.WriteAllText(CacheFilePath, JsonSerializer.Serialize(_localCache)); } catch { }

                foreach (var cell in DayCells)
                {
                    cell.Items.Clear();
                    var dayItems = allItems.Where(it => it.DateKey == cell.Date.ToString("yyyy-MM-dd") && IsItemVisible(it));
                    foreach (var item in dayItems) cell.Items.Add(item);
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
                wrapGrid.ItemHeight = Math.Max(80, e.NewSize.Height / 6.0); // 这里也可以随动态行数进一步修改，但目前 /6 是安全的兜底
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
                    .Where(IsItemVisible) // 确保这里同步应用了开关和来源过滤
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
                        DateKey = item.DateKey
                    };
                    SelectedDayItems.Add(displayItem);
                    hasItems = true;
                }
                if (!hasItems)
                {
                    SelectedDayItems.Add(new AgendaItem
                    {
                        Title = _loader.GetString("TextNoAgendaTitle"),
                        Subtitle = "-",
                        IsEvent = true
                    });
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

        private bool IsItemVisible(AgendaItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.Provider)) return true; // 安全防空

            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            bool showGE = localSettings.Values["ShowGoogleEvents"] as bool? ?? true;
            bool showGT = localSettings.Values["ShowGoogleTasks"] as bool? ?? true;
            bool showME = localSettings.Values["ShowMSEvents"] as bool? ?? true;
            bool showMT = localSettings.Values["ShowMSTasks"] as bool? ?? true;

            string provider = item.Provider.ToLower();

            // 模糊匹配包含 google 字眼
            if (provider.Contains("google"))
            {
                if (item.IsEvent && !showGE) return false;
                if (item.IsTask && !showGT) return false;
            }
            // 模糊匹配包含 microsoft 或 todo 字眼 (适配 Microsoft To Do)
            else if (provider.Contains("microsoft") || provider.Contains("todo"))
            {
                if (item.IsEvent && !showME) return false;
                if (item.IsTask && !showMT) return false;
            }
            return true;
        }

        public void ReloadFilters()
        {
            LoadCalendar(_viewDate);
        }

        public void ForceSync()
        {
            _ = SyncMonthDataAsync();
        }

        private void EditRadioType_Changed(object sender, RoutedEventArgs e)
        {
            if (EditTxtLocation == null || EditEndTimePicker == null) return;

            bool isEvent = EditRadioEvent.IsChecked == true;
            EditTxtLocation.Visibility = isEvent ? Visibility.Visible : Visibility.Collapsed;
            EditEndTimePicker.Visibility = isEvent ? Visibility.Visible : Visibility.Collapsed;
            EditStartTimePicker.Header = isEvent ? _loader.GetString("TextStartTime") : _loader.GetString("TextDueTime");
        }
        private void SetupEditProviderComboBox(string forceSelectProvider = null)
        {
            EditCmbProvider.Items.Clear();
            var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
            bool isGoogle = settings.Values["IsGoogleConnected"] as bool? ?? false;
            bool isMs = settings.Values["IsMSConnected"] as bool? ?? false;

            if (isGoogle || forceSelectProvider == "Google")
                EditCmbProvider.Items.Add(new ComboBoxItem { Content = "Google", Tag = "Google" });
            if (isMs || forceSelectProvider == "Microsoft")
                EditCmbProvider.Items.Add(new ComboBoxItem { Content = "Microsoft", Tag = "Microsoft" });

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
            EditDialog.Title = _loader.GetString("TextNewItem");
            EditDialog.SecondaryButtonText = "";
            SetupEditProviderComboBox();
            EditCmbProvider.IsEnabled = true; 
            EditRadioEvent.IsEnabled = true; 
            EditRadioTask.IsEnabled = true;

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
            EditDialog.Title = _loader.GetString("CalendarDialog/Title");
            EditDialog.SecondaryButtonText = _loader.GetString("CalendarDialog/SecondaryButtonText"); // 显示"删除"按钮

            EditTxtTitle.Text = item.Title;
            EditTxtLocation.Text = item.Location;
            EditTxtDescription.Text = item.Description;

            SetupEditProviderComboBox(item.Provider);
            EditCmbProvider.IsEnabled = false;

            EditRadioEvent.IsChecked = item.IsEvent;
            EditRadioTask.IsChecked = item.IsTask;
            EditRadioEvent.IsEnabled = false;
            EditRadioTask.IsEnabled = false;

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
                if (item.Title != null && item.Title.Contains(_loader.GetString("TextNoAgendaTitle"))) return;
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

            string newSubtitleText = _loader.GetString("TextAllDay");
            if (newStartTime.HasValue && newEndTime.HasValue) newSubtitleText = $"{newStartTime.Value:hh\\:mm} - {newEndTime.Value:hh\\:mm}";
            else if (newStartTime.HasValue) newSubtitleText = $"{newStartTime.Value:hh\\:mm}";

            bool isEvent = EditRadioEvent.IsChecked == true;
            bool isAllDay = EditChkAllDay.IsChecked == true;
            string providerName = (EditCmbProvider.SelectedItem as ComboBoxItem)?.Tag.ToString() ?? "Google";

            if (_itemBeingEdited == null)
            {
                try
                {
                    await _syncManager.CreateItemAsync(
                        EditTxtTitle.Text, isEvent, isAllDay, EditDatePicker.Date.DateTime,
                        newStartTime ?? TimeSpan.Zero, newEndTime ?? TimeSpan.Zero, EditTxtLocation.Text, providerName);
                    _ = SyncMonthDataAsync();
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
                        _ = SyncMonthDataAsync();
                    }
                    catch { }
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
    }
}