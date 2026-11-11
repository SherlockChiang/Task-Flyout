using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Linq;
using System;
using System.Threading;
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
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _nativeWidgetsVerificationTimer;
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _taskbarRestartListenerRetryTimer;
        private H.NotifyIcon.Core.MessageWindow? _taskbarMessageWindow;
        private long _lastWeatherBarRecreationTimestamp;
        private int _nativeWidgetsVerificationAttempts;
        private static readonly TimeSpan NormalWeatherBarWatchdogInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan RecoveryWeatherBarWatchdogInterval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan WeatherBarRecreationCooldown = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan NativeWidgetsVerificationInterval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan NativeWidgetsBackgroundRetryInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan TaskbarRestartListenerRetryInterval = TimeSpan.FromSeconds(2);
        private const int NativeWidgetsFastVerificationAttempts = 5;
        private UISettings _uiSettings = null!;
        private ResourceLoader _loader = new();
        private string _trayToolTipText = "Task Flyout";
        private string? _currentTrayIconName;
        private bool _traySyncRunning;
        private readonly DeferredStartupWork _accountHydration = new();
        private WindowsWidgetsAvailability _windowsWidgetsAvailability =
            WindowsWidgetsAvailability.Unavailable(
                WindowsWidgetsAvailabilityReason.DetectionFailed,
                "Native Widgets availability has not been checked.");
        private DateTime _windowsWidgetsAvailabilityCheckedAtUtc = DateTime.MinValue;
        private bool _nativeWidgetsModeActive;
        private bool _nativeWidgetsActivationFailed;
        private volatile bool _weatherCompanionBridgeEnabled;
        private WeatherCompanionCoordinator? _weatherCompanionCoordinator;
        private StandaloneTaskbarCoordinator? _standaloneTaskbarCoordinator;
        private Task? _standaloneTaskbarHandoffTask;
        private bool _standaloneTaskbarSuppressed;
        private bool _standaloneWidgetsHandoffPending;
        private bool _standaloneWidgetsRemovalVerificationActive;
        private bool _standaloneWidgetsSuppressionReady;
        private bool _standaloneWidgetsVerificationBackgroundOnly;
        private bool _standaloneTaskbarExplorerRecoveryPending;
        private const string WeatherCompanionBridgeEnabledSettingKey = "WeatherCompanionBridgeEnabled";
        private string _weatherBarModeRuntimeDetail = string.Empty;
        private static readonly TimeSpan WindowsWidgetsAvailabilityCacheLifetime = TimeSpan.FromSeconds(30);
        public static FlyoutWindow? MyFlyoutWindow { get; private set; }
        public static MainWindow? MyMainWindow { get; private set; }
        public static WeatherBarWindow? MyWeatherBar { get; private set; }
        public static Microsoft.UI.Dispatching.DispatcherQueue MainDispatcherQueue { get; private set; } = null!;
        internal static event EventHandler? WeatherBarModeStatusChanged;
        public SyncManager SyncManager { get; } = new SyncManager();
        public NotificationService NotificationService { get; private set; } = null!;
        public WeatherService WeatherService { get; } = new WeatherService();
        public MailService MailService { get; } = new MailService();
        internal TaskMutationCoordinator TaskMutations { get; } = new();
        internal ComposeDraftCoordinator ComposeDrafts { get; } = new();

        public async Task<MailAccount> ConnectNewGoogleAccountAsync(
            CancellationToken cancellationToken = default)
        {
            await EnsureAccountsHydratedAsync();
            var provider = SyncManager.RegisterGoogleProviderForNewAccount();
            bool connected = false;
            try
            {
                await provider.ConnectInteractivelyAsync(selectAccount: true, cancellationToken);
                var account = await MailService.AddGoogleAccountAsync(provider, cancellationToken);
                connected = true;
                return account;
            }
            finally
            {
                if (!connected)
                {
                    try { await provider.ClearLocalAuthorizationAsync(); }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Temporary Google authorization cleanup failed: {ex.Message}");
                    }
                    SyncManager.UnregisterProvider(provider.ProviderName, provider.AccountId);
                }
            }
        }

        public async Task<MailAccount> ReconnectGoogleAccountAsync(
            string accountId,
            CancellationToken cancellationToken = default)
        {
            await EnsureAccountsHydratedAsync();
            string resolvedAccountId = AccountIdentityPolicy.ResolveAccountId("Google", accountId);
            var agendaAccount = SyncManager.AccountManager.GetAccount("Google", resolvedAccountId);
            var linkedMailAccount = MailService
                .GetAccountsForProviderAccount("Google", resolvedAccountId)
                .FirstOrDefault();
            if (agendaAccount == null && linkedMailAccount == null)
                throw new InvalidOperationException("The selected Google account is no longer connected.");

            string? expectedAddress = linkedMailAccount?.Address;
            if (string.IsNullOrWhiteSpace(expectedAddress) &&
                agendaAccount?.DisplayName?.Contains('@') == true)
            {
                expectedAddress = agendaAccount.DisplayName;
            }

            var provider = SyncManager.EnsureGoogleProvider(
                resolvedAccountId,
                agendaAccount?.DisplayName ?? linkedMailAccount?.Address);
            try
            {
                await provider.ReconnectInteractivelyAsync(expectedAddress, cancellationToken);
                return await MailService.AddGoogleAccountAsync(
                    provider,
                    cancellationToken,
                    expectedAddress);
            }
            catch (GoogleAccountAlreadyConnectedException)
            {
                try { await provider.ClearLocalAuthorizationAsync(); }
                catch (Exception cleanupEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Mismatched Google authorization cleanup failed: {cleanupEx.Message}");
                }
                throw;
            }
        }

        public async Task DisconnectProviderCompletelyAsync(
            string providerName,
            string? accountId)
        {
            providerName = ProviderAuthorizationLifecycle.NormalizeProviderName(providerName);
            var agendaAccount = SyncManager.AccountManager.GetAccount(providerName, accountId);
            string? resolvedAccountId = agendaAccount?.AccountId;
            if (string.IsNullOrWhiteSpace(resolvedAccountId) && !string.IsNullOrWhiteSpace(accountId))
                resolvedAccountId = AccountIdentityPolicy.ResolveAccountId(providerName, accountId);

            if (string.Equals(providerName, "Google", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(resolvedAccountId))
            {
                var linkedMailAccount = MailService
                    .GetAccountsForProviderAccount(providerName, resolvedAccountId)
                    .FirstOrDefault();
                SyncManager.EnsureGoogleProvider(
                    resolvedAccountId,
                    agendaAccount?.DisplayName ?? linkedMailAccount?.Address);
            }

            await ProviderAuthorizationLifecycle.DisconnectCompletelyAsync(
                () => SyncManager.ClearProviderAuthorizationForDisconnectAsync(providerName, resolvedAccountId),
                () => SyncManager.RemoveAgendaAccountAsync(providerName, resolvedAccountId),
                async () =>
                {
                    var affectedAccounts = string.IsNullOrWhiteSpace(resolvedAccountId)
                        ? MailService.GetAccounts()
                            .Where(account => ProviderAuthorizationLifecycle.NormalizeProviderName(account.ProviderName) == providerName)
                            .ToList()
                        : MailService.GetAccountsForProviderAccount(providerName, resolvedAccountId).ToList();
                    var affectedIds = affectedAccounts
                        .Select(account => account.Id)
                        .ToList();
                    foreach (var accountId in affectedIds)
                        await ComposeDrafts.DiscardForAccountAsync(accountId);
                    MailService.RemoveAccountsForProvider(providerName, resolvedAccountId);
                },
                WebView2RuntimeService.ClearSensitiveBrowsingDataAsync);
            if (string.Equals(providerName, "Google", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(resolvedAccountId))
            {
                SyncManager.UnregisterProvider(providerName, resolvedAccountId);
            }
            if (MyMainWindow != null)
                await MyMainWindow.RefreshAccountListAsync();
            MyFlyoutWindow?.ReloadFilters();
        }
        public MemoryDiagnosticsService MemoryDiagnostics { get; } = new MemoryDiagnosticsService();
        private BackgroundRefreshCoordinator? _backgroundRefresh;

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
            SyncManager.RegisterProvider(new Services.MicrosoftSyncProvider());
            SyncManager.RegisterProvider(new Services.ICloudSyncProvider());
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            bool performanceDiagnosticsEnabled =
                (ApplicationData.Current.LocalSettings.Values["PerformanceDiagnosticsEnabled"] as bool? ?? false)
                || string.Equals(Environment.GetEnvironmentVariable("TASKFLYOUT_PERFORMANCE_DIAGNOSTICS"), "1", StringComparison.Ordinal);
            PerformanceDiagnostics.Initialize(performanceDiagnosticsEnabled);
            var startup = PerformanceDiagnostics.StartProcessSpanOnce("startup.tray", "startup", "tray_interactive", "process");
            MainDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            _standaloneTaskbarSuppressed = StandaloneTaskbarLaunchPolicy.IsSuppressed(
                GetToastActivationArgument(args.Arguments),
                Environment.GetEnvironmentVariable(
                    StandaloneTaskbarLaunchPolicy.DisableEnvironmentVariable));
            Windows.System.MemoryManager.AppMemoryUsageIncreased += MemoryManager_AppMemoryUsageIncreased;
            ApplyConfiguredThemeToOpenWindows();

            NotificationService = new NotificationService(SyncManager);
            NotificationService.Initialize();
            _backgroundRefresh = new BackgroundRefreshCoordinator(
                MainDispatcherQueue,
                SyncManager,
                NotificationService,
                MailService);
            MailService.NewMailArrived += MailService_NewMailArrived;
            MemoryDiagnostics.StartIfEnabled();

            _trayIcon = (H.NotifyIcon.TaskbarIcon)Resources["MyTrayIcon"];
            _uiSettings = new UISettings();
            _uiSettings.ColorValuesChanged += UiSettings_ColorValuesChanged;
            UpdateTrayIconTheme();
            UpdateTrayStatus(TrayStatus.Idle);
            _trayIcon.ForceCreate(enablesEfficiencyMode: EfficiencyModeEnabledSetting);
            _ = EnsureAccountsHydratedAsync();
            _ = SyncManager.WarmCacheAsync();
            QueueBackgroundRefreshStart();

            var trayInteraction = new TrayInteractionCoordinator(
                EnsureAccountsHydratedAsync,
                () => EnsureFlyoutWindow().ToggleFlyout(),
                () => MyFlyoutWindow?.DismissForMainWindowAsync() ?? Task.CompletedTask,
                () => OpenMainWindowInternal());
            _trayIcon.LeftClickCommand = new RelayCommand(async () =>
            {
                var openRequest = PerformanceDiagnostics.StartSpan("flyout", "tray_click_to_open_request", "tray");
                try
                {
                    EfficiencyModeService.SetEfficiencyMode(false);
                    // AccountManager.Load populates an ObservableCollection. Complete
                    // hydration before Flyout construction so its initial filter pass
                    // observes a stable account snapshot.
                    bool applied = await trayInteraction.ToggleFlyoutAsync();
                    openRequest.Complete(applied ? "success" : "superseded");
                    if (!applied) UpdateEfficiencyMode();
                }
                catch (Exception ex)
                {
                    openRequest.Complete("failure");
                    System.Diagnostics.Debug.WriteLine($"Opening tray flyout failed: {ex.Message}");
                    UpdateEfficiencyMode();
                }
            });

            // Initialize weather bar if enabled
            InitWeatherBar();

            // Resume a previously user-enabled tracking preference after startup.
            WeatherService.LocationUpdated += OnWeatherLocationUpdated;
            QueueLocationTrackingResume();

            // H.NotifyIcon suppresses a pending single click on double click.
            // Also invalidate any single click already awaiting account hydration.
            _trayIcon.DoubleClickCommand = new RelayCommand(async () =>
            {
                try
                {
                    EfficiencyModeService.SetEfficiencyMode(false);
                    await trayInteraction.OpenMainWindowAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Opening main window from tray failed: {ex.Message}");
                    UpdateEfficiencyMode();
                }
            });

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

            // Launched into the tray with no window on screen — start throttled.
            UpdateEfficiencyMode();
            startup.Complete();
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
                    (MyFlyoutWindow?.IsVisibleOrOpening == true);

                EfficiencyModeService.SetEfficiencyMode(!windowActive);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateEfficiencyMode failed: {ex.Message}");
            }
        }

        private void QueueFlyoutPrewarmIfEnabled()
        {
            bool? configured = ApplicationData.Current.LocalSettings.Values["FlyoutPrewarmEnabled"] as bool?;
            if (!CanPrewarmFlyout(configured)) return;

            MainDispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
            {
                try
                {
                    await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(8));
                    if (MyFlyoutWindow != null) return;
                    if (!CanPrewarmFlyout(configured)) return;

                    await EnsureAccountsHydratedAsync();
                    await SyncManager.WarmCacheAsync();
                    if (!CanPrewarmFlyout(configured)) return;
                    EnsureFlyoutWindow().Prewarm();
                }
                catch { }
            });
        }

        private static bool CanPrewarmFlyout(bool? configured)
        {
            try
            {
                return FlyoutResidencyPolicy.ShouldPrewarm(
                    configured,
                    Windows.System.MemoryManager.AppMemoryUsageLevel >= Windows.System.AppMemoryUsageLevel.Medium,
                    checked((long)Windows.System.MemoryManager.AppMemoryUsage),
                    checked((long)Windows.System.MemoryManager.AppMemoryUsageLimit));
            }
            catch
            {
                return false;
            }
        }

        private void QueueBackgroundRefreshStart()
        {
            MainDispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2));
                    await EnsureAccountsHydratedAsync();
                    MailService.StartMailPolling();
                    _backgroundRefresh?.Start();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Background refresh start failed: {ex.Message}");
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

        private void MemoryManager_AppMemoryUsageIncreased(object? sender, object e)
        {
            var level = Windows.System.MemoryManager.AppMemoryUsageLevel;
            if (level < Windows.System.AppMemoryUsageLevel.Medium) return;

            MainDispatcherQueue.TryEnqueue(() =>
            {
                if (level >= Windows.System.AppMemoryUsageLevel.High)
                {
                    MyMainWindow?.ReleaseMailForMemoryPressure();
                    MailService.ClearVolatileMessageBodies();
                }
                else
                {
                    MailService.TrimVolatileMessageBodies();
                }

                MyFlyoutWindow?.TrimMemoryCaches();
            });
        }

        private void QueueLocationTrackingResume()
        {
            if (!WeatherService.AutoFollowLocation) return;

            MainDispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3));
                    if (WeatherService.AutoFollowLocation && !WeatherService.IsLocationTrackingActive)
                        await WeatherService.StartLocationTrackingAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Resuming location tracking failed: {ex.Message}");
                }
            });
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
                EfficiencyModeService.SetEfficiencyMode(false);
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

            MyFlyoutWindow?.ApplyConfiguredTheme(theme);
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
                    MailService.SynchronizeAgendaAccountDisplayNames(SyncManager.AccountManager);
                    SyncManager.HydrateAccountProviders();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Deferred account hydration failed: {ex.Message}");
                }
                return Task.CompletedTask;
            });

        private void OnWeatherLocationUpdated(object? sender, EventArgs e)
        {
            if (_isExiting) return;
            MainDispatcherQueue?.TryEnqueue(() =>
            {
                if (_isExiting) return;
                RefreshWeatherBar(forceRefresh: true);
                _ = MyFlyoutWindow?.RefreshWeatherAsync(forceRefresh: true);
            });
        }

        private void InitWeatherBar()
        {
            ApplyWeatherBarPresentation(forceProbe: true);
        }

        private bool ShouldWeatherBarBeEnabled()
        {
            if (_nativeWidgetsActivationFailed)
            {
                var values = ApplicationData.Current.LocalSettings.Values;
                return (values["WeatherBarEnabled"] as bool? ?? false) && WeatherService.IsEnabled;
            }

            return ResolveWeatherBarMode().ShouldRunTaskFlyoutBar;
        }

        private WeatherBarModeResolution ResolveWeatherBarMode(bool forceProbe = false)
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            WeatherBarMode requestedMode = WeatherBarModeSettings.ReadAndMigrate(values);
            bool weatherBarEnabled = values["WeatherBarEnabled"] as bool? ?? false;
            WindowsWidgetsAvailability availability = ProbeWindowsWidgetsAvailability(forceProbe);
            return WeatherBarModePolicy.Resolve(
                weatherBarEnabled,
                WeatherService.IsEnabled,
                requestedMode,
                availability.IsAvailable,
                standaloneTaskbarActive:
                    _standaloneTaskbarCoordinator?.Status.State ==
                        StandaloneTaskbarRuntimeState.MountReady);
        }

        private WindowsWidgetsAvailability ProbeWindowsWidgetsAvailability(bool forceRefresh = false)
        {
            DateTime now = DateTime.UtcNow;
            if (!forceRefresh &&
                _windowsWidgetsAvailabilityCheckedAtUtc != DateTime.MinValue &&
                now - _windowsWidgetsAvailabilityCheckedAtUtc < WindowsWidgetsAvailabilityCacheLifetime)
            {
                return _windowsWidgetsAvailability;
            }

            _windowsWidgetsAvailability = WindowsWidgetsService.Detect();
            _windowsWidgetsAvailabilityCheckedAtUtc = now;
            return _windowsWidgetsAvailability;
        }

        private bool DisposeWeatherBar()
        {
            WeatherBarWindow? bar = MyWeatherBar;
            if (bar == null) return true;

            try
            {
                if (!bar.DetachForRecovery()) return false;
                if (ReferenceEquals(MyWeatherBar, bar))
                    MyWeatherBar = null;
                return true;
            }
            catch
            {
                // Keep the object reference when its HWND survived Close(). This
                // prevents a second child window from being created over an orphan.
                return false;
            }
        }

        private void ApplyWeatherBarPresentation(bool forceProbe = false)
        {
            if (_isExiting) return;
            try
            {
                WeatherBarModeResolution resolution = ResolveWeatherBarMode(forceProbe);
                var values = ApplicationData.Current.LocalSettings.Values;
                WeatherBarMode requestedMode = WeatherBarModeSettings.Read(values);
                bool standaloneModeDesired =
                    StandaloneTaskbarWidgetsLifecyclePolicy.IsStandaloneDesired(
                        _standaloneTaskbarSuppressed,
                        requestedMode,
                        values["WeatherBarEnabled"] as bool? ?? false,
                        WeatherService.IsEnabled,
                        _standaloneTaskbarCoordinator?.DiagnosticOnlyRejected ?? false);
                if (standaloneModeDesired &&
                    !_standaloneWidgetsRemovalVerificationActive)
                {
                    // A Windows Widgets activation timer belongs to the opposite
                    // transition. Start standalone removal with a fresh backoff.
                    StopNativeWidgetsVerification();
                }
                bool existingWidgetsOwnershipResolved = true;
                if (!resolution.ShouldUseWindowsWidgets)
                {
                    bool suppressionRecoveryPending =
                        StandaloneTaskbarWidgetsService.HasCapturedTaskbarEntry(values) ||
                        StandaloneTaskbarWidgetsService.IsNotificationPending(values);
                    if (!standaloneModeDesired && !suppressionRecoveryPending)
                        StopNativeWidgetsVerification();
                    bool hadCapturedWidgetsEntry =
                        WindowsWidgetsService.HasCapturedTaskbarEntry(values);
                    if (!_standaloneTaskbarSuppressed &&
                        (_nativeWidgetsModeActive || hadCapturedWidgetsEntry))
                    {
                        bool restored = WindowsWidgetsService.TryRestoreTaskbarEntry(
                            values,
                            out string restoreDetail);
                        existingWidgetsOwnershipResolved = restored;
                        _nativeWidgetsModeActive = !restored;
                        _weatherBarModeRuntimeDetail = restoreDetail;
                        if (!restored)
                        {
                            _standaloneWidgetsRemovalVerificationActive =
                                requestedMode == WeatherBarMode.StandaloneTaskbar;
                            StartNativeWidgetsVerification(resetBackoff: true);
                        }
                        else if (hadCapturedWidgetsEntry &&
                                 requestedMode == WeatherBarMode.StandaloneTaskbar)
                        {
                            _standaloneWidgetsRemovalVerificationActive = true;
                        }
                    }
                    _nativeWidgetsActivationFailed = false;
                }

                if (standaloneModeDesired)
                {
                    if (existingWidgetsOwnershipResolved)
                    {
                        _standaloneWidgetsSuppressionReady =
                            PrepareStandaloneWidgetsSuppression(values);
                    }
                    else
                    {
                        _standaloneWidgetsSuppressionReady = false;
                        _standaloneWidgetsHandoffPending = true;
                        SetStandaloneWidgetsVerification(enabled: true);
                    }
                }
                else
                {
                    _standaloneWidgetsSuppressionReady = false;
                    _standaloneWidgetsHandoffPending = false;
                    _standaloneTaskbarExplorerRecoveryPending = false;
                }

                bool standaloneRequested = CanRequestStandaloneTaskbar(
                    values,
                    requestedMode);
                bool adoptedCleanupLease =
                    EnsureStandaloneCoordinatorForPersistedCleanup(values);
                if (adoptedCleanupLease)
                    standaloneRequested = false;

                // Start the existing bounded weather pipe before asking Explorer
                // to load the native controller. On disable, request stop first;
                // UpdateWeatherCompanionBridge keeps the pipe alive while cleanup
                // remains unacknowledged.
                Task standaloneTransition;
                bool standaloneTransitionWasIncomplete;
                if (standaloneRequested)
                {
                    bool companionReady = UpdateWeatherCompanionBridge(
                        values,
                        requestedMode);
                    standaloneRequested = companionReady;
                    if (companionReady)
                        StandaloneTaskbarCleanupSettings.MarkPending(values);
                    if (companionReady &&
                        _standaloneTaskbarExplorerRecoveryPending)
                    {
                        _standaloneTaskbarExplorerRecoveryPending = false;
                        standaloneTransition = _standaloneTaskbarCoordinator
                            ?.IsRequested == true
                            ? _standaloneTaskbarCoordinator.RefreshAsync()
                            : SetStandaloneTaskbarEnabled(enabled: true);
                    }
                    else
                    {
                        if (!companionReady)
                            _standaloneTaskbarExplorerRecoveryPending = false;
                        standaloneTransition = SetStandaloneTaskbarEnabled(
                            companionReady);
                    }
                    standaloneTransitionWasIncomplete = !standaloneTransition.IsCompleted;
                }
                else
                {
                    if (adoptedCleanupLease)
                    {
                        // A stale Explorer host may still be polling this pipe.
                        // Bring the pipe up before the cold-start idempotent stop.
                        UpdateWeatherCompanionBridge(values, requestedMode);
                        standaloneTransition =
                            _standaloneTaskbarCoordinator!.RefreshAsync();
                    }
                    else
                    {
                        standaloneTransition = SetStandaloneTaskbarEnabled(false);
                    }
                    standaloneTransitionWasIncomplete = !standaloneTransition.IsCompleted;
                    UpdateWeatherCompanionBridge(values, requestedMode);
                }

                bool standaloneCleanupPending =
                    !standaloneTransition.IsCompleted ||
                    (_standaloneTaskbarCoordinator?.RequiresStop ?? false);
                if (standaloneTransitionWasIncomplete)
                {
                    QueueWeatherPresentationAfterStandaloneTransition(
                        standaloneTransition);
                }
                ClearStandaloneCleanupLeaseIfSafe(values);
                bool suppressionRestoreReady = true;
                if (!standaloneModeDesired)
                {
                    suppressionRestoreReady =
                        RestoreStandaloneWidgetsSuppressionIfSafe(
                            values,
                            standaloneCleanupPending);
                }
                if (standaloneRequested)
                {
                    _weatherBarModeRuntimeDetail =
                        $"standalone:{_standaloneTaskbarCoordinator?.Status.DiagnosticKey ?? "starting"}";
                }

                if (resolution.ShouldUseWindowsWidgets)
                {
                    if (_standaloneTaskbarSuppressed ||
                        standaloneCleanupPending ||
                        !suppressionRestoreReady)
                    {
                        if (_standaloneTaskbarSuppressed ||
                            standaloneCleanupPending)
                            StopNativeWidgetsVerification();
                        _nativeWidgetsActivationFailed = true;
                        if (_standaloneTaskbarSuppressed)
                        {
                            _weatherBarModeRuntimeDetail =
                                "standalone:taskbar-control-suppressed";
                        }
                        else if (standaloneCleanupPending)
                        {
                            _weatherBarModeRuntimeDetail =
                                $"standalone:{_standaloneTaskbarCoordinator?.Status.DiagnosticKey ?? "stopping"}";
                        }
                        bool fallbackAllowed =
                            (values["WeatherBarEnabled"] as bool? ?? false) &&
                            WeatherService.IsEnabled;
                        if (fallbackAllowed)
                        {
                            EnsureTaskFlyoutWeatherBar(force: true);
                            SetWeatherBarRecoveryPolling(enabled: true);
                        }
                        else
                        {
                            DisposeWeatherBar();
                            StopWeatherBarWatchdog();
                            EnsureTaskbarRestartListener();
                        }
                        return;
                    }

                    EnsureTaskbarRestartListener();
                    bool entryRequested = WindowsWidgetsService.TryEnableTaskbarEntry(values, out string detail);
                    _nativeWidgetsModeActive = entryRequested;

                    // TaskbarDa is only a request. Explorer materializes the actual
                    // AugmentedEntryPointButton asynchronously, and policy/theme state
                    // can prevent it from appearing. Keep the existing weather bar until
                    // the native bridge is visible so switching modes cannot leave a hole.
                    WindowsWidgetsAvailability refreshed = ProbeWindowsWidgetsAvailability(forceRefresh: true);
                    if (refreshed.NativeEntryPointPresent)
                    {
                        CompleteNativeWidgetsActivation(refreshed.Detail);
                        return;
                    }

                    if (!entryRequested)
                    {
                        KeepTaskFlyoutFallbackWhileNativeWidgetsPending(detail);
                        return;
                    }

                    KeepTaskFlyoutFallbackWhileNativeWidgetsPending(
                        "Windows Widgets was enabled; waiting for Explorer to create its taskbar entry.");
                    return;
                }

                if (resolution.ShouldRunTaskFlyoutBar)
                {
                    EnsureTaskFlyoutWeatherBar();
                    return;
                }

                if (resolution.ShouldUseStandaloneTaskbar)
                {
                    if (!DisposeWeatherBar())
                    {
                        _weatherBarModeRuntimeDetail =
                            "standalone:fallback-close-pending";
                        StartWeatherBarWatchdog();
                        SetWeatherBarRecoveryPolling(enabled: true);
                        return;
                    }

                    StopWeatherBarWatchdog();
                    EnsureTaskbarRestartListener();
                    return;
                }

                DisposeWeatherBar();
                StopWeatherBarWatchdog();
                if (standaloneCleanupPending)
                    EnsureTaskbarRestartListener();
                if (requestedMode == WeatherBarMode.WindowsWidgets &&
                    resolution.FallbackReason == WeatherBarFallbackReason.WindowsWidgetsUnavailable)
                {
                    _weatherBarModeRuntimeDetail = ProbeWindowsWidgetsAvailability().Detail;
                }
            }
            catch (Exception ex)
            {
                _weatherBarModeRuntimeDetail = $"Weather bar mode update failed: {ex.Message}";
                bool fallbackAllowed =
                    (ApplicationData.Current.LocalSettings.Values["WeatherBarEnabled"] as bool? ?? false) &&
                    WeatherService.IsEnabled;
                _nativeWidgetsActivationFailed = fallbackAllowed;
                if (fallbackAllowed)
                    EnsureTaskFlyoutWeatherBar(force: true);
                else
                {
                    DisposeWeatherBar();
                    StopWeatherBarWatchdog();
                }
            }
            finally
            {
                NotifyWeatherBarModeStatusChanged();
            }
        }

        private void CompleteNativeWidgetsActivation(string detail)
        {
            _nativeWidgetsModeActive = true;
            if (!DisposeWeatherBar())
            {
                _nativeWidgetsActivationFailed = true;
                _weatherBarModeRuntimeDetail =
                    "The native Widgets entry is ready; waiting to close the Task Flyout fallback safely.";
                StartNativeWidgetsVerification();
                return;
            }

            _nativeWidgetsActivationFailed = false;
            _weatherBarModeRuntimeDetail = detail;
            StopNativeWidgetsVerification();
            StopWeatherBarWatchdog();
            EnsureTaskbarRestartListener();
        }

        private bool PrepareStandaloneWidgetsSuppression(
            System.Collections.Generic.IDictionary<string, object> values)
        {
            StandaloneTaskbarWidgetsResult result =
                StandaloneTaskbarWidgetsService.TrySuppress(values);
            bool nativeEntryPresent = result.IsEffectivelySuppressed &&
                ProbeWindowsWidgetsAvailability(forceRefresh: true)
                    .NativeEntryPointPresent;
            bool ready = result.Succeeded &&
                result.IsEffectivelySuppressed &&
                !nativeEntryPresent;
            _standaloneWidgetsHandoffPending = !ready;
            _weatherBarModeRuntimeDetail = nativeEntryPresent
                ? "standalone:waiting-for-windows-widgets-removal"
                : $"standalone:{result.DiagnosticKey}";

            bool shouldRetry =
                StandaloneTaskbarWidgetsLifecyclePolicy.ShouldKeepVerification(
                    result,
                    nativeEntryPresent);
            SetStandaloneWidgetsVerification(
                shouldRetry,
                backgroundOnly: ready && !result.NotificationPending);
            return ready;
        }

        private bool RestoreStandaloneWidgetsSuppressionIfSafe(
            System.Collections.Generic.IDictionary<string, object> values,
            bool controllerCleanupPending)
        {
            bool hasOwnership =
                StandaloneTaskbarWidgetsService.HasCapturedTaskbarEntry(values);
            bool notificationPending =
                StandaloneTaskbarWidgetsService.IsNotificationPending(values);
            if (!hasOwnership && !notificationPending)
            {
                SetStandaloneWidgetsVerification(enabled: false);
                return true;
            }

            if (!StandaloneTaskbarWidgetsLifecyclePolicy.CanRestore(
                    _standaloneTaskbarSuppressed,
                    standaloneDesired: false,
                    controllerCleanupPending))
            {
                SetStandaloneWidgetsVerification(enabled: false);
                _weatherBarModeRuntimeDetail = _standaloneTaskbarSuppressed
                    ? "standalone:widgets-restore-suppressed"
                    : "standalone:waiting-for-controller-stop";
                return false;
            }

            StandaloneTaskbarWidgetsResult result =
                StandaloneTaskbarWidgetsService.TryRestore(values);
            _weatherBarModeRuntimeDetail =
                $"standalone:{result.DiagnosticKey}";
            SetStandaloneWidgetsVerification(
                StandaloneTaskbarWidgetsLifecyclePolicy.ShouldKeepVerification(
                    result,
                    nativeEntryPointPresent: false));
            return result.Succeeded;
        }

        private void SetStandaloneWidgetsVerification(
            bool enabled,
            bool backgroundOnly = false)
        {
            bool wasActive = _standaloneWidgetsRemovalVerificationActive;
            bool wasBackgroundOnly =
                _standaloneWidgetsVerificationBackgroundOnly;
            _standaloneWidgetsRemovalVerificationActive = enabled;
            if (enabled)
            {
                bool resetBackoff = !wasActive ||
                    (wasBackgroundOnly && !backgroundOnly);
                StartNativeWidgetsVerification(resetBackoff);
                _standaloneWidgetsVerificationBackgroundOnly = backgroundOnly;
                if (backgroundOnly && _nativeWidgetsVerificationTimer != null)
                {
                    _nativeWidgetsVerificationAttempts =
                        NativeWidgetsFastVerificationAttempts;
                    _nativeWidgetsVerificationTimer.Interval =
                        NativeWidgetsBackgroundRetryInterval;
                }
            }
            else
            {
                _standaloneWidgetsVerificationBackgroundOnly = false;
                if (wasActive)
                    StopNativeWidgetsVerification();
            }
        }

        private Task SetStandaloneTaskbarEnabled(bool enabled)
        {
            if (_isExiting || _standaloneTaskbarSuppressed)
                enabled = false;

            if (_standaloneTaskbarCoordinator == null)
            {
                if (!enabled) return Task.CompletedTask;

                var coordinator = new StandaloneTaskbarCoordinator();
                coordinator.StatusChanged += StandaloneTaskbarCoordinator_StatusChanged;
                _standaloneTaskbarCoordinator = coordinator;
            }

            if (enabled)
                EnsureTaskbarRestartListener();
            return _standaloneTaskbarCoordinator.SetEnabledAsync(enabled);
        }

        private bool EnsureStandaloneCoordinatorForPersistedCleanup(
            System.Collections.Generic.IDictionary<string, object> values)
        {
            if (_standaloneTaskbarSuppressed ||
                _standaloneTaskbarCoordinator != null ||
                !StandaloneTaskbarCleanupSettings.IsPending(values))
            {
                return false;
            }

            var coordinator = new StandaloneTaskbarCoordinator(
                cleanupRequired: true);
            coordinator.StatusChanged += StandaloneTaskbarCoordinator_StatusChanged;
            _standaloneTaskbarCoordinator = coordinator;
            EnsureTaskbarRestartListener();
            return true;
        }

        private void ClearStandaloneCleanupLeaseIfSafe(
            System.Collections.Generic.IDictionary<string, object> values)
        {
            StandaloneTaskbarCoordinator? coordinator =
                _standaloneTaskbarCoordinator;
            if (coordinator != null &&
                !coordinator.IsRequested &&
                !coordinator.RequiresStop)
            {
                try { StandaloneTaskbarCleanupSettings.Clear(values); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Clearing standalone taskbar cleanup lease failed: {ex.Message}");
                }
            }
        }

        private bool CanRequestStandaloneTaskbar(
            System.Collections.Generic.IDictionary<string, object> values,
            WeatherBarMode requestedMode)
            => !_standaloneTaskbarSuppressed &&
               requestedMode == WeatherBarMode.StandaloneTaskbar &&
               (values["WeatherBarEnabled"] as bool? ?? false) &&
               WeatherService.IsEnabled &&
               _standaloneWidgetsSuppressionReady &&
               !(_standaloneTaskbarCoordinator is
                     { IsRequested: false, RequiresStop: true }) &&
               !(_standaloneTaskbarCoordinator?.DiagnosticOnlyRejected ?? false) &&
               !_nativeWidgetsModeActive &&
               !WindowsWidgetsService.HasCapturedTaskbarEntry(values) &&
               !_standaloneWidgetsHandoffPending;

        private void StandaloneTaskbarCoordinator_StatusChanged(
            StandaloneTaskbarRuntimeStatus status)
        {
            MainDispatcherQueue?.TryEnqueue(() =>
            {
                if (_isExiting) return;
                StandaloneTaskbarCoordinator? coordinator =
                    _standaloneTaskbarCoordinator;
                if (coordinator == null || coordinator.Status != status)
                    return;

                ClearStandaloneCleanupLeaseIfSafe(
                    ApplicationData.Current.LocalSettings.Values);
                _weatherBarModeRuntimeDetail =
                    $"standalone:{status.DiagnosticKey}";
                NotifyWeatherBarModeStatusChanged();
                // Mount readiness is a separate, asynchronous proof. Re-run the
                // pure presentation policy for every current controller state;
                // repeated desired-state calls are idempotent and do not retry
                // the broker, while ready/lost transitions close/reopen fallback.
                ApplyWeatherBarPresentation();
            });
        }

        private void QueueWeatherPresentationAfterStandaloneTransition(
            Task transition)
        {
            if (ReferenceEquals(_standaloneTaskbarHandoffTask, transition))
                return;

            _standaloneTaskbarHandoffTask = transition;
            _ = ReapplyWeatherPresentationAfterStandaloneTransitionAsync(
                transition);
        }

        private async Task ReapplyWeatherPresentationAfterStandaloneTransitionAsync(
            Task transition)
        {
            try { await transition.ConfigureAwait(false); }
            catch { }

            MainDispatcherQueue?.TryEnqueue(() =>
            {
                if (_isExiting ||
                    !ReferenceEquals(_standaloneTaskbarHandoffTask, transition))
                {
                    return;
                }

                _standaloneTaskbarHandoffTask = null;
                ClearStandaloneCleanupLeaseIfSafe(
                    ApplicationData.Current.LocalSettings.Values);
                ApplyWeatherBarPresentation();
            });
        }

        private bool UpdateWeatherCompanionBridge(
            System.Collections.Generic.IDictionary<string, object> values,
            WeatherBarMode requestedMode)
        {
            if (_isExiting)
                return _weatherCompanionBridgeEnabled;

            bool weatherBarEnabled =
                values["WeatherBarEnabled"] as bool? ?? false;
            bool standaloneRequested = CanRequestStandaloneTaskbar(
                values,
                requestedMode);
            bool standaloneCleanupPending =
                _standaloneTaskbarCoordinator?.RequiresStop ?? false;
            bool windowsWidgetsBridgeRequested =
                (values[WeatherCompanionBridgeEnabledSettingKey] as bool? ?? false) &&
                requestedMode == WeatherBarMode.WindowsWidgets;
            bool enabled = standaloneCleanupPending ||
                (weatherBarEnabled &&
                 WeatherService.IsEnabled &&
                 (standaloneRequested || windowsWidgetsBridgeRequested));
            _weatherCompanionBridgeEnabled = enabled;

            if (!enabled)
            {
                _weatherCompanionCoordinator?.SetEnabled(false);
                return false;
            }

            try
            {
                if (_weatherCompanionCoordinator == null)
                {
                    _weatherCompanionCoordinator = new WeatherCompanionCoordinator(
                        WeatherService,
                        TryQueueWeatherOpen,
                        report => _standaloneTaskbarCoordinator
                            ?.ReportMountReadiness(report) == true);
                    _weatherCompanionCoordinator.Start();
                }
                _weatherCompanionCoordinator.SetEnabled(true);
                return true;
            }
            catch (Exception ex)
            {
                _weatherCompanionBridgeEnabled = false;
                _weatherCompanionCoordinator = null;
                _weatherBarModeRuntimeDetail = "Weather companion bridge unavailable.";
                System.Diagnostics.Debug.WriteLine($"Weather companion bridge failed to start: {ex.Message}");
                return false;
            }
        }

        private bool TryQueueWeatherOpen()
        {
            if (_isExiting) return false;
            return MainDispatcherQueue?.TryEnqueue(() =>
                OpenMainWindowInternal(window => window.NavigateToWeather())) == true;
        }

        private void KeepTaskFlyoutFallbackWhileNativeWidgetsPending(string detail)
        {
            _nativeWidgetsActivationFailed = true;
            _weatherBarModeRuntimeDetail = detail;
            StartNativeWidgetsVerification(resetBackoff: true);

            bool fallbackAllowed =
                (ApplicationData.Current.LocalSettings.Values["WeatherBarEnabled"] as bool? ?? false) &&
                WeatherService.IsEnabled;
            if (fallbackAllowed)
            {
                EnsureTaskFlyoutWeatherBar(force: true);
                SetWeatherBarRecoveryPolling(enabled: true);
                return;
            }

            DisposeWeatherBar();
            StopWeatherBarWatchdog();
            EnsureTaskbarRestartListener();
        }

        private void StartNativeWidgetsVerification(bool resetBackoff = false)
        {
            if (_nativeWidgetsVerificationTimer != null)
            {
                if (resetBackoff)
                {
                    _nativeWidgetsVerificationAttempts = 0;
                    _nativeWidgetsVerificationTimer.Interval = NativeWidgetsVerificationInterval;
                }
                return;
            }

            _nativeWidgetsVerificationAttempts = 0;
            _nativeWidgetsVerificationTimer = MainDispatcherQueue.CreateTimer();
            _nativeWidgetsVerificationTimer.Interval = NativeWidgetsVerificationInterval;
            _nativeWidgetsVerificationTimer.Tick += (_, _) => VerifyNativeWidgetsEntry();
            _nativeWidgetsVerificationTimer.Start();
            EnsureTaskbarRestartListener();
        }

        private void StopNativeWidgetsVerification()
        {
            _nativeWidgetsVerificationTimer?.Stop();
            _nativeWidgetsVerificationTimer = null;
            _nativeWidgetsVerificationAttempts = 0;
            _standaloneWidgetsRemovalVerificationActive = false;
            _standaloneWidgetsVerificationBackgroundOnly = false;
            ReconcileTaskbarRestartListener();
        }

        private void VerifyNativeWidgetsEntry()
        {
            if (_isExiting)
            {
                StopNativeWidgetsVerification();
                return;
            }

            var values = ApplicationData.Current.LocalSettings.Values;
            bool enabled = values["WeatherBarEnabled"] as bool? ?? false;
            WeatherBarMode requestedMode = WeatherBarModeSettings.Read(values);
            if (StandaloneTaskbarWidgetsLifecyclePolicy.IsStandaloneDesired(
                    _standaloneTaskbarSuppressed,
                requestedMode,
                enabled,
                WeatherService.IsEnabled,
                _standaloneTaskbarCoordinator?.DiagnosticOnlyRejected ?? false))
            {
                AdvanceNativeWidgetsVerificationBackoff();
                ApplyWeatherBarPresentation(forceProbe: true);
                return;
            }

            bool standaloneSuppressionRecovery =
                StandaloneTaskbarWidgetsService.HasCapturedTaskbarEntry(values) ||
                StandaloneTaskbarWidgetsService.IsNotificationPending(values);
            if (standaloneSuppressionRecovery)
            {
                if (_standaloneTaskbarSuppressed)
                {
                    SetStandaloneWidgetsVerification(enabled: false);
                    return;
                }

                AdvanceNativeWidgetsVerificationBackoff();
                ApplyWeatherBarPresentation(forceProbe: true);
                return;
            }

            if (_standaloneTaskbarSuppressed)
            {
                StopNativeWidgetsVerification();
                return;
            }

            if (!enabled || requestedMode != WeatherBarMode.WindowsWidgets)
            {
                if (WindowsWidgetsService.HasCapturedTaskbarEntry(values))
                {
                    bool restored = WindowsWidgetsService.TryRestoreTaskbarEntry(
                        values,
                        out string restoreDetail);
                    _nativeWidgetsModeActive = !restored;
                    _weatherBarModeRuntimeDetail = restoreDetail;
                    if (!restored)
                    {
                        AdvanceNativeWidgetsVerificationBackoff();
                        return;
                    }
                }

                StopNativeWidgetsVerification();
                if (requestedMode == WeatherBarMode.StandaloneTaskbar)
                    ApplyWeatherBarPresentation(forceProbe: true);
                return;
            }

            WindowsWidgetsAvailability availability = ProbeWindowsWidgetsAvailability(forceRefresh: true);
            AdvanceNativeWidgetsVerificationBackoff();
            if (availability.NativeEntryPointPresent)
            {
                CompleteNativeWidgetsActivation(availability.Detail);
                return;
            }

            if (availability.IsAvailable && !_nativeWidgetsModeActive)
            {
                _nativeWidgetsModeActive = WindowsWidgetsService.TryEnableTaskbarEntry(
                    values,
                    out string requestDetail);
                _weatherBarModeRuntimeDetail = requestDetail;
            }
            else
            {
                _weatherBarModeRuntimeDetail = availability.IsAvailable
                    ? "Waiting for Explorer to create the native Windows Widgets taskbar entry."
                    : availability.Detail;
            }

            _nativeWidgetsActivationFailed = true;

            if (!WeatherService.IsEnabled)
                _ = DisposeWeatherBar();
        }

        private void AdvanceNativeWidgetsVerificationBackoff()
        {
            _nativeWidgetsVerificationAttempts++;
            if (_nativeWidgetsVerificationAttempts >= NativeWidgetsFastVerificationAttempts &&
                _nativeWidgetsVerificationTimer != null)
            {
                _nativeWidgetsVerificationTimer.Interval = NativeWidgetsBackgroundRetryInterval;
            }
        }

        private void EnsureTaskFlyoutWeatherBar(bool force = false)
        {
            if (!force && !ShouldWeatherBarBeEnabled())
            {
                DisposeWeatherBar();
                StopWeatherBarWatchdog();
                return;
            }

            if (MyWeatherBar == null || !MyWeatherBar.IsAlive())
            {
                if (!DisposeWeatherBar()) return;
                MyWeatherBar = new WeatherBarWindow();
                ApplyConfiguredThemeToOpenWindows();
                MyWeatherBar.ShowBar();
            }

            StartWeatherBarWatchdog();
        }

        // The weather bar is reparented as a taskbar child, so an Explorer restart
        // destroys its native window and it silently disappears — and any later call into the
        // dead window (e.g. toggling "match taskbar") crashes natively. This watchdog detects
        // the dead/missing bar and rebuilds a fresh one once the taskbar is back.
        private void StartWeatherBarWatchdog()
        {
            if (_weatherBarWatchdog == null)
            {
                _weatherBarWatchdog = MainDispatcherQueue.CreateTimer();
                _weatherBarWatchdog.Interval = NormalWeatherBarWatchdogInterval;
                _weatherBarWatchdog.Tick += (_, _) => CheckWeatherBarAlive();
                _weatherBarWatchdog.Start();
            }
            EnsureTaskbarRestartListener();
        }

        private void EnsureTaskbarRestartListener()
        {
            if (_isExiting) return;
            if (_taskbarMessageWindow != null)
            {
                StopTaskbarRestartListenerRetry();
                return;
            }

            try
            {
                H.NotifyIcon.Core.MessageWindow? messageWindow = _trayIcon?.TrayIcon.MessageWindow;
                if (messageWindow == null)
                {
                    StartTaskbarRestartListenerRetry();
                    return;
                }

                messageWindow.TaskbarCreated += TaskbarMessageWindow_TaskbarCreated;
                _taskbarMessageWindow = messageWindow;
                StopTaskbarRestartListenerRetry();
            }
            catch (Exception ex)
            {
                _taskbarMessageWindow = null;
                StartTaskbarRestartListenerRetry();
                System.Diagnostics.Debug.WriteLine($"Taskbar restart listener failed: {ex.Message}");
            }
        }

        private bool ShouldKeepTaskbarRestartListener()
        {
            if (_isExiting) return false;
            if (_weatherBarWatchdog != null ||
                _nativeWidgetsVerificationTimer != null ||
                _nativeWidgetsModeActive)
            {
                return true;
            }

            StandaloneTaskbarCoordinator? coordinator =
                _standaloneTaskbarCoordinator;
            if (coordinator != null &&
                (coordinator.IsRequested || coordinator.RequiresStop))
            {
                return true;
            }

            try
            {
                var values = ApplicationData.Current.LocalSettings.Values;
                if (StandaloneTaskbarWidgetsService.HasCapturedTaskbarEntry(values) ||
                    StandaloneTaskbarWidgetsService.IsNotificationPending(values))
                {
                    return true;
                }
                bool enabled = values["WeatherBarEnabled"] as bool? ?? false;
                WeatherBarMode mode = WeatherBarModeSettings.Read(values);
                return enabled && mode is
                    WeatherBarMode.WindowsWidgets or
                    WeatherBarMode.StandaloneTaskbar;
            }
            catch
            {
                return false;
            }
        }

        private void ReconcileTaskbarRestartListener()
        {
            if (ShouldKeepTaskbarRestartListener())
            {
                EnsureTaskbarRestartListener();
                return;
            }

            StopTaskbarRestartListenerRetry();
            if (_taskbarMessageWindow != null)
            {
                _taskbarMessageWindow.TaskbarCreated -=
                    TaskbarMessageWindow_TaskbarCreated;
                _taskbarMessageWindow = null;
            }
        }

        private void StartTaskbarRestartListenerRetry()
        {
            if (_isExiting ||
                _taskbarRestartListenerRetryTimer != null ||
                !ShouldKeepTaskbarRestartListener())
            {
                return;
            }

            _taskbarRestartListenerRetryTimer = MainDispatcherQueue.CreateTimer();
            _taskbarRestartListenerRetryTimer.Interval =
                TaskbarRestartListenerRetryInterval;
            _taskbarRestartListenerRetryTimer.Tick += (_, _) =>
            {
                if (!ShouldKeepTaskbarRestartListener())
                {
                    ReconcileTaskbarRestartListener();
                    return;
                }
                EnsureTaskbarRestartListener();
            };
            _taskbarRestartListenerRetryTimer.Start();
        }

        private void StopTaskbarRestartListenerRetry()
        {
            _taskbarRestartListenerRetryTimer?.Stop();
            _taskbarRestartListenerRetryTimer = null;
        }

        private void TaskbarMessageWindow_TaskbarCreated(object? sender, EventArgs e)
        {
            MainDispatcherQueue.TryEnqueue(() =>
            {
                if (_isExiting) return;

                WeatherBarModeResolution resolution = ResolveWeatherBarMode(forceProbe: true);
                StandaloneTaskbarCoordinator? standaloneCoordinator =
                    _standaloneTaskbarCoordinator;
                var values = ApplicationData.Current.LocalSettings.Values;
                WeatherBarMode requestedMode = WeatherBarModeSettings.Read(
                    values);
                if (requestedMode == WeatherBarMode.StandaloneTaskbar)
                {
                    // Taskbar recreation is an explicit recovery opportunity for
                    // suppression, an earlier pipe/binary failure, and an already
                    // requested controller. Apply first so the recreated native
                    // Widgets entry cannot race the host refresh for the left slot.
                    if (standaloneCoordinator?.IsRequested == true)
                        _standaloneTaskbarExplorerRecoveryPending = true;
                    ApplyWeatherBarPresentation(forceProbe: true);
                }
                else if (StandaloneTaskbarWidgetsService
                             .HasCapturedTaskbarEntry(values) ||
                         StandaloneTaskbarWidgetsService
                             .IsNotificationPending(values))
                {
                    ApplyWeatherBarPresentation(forceProbe: true);
                    return;
                }
                else if (standaloneCoordinator != null &&
                    (standaloneCoordinator.IsRequested || standaloneCoordinator.RequiresStop))
                {
                    Task recovery = standaloneCoordinator.RefreshAsync();
                    QueueWeatherPresentationAfterStandaloneTransition(recovery);
                }

                if (resolution.ShouldUseWindowsWidgets)
                {
                    ApplyWeatherBarPresentation(forceProbe: true);
                    return;
                }

                if (!resolution.ShouldRunTaskFlyoutBar) return;

                // This is a new shell generation, so an earlier reconstruction must not
                // delay recovery for the newly created taskbar.
                _lastWeatherBarRecreationTimestamp = 0;
                SetWeatherBarRecoveryPolling(enabled: true);
                CheckWeatherBarAlive();
            });
        }

        private void SetWeatherBarRecoveryPolling(bool enabled)
        {
            if (_weatherBarWatchdog == null) return;

            TimeSpan desired = enabled
                ? RecoveryWeatherBarWatchdogInterval
                : NormalWeatherBarWatchdogInterval;
            if (_weatherBarWatchdog.Interval != desired)
                _weatherBarWatchdog.Interval = desired;
        }

        private void CheckWeatherBarAlive()
        {
            try
            {
                WeatherBarModeResolution modeResolution = ResolveWeatherBarMode();
                if (modeResolution.ShouldUseWindowsWidgets && !_nativeWidgetsActivationFailed)
                {
                    ApplyWeatherBarPresentation();
                    return;
                }
                if (modeResolution.ShouldUseStandaloneTaskbar)
                {
                    if (!DisposeWeatherBar())
                    {
                        SetWeatherBarRecoveryPolling(enabled: true);
                        return;
                    }

                    StopWeatherBarWatchdog();
                    EnsureTaskbarRestartListener();
                    return;
                }

                bool enabled = ShouldWeatherBarBeEnabled();
                bool taskbarAvailable = enabled && GetSupportedTaskbarWindow() != IntPtr.Zero;
                WeatherBarWindow? bar = MyWeatherBar;
                bool barExists = bar != null;
                bool barAlive = bar?.IsAlive() == true;
                bool attached = barAlive && bar!.IsAttachedToCurrentTaskbar();
                TimeSpan elapsedSinceRecreation = _lastWeatherBarRecreationTimestamp == 0
                    ? TimeSpan.MaxValue
                    : System.Diagnostics.Stopwatch.GetElapsedTime(_lastWeatherBarRecreationTimestamp);

                WeatherBarRecoveryAction action = WeatherBarRecoveryPolicy.Decide(
                    enabled,
                    taskbarAvailable,
                    barExists,
                    barAlive,
                    attached,
                    reattachAttempted: false,
                    elapsedSinceRecreation,
                    WeatherBarRecreationCooldown);

                if (action == WeatherBarRecoveryAction.Stop)
                {
                    StopWeatherBarWatchdog();
                    return;
                }

                if (action == WeatherBarRecoveryAction.WaitForTaskbar ||
                    action == WeatherBarRecoveryAction.Cooldown)
                {
                    SetWeatherBarRecoveryPolling(enabled: true);
                    return;
                }

                if (action == WeatherBarRecoveryAction.DiscardAndWaitForTaskbar)
                {
                    if (!DisposeWeatherBar())
                    {
                        SetWeatherBarRecoveryPolling(enabled: true);
                        return;
                    }
                    SetWeatherBarRecoveryPolling(enabled: true);
                    return;
                }

                if (action == WeatherBarRecoveryAction.Healthy)
                {
                    MarkWeatherBarHealthy();
                    return;
                }

                if (action == WeatherBarRecoveryAction.Reattach)
                {
                    SetWeatherBarRecoveryPolling(enabled: true);
                    bar!.ForceReattach();
                    attached = bar.IsAttachedToCurrentTaskbar();
                    action = WeatherBarRecoveryPolicy.Decide(
                        enabled,
                        taskbarAvailable,
                        barExists: true,
                        barAlive: bar.IsAlive(),
                        attachedToCurrentTaskbar: attached,
                        reattachAttempted: true,
                        elapsedSinceRecreation,
                        WeatherBarRecreationCooldown);
                    if (action == WeatherBarRecoveryAction.Healthy)
                    {
                        MarkWeatherBarHealthy();
                        return;
                    }
                    if (action != WeatherBarRecoveryAction.Recreate)
                        return;
                }

                SetWeatherBarRecoveryPolling(enabled: true);
                if (!DisposeWeatherBar()) return;

                _lastWeatherBarRecreationTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
                MyWeatherBar = new WeatherBarWindow();
                ApplyConfiguredThemeToOpenWindows();
                MyWeatherBar.ShowBar();

                if (MyWeatherBar.IsAttachedToCurrentTaskbar())
                    MarkWeatherBarHealthy();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WeatherBar watchdog failed: {ex.Message}");
            }
        }

        private void MarkWeatherBarHealthy()
        {
            SetWeatherBarRecoveryPolling(enabled: false);
        }

        private static IntPtr GetSupportedTaskbarWindow()
        {
            IntPtr primary = FindWindow(TaskbarSelectionPolicy.PrimaryClass, null);
            return primary != IntPtr.Zero
                ? primary
                : FindWindow(TaskbarSelectionPolicy.SecondaryClass, null);
        }

        public static WeatherBarDiagnostics GetWeatherBarDiagnostics()
            => MyWeatherBar?.Diagnostics ?? WeatherBarDiagnostics.Unavailable();

        public static void ReattachWeatherBar()
        {
            MainDispatcherQueue.TryEnqueue(() =>
            {
                if (MyWeatherBar != null && MyWeatherBar.IsAlive())
                {
                    MyWeatherBar.ForceReattach();
                    return;
                }

                if (Current is App app)
                    app.CheckWeatherBarAlive();
            });
        }

        public static void ToggleWeatherBar(bool enabled)
        {
            MainDispatcherQueue.TryEnqueue(async () =>
            {
                Windows.Storage.ApplicationData.Current.LocalSettings.Values["WeatherBarEnabled"] = enabled;
                if (Current is not App app) return;

                app._lastWeatherBarRecreationTimestamp = 0;
                app.ApplyWeatherBarPresentation(forceProbe: true);

                WeatherBarWindow? bar = MyWeatherBar;
                if (enabled && bar?.IsAlive() == true && bar.IsAttachedToCurrentTaskbar())
                    await bar.RefreshWeatherAsync();
            });
        }

        internal static WeatherBarMode GetWeatherBarMode()
        {
            if (Current is not App)
                return WeatherBarMode.TaskFlyout;

            return WeatherBarModeSettings.ReadAndMigrate(
                ApplicationData.Current.LocalSettings.Values);
        }

        internal static WindowsWidgetsAvailability GetWindowsWidgetsAvailability(bool forceRefresh = false)
        {
            if (Current is App app)
                return app.ProbeWindowsWidgetsAvailability(forceRefresh);

            return WindowsWidgetsService.Detect();
        }

        internal static string GetWeatherBarModeRuntimeDetail()
            => Current is App app ? app._weatherBarModeRuntimeDetail : string.Empty;

        internal static StandaloneTaskbarDiagnostics GetStandaloneTaskbarDiagnostics()
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            bool captured =
                StandaloneTaskbarWidgetsService.HasCapturedTaskbarEntry(values);
            bool applied = false;
            bool snapshotValid = captured &&
                StandaloneTaskbarWidgetsService.TryReadCapturedTaskbarEntry(
                    values,
                    out _,
                    out applied);

            if (Current is not App app)
            {
                return new StandaloneTaskbarDiagnostics(
                    new StandaloneTaskbarRuntimeStatus(
                        StandaloneTaskbarRuntimeState.Disabled),
                    ControllerRequested: false,
                    ControllerCleanupPending:
                        StandaloneTaskbarCleanupSettings.IsPending(values),
                    CompanionPipeActive: false,
                    WidgetsSuppressionReady: false,
                    WidgetsSnapshotCaptured: captured,
                    WidgetsSnapshotValid: snapshotValid,
                    WidgetsSnapshotApplied: applied,
                    WidgetsNotificationPending:
                        StandaloneTaskbarWidgetsService.IsNotificationPending(values),
                    NativeWidgetsEntryPresent: false);
            }

            StandaloneTaskbarCoordinator? coordinator =
                app._standaloneTaskbarCoordinator;
            return new StandaloneTaskbarDiagnostics(
                coordinator?.Status ?? new StandaloneTaskbarRuntimeStatus(
                    StandaloneTaskbarRuntimeState.Disabled),
                ControllerRequested: coordinator?.IsRequested ?? false,
                ControllerCleanupPending:
                    (coordinator?.RequiresStop ?? false) ||
                    StandaloneTaskbarCleanupSettings.IsPending(values),
                CompanionPipeActive: app._weatherCompanionBridgeEnabled,
                WidgetsSuppressionReady:
                    app._standaloneWidgetsSuppressionReady,
                WidgetsSnapshotCaptured: captured,
                WidgetsSnapshotValid: snapshotValid,
                WidgetsSnapshotApplied: applied,
                WidgetsNotificationPending:
                    StandaloneTaskbarWidgetsService.IsNotificationPending(values),
                NativeWidgetsEntryPresent:
                    app.ProbeWindowsWidgetsAvailability().NativeEntryPointPresent);
        }

        internal static StandaloneTaskbarRuntimeStatus GetStandaloneTaskbarRuntimeStatus()
            => Current is App app && app._standaloneTaskbarCoordinator != null
                ? app._standaloneTaskbarCoordinator.Status
                : new StandaloneTaskbarRuntimeStatus(
                    StandaloneTaskbarRuntimeState.Disabled);

        internal static bool IsWeatherCompanionBridgeActive()
            => Current is App app && app._weatherCompanionBridgeEnabled;

        private static void NotifyWeatherBarModeStatusChanged()
        {
            EventHandler? handlers = WeatherBarModeStatusChanged;
            if (handlers == null) return;

            foreach (EventHandler handler in handlers.GetInvocationList())
            {
                try { handler(Current, EventArgs.Empty); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Weather bar status subscriber failed: {ex.Message}");
                }
            }
        }

        internal static void SetWeatherBarMode(WeatherBarMode mode)
        {
            WeatherBarModeSettings.Write(
                ApplicationData.Current.LocalSettings.Values,
                mode);
            MainDispatcherQueue.TryEnqueue(() =>
            {
                if (Current is not App app) return;
                app.ApplyWeatherBarPresentation(forceProbe: true);
            });
        }

        internal static bool GetWeatherCompanionBridgeEnabled()
            => ApplicationData.Current.LocalSettings.Values[
                WeatherCompanionBridgeEnabledSettingKey] as bool? ?? false;

        internal static void SetWeatherCompanionBridgeEnabled(bool enabled)
        {
            ApplicationData.Current.LocalSettings.Values[
                WeatherCompanionBridgeEnabledSettingKey] = enabled;
            MainDispatcherQueue.TryEnqueue(() =>
            {
                if (Current is App app)
                    app.ApplyWeatherBarPresentation(forceProbe: true);
            });
        }

        public static async Task<bool> OpenWindowsWidgetsSettingsAsync()
        {
            try
            {
                return await Windows.System.Launcher.LaunchUriAsync(
                    new Uri("ms-settings:taskbar"));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Opening taskbar settings failed: {ex.Message}");
                return false;
            }
        }

        public static async Task<bool> OpenWindowsWidgetsBoardAsync()
        {
            foreach (string protocol in new[] { "ms-widgets:", "ms-widgetboard:" })
            {
                try
                {
                    var uri = new Uri(protocol);
                    Windows.System.LaunchQuerySupportStatus support =
                        await Windows.System.Launcher.QueryUriSupportAsync(
                            uri,
                            Windows.System.LaunchQuerySupportType.Uri);
                    if (support == Windows.System.LaunchQuerySupportStatus.Available &&
                        await Windows.System.Launcher.LaunchUriAsync(uri))
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Opening Windows Widgets with {protocol} failed: {ex.Message}");
                }
            }

            return false;
        }

        public static async Task<bool> OpenWindowsWidgetsStoreAsync()
        {
            try
            {
                return await Windows.System.Launcher.LaunchUriAsync(
                    new Uri("ms-windows-store://pdp/?ProductId=9MSSGKG348SP"));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Opening Windows Web Experience Pack failed: {ex.Message}");
                return false;
            }
        }

        private void StopWeatherBarWatchdog()
        {
            _weatherBarWatchdog?.Stop();
            _weatherBarWatchdog = null;
            _lastWeatherBarRecreationTimestamp = 0;
            ReconcileTaskbarRestartListener();
        }

        public static void RefreshWeatherBar(bool forceRefresh = false)
        {
            MainDispatcherQueue.TryEnqueue(async () =>
            {
                if (Current is not App app) return;
                if (app._isExiting) return;
                app.UpdateWeatherCompanionBridge(
                    ApplicationData.Current.LocalSettings.Values,
                    WeatherBarModeSettings.Read(ApplicationData.Current.LocalSettings.Values));
                app._weatherCompanionCoordinator?.RequestRefresh(forceRefresh);
                if (!app.ShouldWeatherBarBeEnabled()) return;

                app.CheckWeatherBarAlive();
                WeatherBarWindow? bar = MyWeatherBar;
                if (bar?.IsAlive() == true && bar.IsAttachedToCurrentTaskbar())
                    await bar.RefreshWeatherAsync(forceRefresh);
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

        private volatile bool _isExiting;

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
            WeatherService.LocationUpdated -= OnWeatherLocationUpdated;

            StandaloneTaskbarCoordinator? standaloneTaskbar =
                _standaloneTaskbarCoordinator;
            _standaloneTaskbarCoordinator = null;
            _standaloneTaskbarHandoffTask = null;
            if (standaloneTaskbar != null)
            {
                standaloneTaskbar.StatusChanged -=
                    StandaloneTaskbarCoordinator_StatusChanged;
                try
                {
                    await standaloneTaskbar.DisposeAsync().AsTask().WaitAsync(
                        StandaloneTaskbarCoordinator.ShutdownTimeout +
                        TimeSpan.FromSeconds(1));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Standalone taskbar shutdown failed: {ex.Message}");
                }

                if (!standaloneTaskbar.RequiresStop)
                {
                    try
                    {
                        StandaloneTaskbarCleanupSettings.Clear(
                            ApplicationData.Current.LocalSettings.Values);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"Clearing standalone taskbar cleanup lease during exit failed: {ex.Message}");
                    }
                }
            }

            try
            {
                var values = ApplicationData.Current.LocalSettings.Values;
                bool controllerCleanupPending = standaloneTaskbar != null
                    ? standaloneTaskbar.RequiresStop
                    : StandaloneTaskbarCleanupSettings.IsPending(values);
                if (!_standaloneTaskbarSuppressed &&
                    !controllerCleanupPending &&
                    (StandaloneTaskbarWidgetsService
                         .HasCapturedTaskbarEntry(values) ||
                     StandaloneTaskbarWidgetsService
                         .IsNotificationPending(values)))
                {
                    StandaloneTaskbarWidgetsResult restore =
                        StandaloneTaskbarWidgetsService.TryRestore(values);
                    if (!restore.Succeeded || restore.NotificationPending)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"Standalone Widgets restore on exit: {restore.DiagnosticKey}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Standalone Widgets restore during exit failed: {ex.Message}");
            }

            WeatherCompanionCoordinator? weatherCompanion = _weatherCompanionCoordinator;
            _weatherCompanionCoordinator = null;
            _weatherCompanionBridgeEnabled = false;
            if (weatherCompanion != null)
            {
                try
                {
                    await weatherCompanion.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Weather companion shutdown failed: {ex.Message}");
                }
            }

            StopNativeWidgetsVerification();
            StopWeatherBarWatchdog();
            _backgroundRefresh?.Stop();
            NotificationService?.Stop();
            MailService.StopMailPolling();
            MailService.StopPendingMutationRetryScheduler();
            MemoryDiagnostics.Stop();
            Windows.System.MemoryManager.AppMemoryUsageIncreased -= MemoryManager_AppMemoryUsageIncreased;
            await PerformanceDiagnostics.FlushAsync();
            MailService.NewMailArrived -= MailService_NewMailArrived;
            await FlushPendingSavesBeforeExitAsync();
            if (_uiSettings != null) _uiSettings.ColorValuesChanged -= UiSettings_ColorValuesChanged;
            _trayIcon?.Dispose();
            MyWeatherBar?.DetachForRecovery();
            MyFlyoutWindow?.Shutdown();
            MyFlyoutWindow = null;
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
