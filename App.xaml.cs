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

        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            // 💡 核心修复：提前把窗口 new 出来，但不调用 ToggleFlyout()，它就会在后台隐身待命。
            // 这样既能秒开，又能为托盘插件提供 UI 线程和 XamlRoot 的支持。
            MyFlyoutWindow = new FlyoutWindow();

            // 1. 获取托盘图标资源
            _trayIcon = (H.NotifyIcon.TaskbarIcon)Resources["MyTrayIcon"];
            _trayIcon.ForceCreate();

            // 2. 绑定左键点击命令
            _trayIcon.LeftClickCommand = new RelayCommand(() =>
            {
                // 因为上面已经 new 过了，这里直接呼出即可
                MyFlyoutWindow.ToggleFlyout();
            });

            // 3. 挂载右键菜单点击事件
            if (_trayIcon.ContextFlyout is MenuFlyout menu)
            {
                foreach (var item in menu.Items.OfType<MenuFlyoutItem>())
                {
                    if (item.Text == "显示主页面") item.Click += ShowMainWindow_Click;
                    else if (item.Text == "退出") item.Click += ExitApp_Click;
                }
            }
        }

        private void ShowMainWindow_Click(object sender, RoutedEventArgs e)
        {
            MyMainWindow ??= new MainWindow();
            MyMainWindow.Activate();
        }

        private void ExitApp_Click(object sender, RoutedEventArgs e)
        {
            _trayIcon?.Dispose();
            MyFlyoutWindow?.Close();
            MyMainWindow?.Close();
            Exit();
        }
    }
}