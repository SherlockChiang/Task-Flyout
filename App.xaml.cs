using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Linq;
using System;

namespace Task_Flyout
{
    // 💡 1. 把 RelayCommand 提出来，放在 App 类的外面！
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
        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            MainDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            MyFlyoutWindow = new FlyoutWindow();

            _trayIcon = (H.NotifyIcon.TaskbarIcon)Resources["MyTrayIcon"];
            _trayIcon.ForceCreate();

            // 左键点击保持不变
            _trayIcon.LeftClickCommand = new RelayCommand(() => MyFlyoutWindow.ToggleFlyout());

            // 👉 重点修改：改用 Command 属性
            if (_trayIcon.ContextFlyout is MenuFlyout menu)
            {
                foreach (var item in menu.Items.OfType<MenuFlyoutItem>())
                {
                    if (item.Text.Contains("主页面"))
                    {
                        // 使用 Command 而非 Click 事件
                        item.Command = new RelayCommand(() => OpenMainWindowInternal());
                    }
                    else if (item.Text.Contains("退出"))
                    {
                        item.Command = new RelayCommand(() => ExitAppInternal());
                    }
                }
            }
        }

        // 👉 抽取逻辑到独立方法，确保逻辑清晰
        private void OpenMainWindowInternal()
        {
            System.Diagnostics.Debug.WriteLine("=== Command triggered: Opening MainWindow ===");

            MainDispatcherQueue.TryEnqueue(() =>
            {
                if (MyMainWindow == null)
                {
                    MyMainWindow = new MainWindow();
                    MyMainWindow.Closed += (s, args) => { MyMainWindow = null; };
                }

                MyMainWindow.Activate();
                MyMainWindow.AppWindow.Show();
                System.Diagnostics.Debug.WriteLine("=== MainWindow state: Shown ===");
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