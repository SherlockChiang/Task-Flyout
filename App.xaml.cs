using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Linq;
using System;
using Task_Flyout.Services;
using Windows.UI.ViewManagement;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Task_Flyout
{
    public class RelayCommand : System.Windows.Input.ICommand
    {
        private readonly System.Action _execute;
        public RelayCommand(System.Action execute) => _execute = execute;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute();
        public event System.EventHandler? CanExecuteChanged;
    }

    public partial class App : Application
    {
        private H.NotifyIcon.TaskbarIcon? _trayIcon;
        private UISettings _uiSettings;
        public static FlyoutWindow? MyFlyoutWindow { get; private set; }
        public static MainWindow? MyMainWindow { get; private set; }
        public static Microsoft.UI.Dispatching.DispatcherQueue MainDispatcherQueue { get; private set; }
        public SyncManager SyncManager { get; } = new SyncManager();

        public App()
        {
            this.InitializeComponent();
            SyncManager.RegisterProvider(new GoogleSyncProvider());
            SyncManager.RegisterProvider(new Services.MicrosoftSyncProvider());
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            MainDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            MyFlyoutWindow = new FlyoutWindow();

            _trayIcon = (H.NotifyIcon.TaskbarIcon)Resources["MyTrayIcon"];
            _uiSettings = new UISettings();
            _uiSettings.ColorValuesChanged += UiSettings_ColorValuesChanged;
            UpdateTrayIconTheme();
            _trayIcon.ForceCreate();

            _trayIcon.LeftClickCommand = new RelayCommand(() => MyFlyoutWindow.ToggleFlyout());

            _trayIcon.DoubleClickCommand = new RelayCommand(() => OpenMainWindowInternal());

            if (_trayIcon.ContextFlyout is MenuFlyout menu)
            {
                foreach (var item in menu.Items.OfType<MenuFlyoutItem>())
                {
                    if (item.Name == "MenuShowMain")
                    {
                        item.Command = new RelayCommand(() => OpenMainWindowInternal());
                    }
                    else if (item.Name == "MenuExit")
                    {
                        item.Command = new RelayCommand(() => ExitAppInternal());
                    }
                }
            }
        }

        private void UiSettings_ColorValuesChanged(UISettings sender, object args)
        {
            MainDispatcherQueue.TryEnqueue(() => UpdateTrayIconTheme());
        }

        private void UpdateTrayIconTheme()
        {
            if (_trayIcon == null) return;

            var backgroundColor = _uiSettings.GetColorValue(UIColorType.Background);
            bool isDarkTheme = backgroundColor == Windows.UI.Color.FromArgb(255, 0, 0, 0);

            string iconName = isDarkTheme ? "TrayIcon_Dark.ico" : "TrayIcon_Light.ico";

            _trayIcon.IconSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri($"ms-appx:///Assets/{iconName}"));
        }

        public static void OpenMainWindowInternal(Action<MainWindow> onOpened = null)
        {
            MainDispatcherQueue.TryEnqueue(() =>
            {
                if (MyMainWindow == null)
                {
                    MyMainWindow = new MainWindow();
                    MyMainWindow.Closed += (s, args) => { MyMainWindow = null; };
                }

                MyMainWindow.Activate();
                MyMainWindow.AppWindow.Show();

                onOpened?.Invoke(MyMainWindow);
            });
        }

        private void ExitAppInternal()
        {
            _trayIcon?.Dispose();
            MyFlyoutWindow?.Close();
            MyMainWindow?.Close();
            Exit();
        }
    }
}