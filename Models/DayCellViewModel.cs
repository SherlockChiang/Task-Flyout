using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace Task_Flyout.Models
{
    public class DayCellViewModel : INotifyPropertyChanged
    {
        private int _hiddenItemCount;

        public DateTime Date { get; set; }
        public string DayNumber => Date.Day.ToString();
        public bool IsCurrentMonth { get; set; }
        public bool IsToday => Date.Date == DateTime.Today;

        public int HiddenItemCount
        {
            get => _hiddenItemCount;
            set
            {
                if (_hiddenItemCount == value) return;
                _hiddenItemCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OverflowVisibility));
                OnPropertyChanged(nameof(OverflowText));
            }
        }

        public Visibility OverflowVisibility => HiddenItemCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        public string OverflowText => HiddenItemCount > 0 ? $"+{HiddenItemCount}" : string.Empty;

        public ObservableCollection<AgendaItem> Items { get; set; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
