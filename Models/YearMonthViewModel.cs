using System;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;

namespace Task_Flyout.Models
{
    public class YearMonthViewModel
    {
        public DateTime MonthStart { get; set; }
        public string Title { get; set; } = "";
        public string CountText { get; set; } = "";
        public Visibility EmptyVisibility { get; set; } = Visibility.Collapsed;
        public ObservableCollection<YearDayViewModel> Days { get; } = new();
    }

    public class YearDayViewModel
    {
        public DateTime? Date { get; set; }
        public string DayNumber => Date?.Day.ToString() ?? string.Empty;
        public string ColorHex { get; set; } = "";
        public string ToolTip { get; set; } = "";
        public Visibility DayVisibility => Visibility.Visible;
        public Visibility MarkerVisibility => string.IsNullOrWhiteSpace(ColorHex) ? Visibility.Collapsed : Visibility.Visible;
    }
}
