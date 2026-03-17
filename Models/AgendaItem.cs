using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Task_Flyout.Models // 建议放在 Models 文件夹下
{
    public class AgendaItem : INotifyPropertyChanged
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }
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

        private string _location;
        public string Location
        {
            get => _location;
            set { _location = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasLocation)); }
        }

        public Microsoft.UI.Xaml.Visibility HasLocation => string.IsNullOrWhiteSpace(Location) ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}