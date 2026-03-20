using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Task_Flyout.Models
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
            set
            {
                _location = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasLocation));
            }
        }

        // 👉 新增：详情/备注字段，并实现双向绑定通知
        private string _description;
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
        public string DateKey { get; set; }

        public Microsoft.UI.Xaml.Visibility HasLocation => string.IsNullOrWhiteSpace(Location) ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

        // 👉 新增：控制详情 UI 显隐的属性，保持与 HasLocation 一致的代码风格
        public Microsoft.UI.Xaml.Visibility HasDescription => string.IsNullOrWhiteSpace(Description) ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;


        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}