using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Task_Flyout.Services; // 确保引用了你的服务命名空间
using Google.Apis.Calendar.v3.Data;
using TaskItem = Google.Apis.Tasks.v1.Data.Task;
using Windows.UI;

namespace Task_Flyout
{
    public sealed partial class MainWindow : Window
    {
        private GoogleAuthService _authService;

        // 缓存所有的事件，用于给日历打标记，避免频繁请求 API
        private List<Event> _allCachedEvents = new List<Event>();

        // UI 绑定的数据源
        public ObservableCollection<EventViewModel> SelectedDayEvents { get; set; } = new();
        public ObservableCollection<TaskViewModel> GoogleTasks { get; set; } = new();

        public MainWindow()
        {
            this.InitializeComponent();
            EventsListView.ItemsSource = SelectedDayEvents;
            TasksListView.ItemsSource = GoogleTasks;

            _authService = new GoogleAuthService();
            _ = InitializeGoogleDataAsync();
        }

        private async Task InitializeGoogleDataAsync()
        {
            try
            {
                LoadingRing.IsActive = true;
                LoadingRing.Visibility = Visibility.Visible;

                // 1. 唤起/恢复授权
                await _authService.AuthorizeAsync();

                // 2. 拉取日历数据（拉取前后一个月的作为缓存标记）
                await FetchCalendarEventsAsync();

                // 3. 拉取任务数据
                await FetchTasksAsync();

                // 4. 强制刷新日历，触发 CalendarViewDayItemChanging 以显示标记
                MainCalendar.UpdateLayout();
            }
            catch (Exception ex)
            {
                // 实际项目中建议用 ContentDialog 提示错误
                System.Diagnostics.Debug.WriteLine($"Error initializing Google Data: {ex.Message}");
            }
            finally
            {
                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
            }
        }

        private async Task FetchCalendarEventsAsync()
        {
            if (_authService.CalendarSvc == null) return;

            var request = _authService.CalendarSvc.Events.List("primary");
            request.TimeMin = DateTime.Now.AddMonths(-1); // 缓存上个月
            request.TimeMax = DateTime.Now.AddMonths(2);  // 缓存接下来两个月
            request.ShowDeleted = false;
            request.SingleEvents = true;
            request.OrderBy = Google.Apis.Calendar.v3.EventsResource.ListRequest.OrderByEnum.StartTime;

            var events = await request.ExecuteAsync();
            _allCachedEvents = events.Items != null ? events.Items.ToList() : new List<Event>();
        }

        private async Task FetchTasksAsync()
        {
            if (_authService.TasksSvc == null) return;

            var request = _authService.TasksSvc.Tasks.List("@default");
            request.ShowCompleted = true;
            request.ShowHidden = false;

            var tasks = await request.ExecuteAsync();
            if (tasks.Items != null)
            {
                GoogleTasks.Clear();
                foreach (var task in tasks.Items)
                {
                    GoogleTasks.Add(new TaskViewModel
                    {
                        Id = task.Id,
                        Title = task.Title,
                        IsCompleted = task.Status == "completed"
                    });
                }
            }
        }

        // 💡 核心：为日历中含有日程的天数添加标记
        private void MainCalendar_CalendarViewDayItemChanging(CalendarView sender, CalendarViewDayItemChangingEventArgs args)
        {
            var date = args.Item.Date.Date;

            // 检查缓存中是否有这一天的日程
            bool hasEvents = _allCachedEvents.Any(e =>
            {
                // 全天日程存在 e.Start.Date 中，具体时间日程存在 e.Start.DateTime 中
                DateTime eventDate;
                if (e.Start.DateTime.HasValue) eventDate = e.Start.DateTime.Value.Date;
                else if (DateTime.TryParse(e.Start.Date, out DateTime parsedDate)) eventDate = parsedDate;
                else return false;

                return eventDate == date;
            });

            if (hasEvents)
            {
                // 在日期下方画一个蓝色的提示条 (DensityBar)
                args.Item.SetDensityColors(new List<Color> { Color.FromArgb(255, 0, 120, 215) });
            }
            else
            {
                // 如果没有日程，清除标记
                args.Item.SetDensityColors(null);
            }
        }

        // 选中某一天时，过滤并显示该天的日程
        private void MainCalendar_SelectedDatesChanged(CalendarView sender, CalendarViewSelectedDatesChangedEventArgs args)
        {
            if (args.AddedDates.Count == 0) return;

            var selectedDate = args.AddedDates[0].Date;
            SelectedDateText.Text = $"{selectedDate:yyyy-MM-dd} 的日程";

            SelectedDayEvents.Clear();

            var eventsForDay = _allCachedEvents.Where(e =>
            {
                DateTime eventDate;
                if (e.Start.DateTime.HasValue) eventDate = e.Start.DateTime.Value.Date;
                else if (DateTime.TryParse(e.Start.Date, out DateTime parsedDate)) eventDate = parsedDate;
                else return false;

                return eventDate == selectedDate;
            });

            foreach (var ev in eventsForDay)
            {
                string displayTime = ev.Start.DateTime.HasValue ? ev.Start.DateTime.Value.ToString("HH:mm") : "全天";
                SelectedDayEvents.Add(new EventViewModel { Summary = ev.Summary, DisplayTime = displayTime });
            }
        }

        // 勾选/取消勾选任务的事件（供你后续实现同步到 Google 云端）
        private async void TaskCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            var cb = sender as CheckBox;
            string taskId = cb?.Tag?.ToString();
            // TODO: 调用 _authService.TasksSvc 更新任务状态为 completed
        }

        private async void TaskCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            var cb = sender as CheckBox;
            string taskId = cb?.Tag?.ToString();
            // TODO: 调用 _authService.TasksSvc 更新任务状态为 needsAction
        }
    }

}