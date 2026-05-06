using Microsoft.UI.Xaml;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Task_Flyout.Models
{
    public class CalendarDay : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public DateTime Date { get; set; }

        public string DayText => Date.Day.ToString();

        private bool _isCurrentMonth;
        public bool IsCurrentMonth
        {
            get => _isCurrentMonth;
            set { _isCurrentMonth = value; OnPropertyChanged(); OnPropertyChanged(nameof(TextOpacity)); }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public bool IsToday => Date.Date == DateTime.Today;

        // 新增：将 bool 转换为字体粗细
        public Windows.UI.Text.FontWeight TodayFontWeight => IsToday ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;

        // 新增：将 bool 转换为可见性 (用于今天的圆圈边框)
        public Microsoft.UI.Xaml.Visibility TodayVisibility => IsToday ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        public double TextOpacity => IsCurrentMonth ? 1.0 : 0.4;

        private int _eventCount;
        public int EventCount
        {
            get => _eventCount;
            set
            {
                _eventCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Dot1Vis));
                OnPropertyChanged(nameof(Dot2Vis));
                OnPropertyChanged(nameof(Dot3Vis));
                OnPropertyChanged(nameof(Dot4Vis));
            }
        }

        // 控制小胶囊显示的快捷属性
        public Visibility Dot1Vis => EventCount >= 1 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility Dot2Vis => EventCount >= 2 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility Dot3Vis => EventCount >= 3 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility Dot4Vis => EventCount >= 4 ? Visibility.Visible : Visibility.Collapsed;
    }
}
