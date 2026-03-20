using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Linq;
using System;
using Task_Flyout.Services;

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
        public static FlyoutWindow? MyFlyoutWindow { get; private set; }
        public static MainWindow? MyMainWindow { get; private set; }
        public static Microsoft.UI.Dispatching.DispatcherQueue MainDispatcherQueue { get; private set; }
        public SyncManager SyncManager { get; } = new SyncManager();

        public App()
        {
            this.InitializeComponent();
            SyncManager.RegisterProvider(new GoogleSyncProvider());
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            MainDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            MyFlyoutWindow = new FlyoutWindow();

            _trayIcon = (H.NotifyIcon.TaskbarIcon)Resources["MyTrayIcon"];
            _trayIcon.ForceCreate();

            _trayIcon.LeftClickCommand = new RelayCommand(() => MyFlyoutWindow.ToggleFlyout());

            _trayIcon.DoubleClickCommand = new RelayCommand(() => OpenMainWindowInternal());

            if (_trayIcon.ContextFlyout is MenuFlyout menu)
            {
                foreach (var item in menu.Items.OfType<MenuFlyoutItem>())
                {
                    if (item.Text.Contains("主页面"))
                    {
                        item.Command = new RelayCommand(() => OpenMainWindowInternal());
                    }
                    else if (item.Text.Contains("退出"))
                    {
                        item.Command = new RelayCommand(() => ExitAppInternal());
                    }
                }
            }
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