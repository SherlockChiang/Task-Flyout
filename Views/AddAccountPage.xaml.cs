using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using System;
using System.Linq;
using System.Collections.Generic;
using Task_Flyout.Models;
using Task_Flyout.Services;

namespace Task_Flyout.Views
{
    public sealed partial class AddAccountPage : Page
    {
        private ResourceLoader _loader = new ResourceLoader();

        public AddAccountPage()
        {
            this.InitializeComponent();
            Loaded += AddAccountPage_Loaded;
            Unloaded += AddAccountPage_Unloaded;
            OnboardingActions.Visibility = Windows.Storage.ApplicationData.Current.LocalSettings.Values[OnboardingPolicy.CompletedVersionKey] is int completed
                && completed >= OnboardingPolicy.CurrentVersion
                ? Visibility.Collapsed
                : Visibility.Visible;
            UpdateButtonStates();
            UpdateChecklist();
        }

        private void AddAccountPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (GetSyncManager() is { } syncManager)
                syncManager.ProviderHealthChanged += SyncManager_ProviderHealthChanged;
            if (App.Current is App app)
                app.TaskMutations.StateChanged += TaskMutations_StateChanged;
            UpdateChecklist();
        }

        private void AddAccountPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (GetSyncManager() is { } syncManager)
                syncManager.ProviderHealthChanged -= SyncManager_ProviderHealthChanged;
            if (App.Current is App app)
                app.TaskMutations.StateChanged -= TaskMutations_StateChanged;
        }

        private void SyncManager_ProviderHealthChanged(object? sender, EventArgs e)
            => DispatcherQueue.TryEnqueue(UpdateChecklist);

        private void TaskMutations_StateChanged(object? sender, EventArgs e)
            => DispatcherQueue.TryEnqueue(UpdateChecklist);

        private void UpdateButtonStates()
        {
            var mgr = GetAccountManager();
            if (mgr == null) return;

            BtnGoogle.IsEnabled = true;
            BtnMicrosoft.IsEnabled = true;

            if (mgr.IsConnected("Google"))
                BtnGoogle.Content = CreateDisabledContent("Google", "#EA4335",
                    _loader.GetStringOrDefault("AddAccount_Reconnect") ?? "Reconnect all Google features");
            if (mgr.IsConnected("Microsoft"))
                BtnMicrosoft.Content = CreateDisabledContent("Microsoft", "#0078D4",
                    _loader.GetStringOrDefault("AddAccount_Reconnect") ?? "Reconnect all Microsoft features");

            UpdateChecklist();
        }

        private void UpdateChecklist()
        {
            var mgr = GetAccountManager();
            if (mgr == null || ChecklistText == null) return;

            string ready = _loader.GetStringOrDefault("AddAccount_Ready") ?? "Ready";
            var google = mgr.IsConnected("Google") ? ready : (_loader.GetStringOrDefault("AddAccount_ConnectGoogle") ?? "Connect Google for Calendar, Tasks, and Gmail.");
            var microsoft = mgr.IsConnected("Microsoft") ? ready : (_loader.GetStringOrDefault("AddAccount_ConnectMicrosoft") ?? "Connect Microsoft for Outlook Calendar and To Do.");
            var mail = App.Current is App app && app.MailService.HasSetupCompleteAccounts()
                ? ready
                : (_loader.GetStringOrDefault("AddAccount_ConnectMail") ?? "Add Gmail, Outlook, or IMAP from the Mail page.");
            var weather = App.Current is App app2 && app2.WeatherService.IsEnabled
                ? ready
                : (_loader.GetStringOrDefault("AddAccount_ConnectWeather") ?? "Enable weather and choose a city from the Weather page.");

            ChecklistText.Text = string.Format(
                _loader.GetStringOrDefault("AddAccount_ChecklistFormat") ?? "Google: {0}\nMicrosoft: {1}\nMail: {2}\nWeather: {3}",
                google,
                microsoft,
                mail,
                weather);
            OpenMailSetupButton.Visibility = App.Current is App mailApp && mailApp.MailService.HasSetupCompleteAccounts() ? Visibility.Collapsed : Visibility.Visible;
            OpenWeatherSetupButton.Visibility = App.Current is App weatherApp && weatherApp.WeatherService.IsEnabled ? Visibility.Collapsed : Visibility.Visible;
            UpdateHealthText(mgr);
        }

        private void UpdateHealthText(AccountManager mgr)
        {
            if (HealthText == null || App.Current is not App app) return;

            var lines = new List<string>();
            foreach (var providerName in new[] { "Google", "Microsoft" })
            {
                if (!mgr.IsConnected(providerName)) continue;
                var health = app.SyncManager.GetProviderHealth(providerName);
                var state = health.Kind switch
                {
                    ProviderHealthKind.Syncing => _loader.GetStringOrDefault("AddAccount_HealthSyncing") ?? "Syncing",
                    ProviderHealthKind.Cached => _loader.GetStringOrDefault("AddAccount_HealthCached") ?? "Offline or unavailable; cached data is available",
                    ProviderHealthKind.ReconnectRequired => _loader.GetStringOrDefault("AddAccount_HealthReconnect") ?? "Reconnect required",
                    ProviderHealthKind.Failed => _loader.GetStringOrDefault("AddAccount_HealthFailed") ?? "Sync failed",
                    _ => _loader.GetStringOrDefault("AddAccount_HealthReady") ?? "Connected"
                };
                if (health.LastSuccessUtc.HasValue)
                    state += " · " + string.Format(_loader.GetStringOrDefault("AddAccount_LastSuccess") ?? "Last success: {0}", health.LastSuccessUtc.Value.LocalDateTime.ToString("g"));
                else if (health.HasCachedData)
                    state += " · " + (_loader.GetStringOrDefault("AddAccount_CacheAvailable") ?? "Cached data available");
                lines.Add($"{providerName}: {state}");
            }

            int taskPending = app.TaskMutations.PendingCount;
            int taskFailed = app.TaskMutations.FailedCount;
            int mailPending = app.MailService.GetPendingMutationCount();
            foreach (var account in app.MailService.GetAccounts())
            {
                var mailState = account.IsSetupComplete
                    ? _loader.GetStringOrDefault("AddAccount_HealthReady") ?? "Connected"
                    : _loader.GetStringOrDefault("AddAccount_MailSetupIncomplete") ?? "Setup incomplete";
                int accountPending = app.MailService.GetPendingMutationCount(account.Id);
                lines.Add(string.Format(
                    _loader.GetStringOrDefault("AddAccount_MailHealth") ?? "Mail {0}: {1}; pending changes {2}",
                    account.DisplayName,
                    mailState,
                    accountPending));
            }
            lines.Add(string.Format(
                _loader.GetStringOrDefault("AddAccount_PendingChanges") ?? "Pending changes: tasks {0}, failed tasks {1}, mail {2}",
                taskPending,
                taskFailed,
                mailPending));
            HealthText.Text = string.Join(Environment.NewLine, lines);
            RetrySyncButton.Visibility = mgr.Accounts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void RetrySyncButton_Click(object sender, RoutedEventArgs e)
        {
            if (GetSyncManager() is not { } syncManager) return;
            RetrySyncButton.IsEnabled = false;
            AuthProgress.IsActive = true;
            try
            {
                await syncManager.GetAllDataAsync(DateTime.Today.AddMonths(-1), DateTime.Today.AddMonths(2), forceRefresh: true);
                UpdateChecklist();
            }
            finally
            {
                AuthProgress.IsActive = false;
                RetrySyncButton.IsEnabled = true;
            }
        }

        private void OpenMailSetupButton_Click(object sender, RoutedEventArgs e)
        {
            Frame?.Navigate(typeof(MailPage));
        }

        private void OpenWeatherSetupButton_Click(object sender, RoutedEventArgs e)
        {
            Frame?.Navigate(typeof(WeatherPage));
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
            => CompleteOnboarding();

        private void FinishButton_Click(object sender, RoutedEventArgs e)
            => CompleteOnboarding();

        private void CompleteOnboarding()
        {
            Windows.Storage.ApplicationData.Current.LocalSettings.Values[OnboardingPolicy.CompletedVersionKey] = OnboardingPolicy.CurrentVersion;
            Frame?.Navigate(typeof(CalendarPage));
        }

        private StackPanel CreateDisabledContent(string name, string color, string status)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            sp.Children.Add(new FontIcon
            {
                Glyph = "\uE77B",
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.Gray),
                FontSize = 24
            });
            var inner = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            inner.Children.Add(new TextBlock { Text = name, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            inner.Children.Add(new TextBlock
            {
                Text = status,
                FontSize = 12,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray)
            });
            sp.Children.Add(inner);
            return sp;
        }

        private async void BtnGoogle_Click(object sender, RoutedEventArgs e)
        {
            await ConnectAccountAsync("Google");
        }

        private async void BtnMicrosoft_Click(object sender, RoutedEventArgs e)
        {
            await ConnectAccountAsync("Microsoft");
        }

        private async System.Threading.Tasks.Task ConnectAccountAsync(string providerName)
        {
            var mgr = GetAccountManager();
            var syncManager = GetSyncManager();
            if (mgr == null || syncManager == null) return;

            AuthProgress.IsActive = true;
            StatusText.Text = _loader.GetStringOrDefault("TextAuthorizing") ?? "Authorizing...";
            BtnGoogle.IsEnabled = false;
            BtnMicrosoft.IsEnabled = false;

            try
            {
                var provider = syncManager.Providers.FirstOrDefault(p => p.ProviderName == providerName);
                if (provider == null) throw new Exception($"Provider {providerName} not registered");

                await provider.ConnectInteractivelyAsync();

                if (App.Current is App app)
                {
                    if (providerName == "Google")
                        await app.MailService.AddGoogleAccountAsync();
                    else if (providerName == "Microsoft")
                        await app.MailService.AddOutlookAccountAsync();
                }

                // Create account entry
                var account = mgr.GetAccount(providerName) ?? new ConnectedAccountInfo { ProviderName = providerName };

                // Fetch subscribed calendars
                try
                {
                    var calendars = await provider.FetchCalendarListAsync();
                    var visibility = account.Calendars.ToDictionary(calendar => calendar.Id, calendar => calendar.IsVisible);
                    account.Calendars.Clear();
                    foreach (var cal in calendars)
                    {
                        if (visibility.TryGetValue(cal.Id, out bool isVisible)) cal.IsVisible = isVisible;
                        account.Calendars.Add(cal);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to fetch calendar list for {providerName}: {ex.Message}");
                }

                if (!mgr.IsConnected(providerName)) mgr.AddAccount(account); else mgr.Save();
                Windows.Storage.ApplicationData.Current.LocalSettings.Values[OnboardingPolicy.CompletedVersionKey] = OnboardingPolicy.CurrentVersion;

                // Refresh the MainWindow pane
                if (App.MyMainWindow is MainWindow mainWin)
                {
                    _ = mainWin.RefreshAccountListAsync();

                    // Navigate back to calendar
                    var frame = mainWin.Content is Grid grid
                        ? grid.FindName("ContentFrame") as Frame
                        : null;

                    // Use the MainWindow's navigation
                    mainWin.DispatcherQueue.TryEnqueue(() =>
                    {
                        if (this.Frame != null)
                        {
                            this.Frame.Navigate(typeof(CalendarPage));
                            // Force sync to load data from new account
                            mainWin.DispatcherQueue.TryEnqueue(() =>
                            {
                                if (this.Frame?.Content is CalendarPage page) page.ForceSync();
                            });
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = _loader.GetStringOrDefault("TextAuthFailed") ?? "Auth Failed";
                System.Diagnostics.Debug.WriteLine($"Auth failed for {providerName}: {ex}");
            }
            finally
            {
                AuthProgress.IsActive = false;
                UpdateButtonStates();
            }
        }

        private static AccountManager? GetAccountManager()
        {
            if (App.Current is App app) return app.SyncManager.AccountManager;
            return null;
        }

        private static SyncManager? GetSyncManager()
        {
            if (App.Current is App app) return app.SyncManager;
            return null;
        }
    }
}
