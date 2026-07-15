using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Linq;
using System;
using System.Threading.Tasks;
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
        private const int SW_RESTORE = 9;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        private H.NotifyIcon.TaskbarIcon? _trayIcon;
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _weatherBarWatchdog;
        private UISettings _uiSettings = null!;
        private ResourceLoader _loader = new();
        private string _trayToolTipText = "Task Flyout";
        private string? _currentTrayIconName;
        private bool _traySyncRunning;
        private readonly DeferredStartupWork _accountHydration = new();
        public static FlyoutWindow? MyFlyoutWindow { get; private set; }
        public static MainWindow? MyMainWindow { get; private set; }
        public static WeatherBarWindow? MyWeatherBar { get; private set; }
        public static Microsoft.UI.Dispatching.DispatcherQueue MainDispatcherQueue { get; private set; } = null!;
        public SyncManager SyncManager { get; } = new SyncManager();
        public NotificationService NotificationService { get; private set; } = null!;
        public WeatherService WeatherService { get; } = new WeatherService();
        public MailService MailService { get; } = new MailService();
        internal TaskMutationCoordinator TaskMutations { get; } = new();
        internal ComposeDraftCoordinator ComposeDrafts { get; } = new();

        public async Task DisconnectProviderCompletelyAsync(string providerName)
        {
            providerName = ProviderAuthorizationLifecycle.NormalizeProviderName(providerName);
            await ProviderAuthorizationLifecycle.DisconnectCompletelyAsync(
                () => SyncManager.ClearProviderAuthorizationForDisconnectAsync(providerName),
                () => SyncManager.RemoveAgendaAccountAsync(providerName),
                async () =>
                {
                    var affectedIds = MailService.GetAccounts()
                        .Where(account => ProviderAuthorizationLifecycle.NormalizeProviderName(account.ProviderName) == providerName)
                        .Select(account => account.Id)
                        .ToList();
                    foreach (var accountId in affectedIds)
                        await ComposeDrafts.DiscardForAccountAsync(accountId);
                    MailService.RemoveAccountsForProvider(providerName);
                },
                WebView2RuntimeService.ClearSensitiveBrowsingDataAsync);
            if (MyMainWindow != null)
                await MyMainWindow.RefreshAccountListAsync();
            MyFlyoutWindow?.ReloadFilters();
        }
        public MemoryDiagnosticsService MemoryDiagnostics { get; } = new MemoryDiagnosticsService();

        public App()
        {
            // Global safety net against catastrophic regex backtracking (ReDoS) on
            // untrusted input — the mail/RSS HTML sanitizers run many backtracking
            // patterns over attacker-controlled content. Any Regex without an explicit
            // timeout inherits this and throws RegexMatchTimeoutException instead of
            // pinning a core. Must be set before the first Regex is constructed.
            AppContext.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromSeconds(1));

            this.InitializeComponent();
            this.UnhandledException += (sender, e) =>
            {
                e.Handled = true;
                string errorMsg = DiagnosticEventFormatter.FormatException("app.unhandled", e.Exception);

                try
                {
                    string logDir = AppDataPathHelper.EnsureDirectory(AppDataPathHelper.ResolveLocal("Logs"));
                    string fileName = $"TaskFlyout_CrashLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                    string logPath = AppDataPathHelper.ResolveLocal("Logs", fileName);
                    System.IO.File.WriteAllText(logPath, errorMsg);
                    PruneOldCrashLogs(logDir);
                }
                catch { }
            };
            SyncManager.RegisterProvider(new GoogleSyncProvider());
            SyncManager.RegisterProvider(new Services.MicrosoftSyncProvider());
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            var startup = StartupDiagnostics.Start();
            MainDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            ApplyConfiguredThemeToOpenWindows();
            startup.Mark("dispatcher-theme");

            NotificationService = new NotificationService(SyncManager);
            NotificationService.Initialize();
            MailService.NewMailArrived += MailService_NewMailArrived;
            MemoryDiagnostics.StartIfEnabled();
            startup.Mark("services");

            _trayIcon = (H.NotifyIcon.TaskbarIcon)Resources["MyTrayIcon"];
            _uiSettings = new UISettings();
            _uiSettings.ColorValuesChanged += UiSettings_ColorValuesChanged;
            UpdateTrayIconTheme();
            UpdateTrayStatus(TrayStatus.Idle);
            _trayIcon.ForceCreate(enablesEfficiencyMode: EfficiencyModeEnabledSetting);
            _ = EnsureAccountsHydratedAsync();
            QueueMailPollingStart();
            startup.Mark("tray-mail");

            _trayIcon.LeftClickCommand = new RelayCommand(async () =>
            {
                await EnsureAccountsHydratedAsync();
                EnsureFlyoutWindow().ToggleFlyout();
            });

            // Initialize weather bar if enabled
            InitWeatherBar();
            if (ShouldWeatherBarBeEnabled())
                StartWeatherBarWatchdog();
            startup.Mark("weatherbar");

            // Subscribe for user-started location tracking updates. Do not request location
            // permission during app startup; only WeatherPage user actions may start tracking.
            WeatherService.LocationUpdated += OnWeatherLocationUpdated;

            _trayIcon.DoubleClickCommand = new RelayCommand(() => OpenMainWindowInternal());

            if (_trayIcon.ContextFlyout is MenuFlyout menu)
            {
                foreach (var item in menu.Items.OfType<MenuFlyoutItem>())
                {
                    if (item.Name == "MenuShowMain")
                    {
                        item.Command = new RelayCommand(() => OpenMainWindowInternal());
                    }
                    else if (item.Name == "MenuNewItem")
                    {
                        item.Command = new RelayCommand(OpenTrayNewItem);
                    }
                    else if (item.Name == "MenuSyncNow")
                    {
                        item.Command = new RelayCommand(RunTraySync);
                    }
                    else if (item.Name == "MenuCompose")
                    {
                        item.Command = new RelayCommand(() => OpenMainWindowInternal(window => window.NavigateToMailCompose()));
                    }
                    else if (item.Name == "MenuWeather")
                    {
                        item.Command = new RelayCommand(() => OpenMainWindowInternal(window => window.NavigateToWeather()));
                    }
                    else if (item.Name == "MenuExit")
                    {
                        item.Command = new RelayCommand(() => ExitAppInternal());
                    }
                }
            }

            HandleLaunchActivation(args);
            QueueFlyoutPrewarmIfEnabled();
            QueueInitialNotificationCheck();
            startup.Mark("activation");

            // Launched into the tray with no window on screen — start throttled.
            UpdateEfficiencyMode();
            startup.Mark("efficiency-mode");
            _ = startup.FlushAsync();
        }

        public static bool EfficiencyModeEnabledSetting =>
            ApplicationData.Current.LocalSettings.Values["EfficiencyModeEnabled"] as bool? ?? true;

        /// <summary>
        /// Re-evaluate EcoQoS: throttle when the app is collapsed to the tray (no main
        /// window or flyout on screen), run at full speed while a window is visible.
        /// </summary>
        public static void UpdateEfficiencyMode()
        {
            try
            {
                if (!EfficiencyModeEnabledSetting)
                {
                    EfficiencyModeService.SetEfficiencyMode(false);
                    return;
                }

                bool windowActive =
                    (MyMainWindow?.AppWindow?.IsVisible == true) ||
                    (MyFlyoutWindow?.AppWindow?.IsVisible == true);

                EfficiencyModeService.SetEfficiencyMode(!windowActive);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateEfficiencyMode failed: {ex.Message}");
            }
        }

        private void QueueFlyoutPrewarmIfEnabled()
        {
            bool enabled = ApplicationData.Current.LocalSettings.Values["FlyoutPrewarmEnabled"] as bool? ?? false;
            if (!enabled) return;

            MainDispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
            {
                try
                {
                    await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(8));
                    if (MyFlyoutWindow != null) return;

                    await EnsureAccountsHydratedAsync();
                    EnsureFlyoutWindow();
                }
                catch { }
            });
        }

        private void QueueInitialNotificationCheck()
        {
            if (NotificationService?.IsEnabled != true) return;

            MainDispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2));
                    await EnsureAccountsHydratedAsync();
                    NotificationService.StartPeriodicCheck();
                    NotificationService.CheckUpcomingEvents();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Initial notification check failed: {ex.Message}");
                }
            });
        }

        private void QueueMailPollingStart()
        {
            MainDispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3));
                    await EnsureAccountsHydratedAsync();
                    MailService.StartMailPolling();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Deferred mail polling start failed: {ex.Message}");
                }
            });
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
                MyWeatherBar?.RefreshAfterSystemThemeChanged();
            });
        }

        private void UpdateTrayIconTheme()
        {
            if (_trayIcon == null) return;

            var backgroundColor = _uiSettings.GetColorValue(UIColorType.Background);
            bool isDarkTheme = backgroundColor == Windows.UI.Color.FromArgb(255, 0, 0, 0);
            string iconName = isDarkTheme ? "TrayIcon_Dark.ico" : "TrayIcon_Light.ico";

            if (!string.Equals(_currentTrayIconName, iconName, StringComparison.Ordinal))
            {
                _trayIcon.IconSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri($"ms-appx:///Assets/{iconName}"));
                _currentTrayIconName = iconName;
            }

            _trayIcon.ToolTipText = _trayToolTipText;
        }

        private void MailService_NewMailArrived(object? sender, NewMailNotificationEventArgs e)
        {
            MainDispatcherQueue.TryEnqueue(() => UpdateTrayNewMailHint(e));
        }

        private void UpdateTrayNewMailHint(NewMailNotificationEventArgs e)
        {
            UpdateTrayStatus(TrayStatus.NewMail);
        }

        private async void OpenTrayNewItem()
        {
            try
            {
                await EnsureAccountsHydratedAsync();
                EnsureFlyoutWindow().ShowNewItem();
            }
            catch { UpdateTrayStatus(TrayStatus.NeedsAttention); }
        }

        private async void RunTraySync()
        {
            if (_traySyncRunning) return;
            _traySyncRunning = true;
            UpdateTrayStatus(TrayStatus.Syncing);
            try
            {
                await EnsureAccountsHydratedAsync();
                bool succeeded = await EnsureFlyoutWindow().RefreshNowAsync();
                UpdateTrayStatus(succeeded ? TrayStatus.Finished : TrayStatus.NeedsAttention);
            }
            catch { UpdateTrayStatus(TrayStatus.NeedsAttention); }
            finally { _traySyncRunning = false; }
        }

        private void UpdateTrayStatus(TrayStatus status)
        {
            if (_trayIcon == null) return;
            var descriptor = TrayStatusPolicy.Describe(status);
            var text = _loader.GetStringOrDefault(descriptor.ResourceKey) ?? descriptor.Fallback;
            _trayToolTipText = $"Task Flyout · {text}";
            _trayIcon.ToolTipText = _trayToolTipText;
            if (_trayIcon.ContextFlyout is MenuFlyout menu
                && menu.Items.OfType<MenuFlyoutItem>().FirstOrDefault(item => item.Name == "MenuStatus") is { } statusItem)
                statusItem.Text = _trayToolTipText;
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

        private static FlyoutWindow EnsureFlyoutWindow()
        {
            if (MyFlyoutWindow == null)
            {
                MyFlyoutWindow = new FlyoutWindow();
                ApplyConfiguredThemeToOpenWindows();
            }

            return MyFlyoutWindow;
        }

        private Task EnsureAccountsHydratedAsync()
            => _accountHydration.RunAsync(() =>
            {
                try
                {
                    SyncManager.AccountManager.Load();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Deferred account hydration failed: {ex.Message}");
                }
                return Task.CompletedTask;
            });

        private void OnWeatherLocationUpdated(object? sender, EventArgs e)
        {
            MainDispatcherQueue?.TryEnqueue(() =>
            {
                RefreshWeatherBar();
                _ = MyFlyoutWindow?.RefreshWeatherAsync(forceRefresh: true);
            });
        }

        private void InitWeatherBar()
        {
            if (ShouldWeatherBarBeEnabled())
            {
                MyWeatherBar = new WeatherBarWindow();
                MyWeatherBar.ShowBar();
            }
        }

        private bool ShouldWeatherBarBeEnabled()
            => (ApplicationData.Current.LocalSettings.Values["WeatherBarEnabled"] as bool? ?? false)
               && WeatherService.IsEnabled;

        // The weather bar is reparented as a WS_CHILD of Shell_TrayWnd, so an Explorer restart
        // destroys its native window and it silently disappears — and any later call into the
        // dead window (e.g. toggling "match taskbar") crashes natively. This watchdog detects
        // the dead/missing bar and rebuilds a fresh one once the taskbar is back.
        private void StartWeatherBarWatchdog()
        {
            if (_weatherBarWatchdog != null) return;

            _weatherBarWatchdog = MainDispatcherQueue.CreateTimer();
            _weatherBarWatchdog.Interval = TimeSpan.FromSeconds(30);
            _weatherBarWatchdog.Tick += (_, _) => CheckWeatherBarAlive();
            _weatherBarWatchdog.Start();
        }

        private void CheckWeatherBarAlive()
        {
            try
            {
                if (!ShouldWeatherBarBeEnabled())
                {
                    StopWeatherBarWatchdog();
                    return;
                }

                // Alive and well — nothing to do (also covers the user-hidden case: the HWND
                // still exists when merely hidden).
                if (MyWeatherBar != null && MyWeatherBar.IsAlive()) return;

                // Don't recreate mid-restart before the taskbar exists, or the new bar would
                // briefly float as a stray top-level window.
                if (FindWindow("Shell_TrayWnd", null) == IntPtr.Zero) return;

                var dead = MyWeatherBar;
                MyWeatherBar = null;
                dead?.DetachForRecovery();

                MyWeatherBar = new WeatherBarWindow();
                ApplyConfiguredThemeToOpenWindows();
                MyWeatherBar.ShowBar();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WeatherBar watchdog failed: {ex.Message}");
            }
        }

        public static void ToggleWeatherBar(bool enabled)
        {
            MainDispatcherQueue.TryEnqueue(async () =>
            {
                Windows.Storage.ApplicationData.Current.LocalSettings.Values["WeatherBarEnabled"] = enabled;

                if (enabled && (App.Current as App)?.WeatherService?.IsEnabled == true)
                {
                    (App.Current as App)?.StartWeatherBarWatchdog();
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
                    (App.Current as App)?.StopWeatherBarWatchdog();
                }
            });
        }

        private void StopWeatherBarWatchdog()
        {
            _weatherBarWatchdog?.Stop();
            _weatherBarWatchdog = null;
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
            MainDispatcherQueue.TryEnqueue(async () =>
            {
                if (Current is App app)
                    await app.EnsureAccountsHydratedAsync();
                if (MyMainWindow == null)
                {
                    MyMainWindow = new MainWindow();
                    ApplyConfiguredThemeToOpenWindows();
                    MyMainWindow.Closed += (s, args) => { MyMainWindow = null; UpdateEfficiencyMode(); };
                }

                if (onOpened == null)
                    MyMainWindow.EnsureContentLoaded();

                MyMainWindow.Activate();
                MyMainWindow.AppWindow.Show();
                BringMainWindowToFront();
                ClearTrayMailHint();
                UpdateEfficiencyMode(); // window on screen — run at full speed

                onOpened?.Invoke(MyMainWindow);
            });
        }

        private static void ClearTrayMailHint()
        {
            if (Current is not App app || app._trayIcon == null) return;

            app.UpdateTrayStatus(TrayStatus.Idle);
            try
            {
                app._trayIcon.ClearNotifications();
            }
            catch { }
        }

        private static void BringMainWindowToFront()
        {
            if (MyMainWindow == null) return;

            try
            {
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(MyMainWindow);
                if (hWnd == IntPtr.Zero) return;

                ShowWindow(hWnd, SW_RESTORE);
                MyMainWindow.Activate();
                SetForegroundWindow(hWnd);
            }
            catch
            {
                MyMainWindow.Activate();
            }
        }

        private static void PruneOldCrashLogs(string logDir)
        {
            try
            {
                var files = System.IO.Directory.GetFiles(logDir, "TaskFlyout_CrashLog_*.txt");
                if (files.Length <= 10) return;

                foreach (var path in files.OrderByDescending(p => p).Skip(10))
                {
                    try { System.IO.File.Delete(path); } catch { }
                }
            }
            catch { }
        }

        private bool _isExiting;

        // Public entry point so the main window's close-to-exit path can terminate the app.
        // Pass the window currently being closed so we don't re-enter its Close().
        public static void ExitApp(Window? closingWindow = null)
        {
            if (Current is App app)
                app.ExitAppInternal(closingWindow);
        }

        private async void ExitAppInternal(Window? closingWindow = null)
        {
            if (_isExiting) return;
            _isExiting = true;

            _weatherBarWatchdog?.Stop();
            NotificationService?.Stop();
            MailService.StopMailPolling();
            MailService.StopPendingMutationRetryScheduler();
            MailService.NewMailArrived -= MailService_NewMailArrived;
            await FlushPendingSavesBeforeExitAsync();
            if (_uiSettings != null) _uiSettings.ColorValuesChanged -= UiSettings_ColorValuesChanged;
            _trayIcon?.Dispose();
            MyWeatherBar?.DetachForRecovery();
            if (!ReferenceEquals(MyFlyoutWindow, closingWindow)) MyFlyoutWindow?.Close();
            if (!ReferenceEquals(MyMainWindow, closingWindow)) MyMainWindow?.Close();
            Exit();
        }

        private async Task FlushPendingSavesBeforeExitAsync()
        {
            try
            {
                await Task.WhenAll(
                    SyncManager.AccountManager.FlushPendingSavesAsync(),
                    MailService.FlushPendingSavesAsync(),
                    ComposeDrafts.FlushAsync(),
                    RssService.FlushPendingCheckpointsAsync(),
                    LocalSqliteStore.FlushPendingCheckpointAsync())
                    .WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Flush pending saves before exit failed: {ex.Message}");
            }
        }
    }
}
