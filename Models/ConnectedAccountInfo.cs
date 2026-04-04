using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Task_Flyout.Models
{
    public class SubscribedCalendarInfo : INotifyPropertyChanged
    {
        public string Id { get; set; }
        public string Name { get; set; }

        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set { if (_isVisible != value) { _isVisible = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ConnectedAccountInfo : INotifyPropertyChanged
    {
        public string ProviderName { get; set; }

        private bool _showEvents = true;
        public bool ShowEvents
        {
            get => _showEvents;
            set { if (_showEvents != value) { _showEvents = value; OnPropertyChanged(); } }
        }

        private bool _showTasks = true;
        public bool ShowTasks
        {
            get => _showTasks;
            set { if (_showTasks != value) { _showTasks = value; OnPropertyChanged(); } }
        }

        public ObservableCollection<SubscribedCalendarInfo> Calendars { get; set; } = new();

        // Display helpers (not serialized, computed at runtime)
        public string IconGlyph => ProviderName switch
        {
            "Google" => "\uE77B",
            "Microsoft" => "\uE77B",
            _ => "\uE77B"
        };

        public string IconColor => ProviderName switch
        {
            "Google" => "#EA4335",
            "Microsoft" => "#0078D4",
            _ => "#888888"
        };

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
