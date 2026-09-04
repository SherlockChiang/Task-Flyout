using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Task_Flyout.Models
{
    public class SubscribedCalendarInfo : INotifyPropertyChanged
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";

        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set { if (_isVisible != value) { _isVisible = value; OnPropertyChanged(); } }
        }

        private string _colorHex = "";
        public string ColorHex
        {
            get => _colorHex;
            set { if (_colorHex != value) { _colorHex = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ConnectedAccountInfo : INotifyPropertyChanged
    {
        public string ProviderName { get; set; } = "";
        public string AccountId { get; set; } = "";

        private string _displayName = "";
        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (string.Equals(_displayName, value, System.StringComparison.Ordinal)) return;
                _displayName = value ?? "";
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }

        [JsonIgnore]
        public string ProviderKey
            => Task_Flyout.Services.AccountIdentityPolicy.CreateProviderKey(ProviderName, AccountId);

        [JsonIgnore]
        public string DisplayTitle
            => string.IsNullOrWhiteSpace(DisplayName) ? ProviderName : DisplayName;

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

        private string _taskColorHex = "";
        public string TaskColorHex
        {
            get => _taskColorHex;
            set { if (_taskColorHex != value) { _taskColorHex = value; OnPropertyChanged(); } }
        }

        public ObservableCollection<SubscribedCalendarInfo> Calendars { get; set; } = new();

        // Display helpers (not serialized, computed at runtime)
        [JsonIgnore]
        public string IconGlyph => ProviderName switch
        {
            "Google" => "\uE77B",
            "Microsoft" => "\uE77B",
            "iCloud" => "\uE787",
            _ => "\uE77B"
        };

        [JsonIgnore]
        public string IconColor => ProviderName switch
        {
            "Google" => "#EA4335",
            "Microsoft" => "#0078D4",
            "iCloud" => "#6E6E73",
            _ => "#888888"
        };

        [JsonIgnore]
        public bool SupportsTasks
            => Task_Flyout.Services.SyncProviderCapabilityPolicy.ForProvider(ProviderName).SupportsTasks;
        [JsonIgnore]
        public Microsoft.UI.Xaml.Visibility TaskSettingsVisibility
            => SupportsTasks ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
