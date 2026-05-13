using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Task_Flyout.Models
{
    public class AgendaItem : INotifyPropertyChanged
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public bool IsTask { get; set; }
        public bool IsEvent { get; set; }

        private bool _isCompleted;
        public bool IsCompleted
        {
            get => _isCompleted;
            set
            {
                if (_isCompleted != value)
                {
                    _isCompleted = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _location = "";
        public string Location
        {
            get => _location;
            set
            {
                _location = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasLocation));
            }
        }

        private string _description = "";
        public string Description
        {
            get => _description;
            set
            {
                _description = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasDescription));
            }
        }

        public string Provider { get; set; } = "Local";
        public string CalendarId { get; set; } = "";
        public string CalendarName { get; set; } = "";
        public string DateKey { get; set; } = "";
        public string ColorHex { get; set; } = "";
        public bool IsRecurring { get; set; }
        public string RecurringEventId { get; set; } = "";
        public string RecurrenceKind { get; set; } = "None";

        public DateTime? StartDateTime { get; set; }
        public DateTime? EndDateTime { get; set; }

        public Microsoft.UI.Xaml.Visibility HasLocation => string.IsNullOrWhiteSpace(Location) ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

        public Microsoft.UI.Xaml.Visibility HasDescription => string.IsNullOrWhiteSpace(Description) ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;


        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
