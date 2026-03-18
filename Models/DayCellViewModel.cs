// Models/DayCellViewModel.cs
using System;
using System.Collections.ObjectModel;

namespace Task_Flyout.Models
{
    public class DayCellViewModel
    {
        public DateTime Date { get; set; }
        public string DayNumber => Date.Day.ToString();
        public bool IsCurrentMonth { get; set; }
        public bool IsToday => Date.Date == DateTime.Today;

        // 存放这一天的所有日程和任务
        public ObservableCollection<AgendaItem> Items { get; set; } = new();
    }
}