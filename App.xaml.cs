using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Linq;
using System;
using Task_Flyout.Services;
using Windows.Storage;
using Windows.UI.ViewManagement;

namespace Task_Flyout
{
    public class RelayCommand : System.Windows.Input.ICommand
    {
        private readonly System.Action _execute;
        public RelayCommand(System.Action execute) => _execute = execute;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute();
        public event System.EventHandler? CanExecuteChanged { add { } remove { } }
    }

    public partial class App : Application
    {
        private H.NotifyIcon.TaskbarIcon? _trayIcon;
        private UISettings _uiSettings = null!;
        public static FlyoutWindow? MyFlyoutWindow { get; private set; }
        public static MainWindow? MyMainWindow { get; private set; }
        public static WeatherBarWindow? MyWeatherBar { get; private set; }
        public static Microsoft.UI.Dispatching.DispatcherQueue MainDispatcherQueue { get; private set; } = null!;
        public SyncManager SyncManager { get; } = new SyncManager();
        public NotificationService NotificationService { get; private set; } = null!;
        public WeatherService WeatherService { get; } = new WeatherService();
        public MailService MailService { get; } = new MailService();

        public App()
        {
            this.InitializeComponent();
            this.UnhandledException += (sender, e) =>
            {
                e.Handled = true;
                string errorMsg = $"Fatal Error! Please contact us! \nTime：{DateTime.Now:yyyy-MM-dd HH:mm:ss}\nError：{e.Exception.Message}\n\nStack:\n{e.Exception.StackTrace}";

                string logDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "TaskFlyout", "Logs");
                string logPath = System.IO.Path.Combine(logDir, "TaskFlyout_CrashLog.txt");

                try
                {
                    System.IO.Directory.CreateDirectory(logDir);
                    System.IO.File.WriteAllText(logPath, errorMsg);
                }
                catch { }
            };
            SyncManager.RegisterProvider(new GoogleSyncProvider());
            SyncManager.RegisterProvider(new Services.MicrosoftSyncProvider());
            SyncManager.AccountManager.Load();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            MainDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            MyFlyoutWindow = new FlyoutWindow();
            ApplyConfiguredThemeToOpenWindows();

            NotificationService = new NotificationService(SyncManager);
            NotificationService.Initialize();
            if (NotificationService.IsEnabled)
                NotificationService.StartPeriodicCheck();
            MailService.StartMailPolling();

            _trayIcon = (H.NotifyIcon.TaskbarIcon)Resources["MyTrayIcon"];
            _uiSettings = new UISettings();
            _uiSettings.ColorValuesChanged += UiSettings_ColorValuesChanged;
            UpdateTrayIconTheme();
            _trayIcon.ForceCreate();

            _trayIcon.LeftClickCommand = new RelayCommand(() => MyFlyoutWindow.ToggleFlyout());

            // Initialize weather bar if enabled
            InitWeatherBar();

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

            HandleLaunchActivation(args);
        }

        private static void HandleLaunchActivation(LaunchActivatedEventArgs args)
        {
            try
            {
                var activationArgs = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
                if (activationArgs.Kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.AppNotification &&
                    activationArgs.Data is Microsoft.Windows.AppNotifications.AppNotificationActivatedEventArgs notificationArgs)
                {
                    NotificationService.OpenFromActivationArguments(notificationArgs.Argument);
                    return;
                }
            }
            catch
            {
                // Older activation paths can still surface as command-line arguments.
            }

            var activationArgument = GetToastActivationArgument(args?.Arguments);
            if (!string.IsNullOrWhiteSpace(activationArgument))
                NotificationService.OpenFromActivationArguments(activationArgument);
        }

        private static string? GetToastActivationArgument(string? launchArguments)
        {
            const string prefix = "----AppNotificationActivated:";

            if (!string.IsNullOrWhiteSpace(launchArguments))
            {
                var prefixIndex = launchArguments.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
                if (prefixIndex >= 0)
                    return launchArguments[(prefixIndex + prefix.Length)..].Trim().Trim('"');
            }

            foreach (var argument in Environment.GetCommandLineArgs())
            {
                var prefixIndex = argument.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
                if (prefixIndex >= 0)
                    return argument[(prefixIndex + prefix.Length)..].Trim().Trim('"');
            }

            return null;
        }

        private void UiSettings_ColorValuesChanged(UISettings sender, object args)
        {
            MainDispatcherQueue.TryEnqueue(() =>
            {
                UpdateTrayIconTheme();
                ApplyConfiguredThemeToOpenWindows();
                MyWeatherBar?.ApplyWindowsTheme();
            });
        }

        private void UpdateTrayIconTheme()
        {
            if (_trayIcon == null) return;

            var backgroundColor = _uiSettings.GetColorValue(UIColorType.Background);
            bool isDarkTheme = backgroundColor == Windows.UI.Color.FromArgb(255, 0, 0, 0);
            string iconName = isDarkTheme ? "TrayIcon_Dark.ico" : "TrayIcon_Light.ico";

            _trayIcon.IconSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri($"ms-appx:///Assets/{iconName}"));
        }

        public static ElementTheme GetConfiguredTheme()
        {
            string theme = ApplicationData.Current.LocalSettings.Values["AppTheme"] as string ?? "Default";
            return theme switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }

        public static void ApplyConfiguredThemeToOpenWindows()
        {
            var theme = GetConfiguredTheme();

            if (MyMainWindow?.Content is FrameworkElement mainRoot)
                mainRoot.RequestedTheme = theme;

            if (MyFlyoutWindow?.Content is FrameworkElement flyoutRoot)
                flyoutRoot.RequestedTheme = theme;
        }

        private void InitWeatherBar()
        {
            bool weatherBarEnabled = Windows.Storage.ApplicationData.Current.LocalSettings.Values["WeatherBarEnabled"] as bool? ?? false;
            if (weatherBarEnabled && WeatherService.IsEnabled)
            {
                MyWeatherBar = new WeatherBarWindow();
                MyWeatherBar.ShowBar();
            }
        }

        public static void ToggleWeatherBar(bool enabled)
        {
            MainDispatcherQueue.TryEnqueue(async () =>
            {
                Windows.Storage.ApplicationData.Current.LocalSettings.Values["WeatherBarEnabled"] = enabled;

                if (enabled && (App.Current as App)?.WeatherService?.IsEnabled == true)
                {
                    if (MyWeatherBar == null)
                    {
                        MyWeatherBar = new WeatherBarWindow();
                    }
                    MyWeatherBar.ShowBar();
                    await MyWeatherBar.RefreshWeatherAsync();
                }
                else
                {
                    MyWeatherBar?.HideBar();
                }
            });
        }

        public static void RefreshWeatherBar()
        {
            MainDispatcherQueue.TryEnqueue(async () =>
            {
                if (MyWeatherBar != null)
                    await MyWeatherBar.RefreshWeatherAsync();
            });
        }

        public static void OpenMainWindowInternal(Action<MainWindow>? onOpened = null)
        {
            MainDispatcherQueue.TryEnqueue(() =>
            {
                if (MyMainWindow == null)
                {
                    MyMainWindow = new MainWindow();
                    ApplyConfiguredThemeToOpenWindows();
                    MyMainWindow.Closed += (s, args) => { MyMainWindow = null; };
                }

                MyMainWindow.Activate();
                MyMainWindow.AppWindow.Show();

                onOpened?.Invoke(MyMainWindow);
            });
        }

        private void ExitAppInternal()
        {
            NotificationService?.Stop();
            MailService?.StopMailPolling();
            _trayIcon?.Dispose();
            MyWeatherBar?.Close();
            MyFlyoutWindow?.Close();
            MyMainWindow?.Close();
            Exit();
        }
    }
}
