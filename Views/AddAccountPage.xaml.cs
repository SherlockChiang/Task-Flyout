using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
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

        private void AddAccountScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double padding = ResponsiveLayoutPolicy.GetPagePadding(e.NewSize.Width, e.NewSize.Height);
            AddAccountLayoutRoot.Padding = new Thickness(padding);
            AddAccountContent.Spacing = ResponsiveLayoutPolicy.GetPageSectionSpacing(
                e.NewSize.Width,
                e.NewSize.Height);
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
            BtnICloud.IsEnabled = true;

            if (mgr.IsConnected("Google"))
                BtnGoogle.Content = CreateDisabledContent("Google", "#EA4335",
                    _loader.GetStringOrDefault("AddAccount_AddAnotherGoogle") ?? "Add another Google account");
            if (mgr.IsConnected("Microsoft"))
                BtnMicrosoft.Content = CreateDisabledContent("Microsoft", "#0078D4",
                    _loader.GetStringOrDefault("AddAccount_Reconnect") ?? "Reconnect all Microsoft features");
            if (mgr.IsConnected("iCloud"))
                BtnICloud.Content = CreateDisabledContent("iCloud", "#6E6E73",
                    _loader.GetStringOrDefault("AddAccount_ReconnectICloud") ?? "Reconnect iCloud Calendar");

            UpdateChecklist();
        }

        private void UpdateChecklist()
        {
            var mgr = GetAccountManager();
            if (mgr == null || ChecklistText == null) return;

            string ready = _loader.GetStringOrDefault("AddAccount_Ready") ?? "Ready";
            var google = mgr.IsConnected("Google") ? ready : (_loader.GetStringOrDefault("AddAccount_ConnectGoogle") ?? "Connect Google for Calendar, Tasks, and Gmail.");
            var microsoft = mgr.IsConnected("Microsoft") ? ready : (_loader.GetStringOrDefault("AddAccount_ConnectMicrosoft") ?? "Connect Microsoft for Outlook Calendar and To Do.");
            var iCloud = mgr.IsConnected("iCloud") ? ready : (_loader.GetStringOrDefault("AddAccount_ConnectICloud") ?? "Connect iCloud Calendar with an app-specific password.");
            var mail = App.Current is App app && app.MailService.HasSetupCompleteAccounts()
                ? ready
                : (_loader.GetStringOrDefault("AddAccount_ConnectMail") ?? "Add Gmail, Outlook, or IMAP from the Mail page.");
            var weather = App.Current is App app2 && app2.WeatherService.IsEnabled
                ? ready
                : (_loader.GetStringOrDefault("AddAccount_ConnectWeather") ?? "Enable weather and choose a city from the Weather page.");

            ChecklistText.Text = string.Format(
                _loader.GetStringOrDefault("AddAccount_ChecklistFormat") ?? "Google: {0}\nMicrosoft: {1}\niCloud: {2}\nMail: {3}\nWeather: {4}",
                google,
                microsoft,
                iCloud,
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
            foreach (var providerName in new[] { "Google", "Microsoft", "iCloud" })
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
            var inner = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, MinWidth = 0 };
            inner.Children.Add(new TextBlock { Text = name, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            inner.Children.Add(new TextBlock
            {
                 Text = status,
                 FontSize = 12,
                 TextWrapping = TextWrapping.WrapWholeWords,
                 Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray)
            });
            sp.Children.Add(inner);
            return sp;
        }

        private async void BtnGoogle_Click(object sender, RoutedEventArgs e)
        {
            await ConnectGoogleAccountAsync();
        }

        private async void BtnMicrosoft_Click(object sender, RoutedEventArgs e)
        {
            await ConnectAccountAsync("Microsoft");
        }

        private async void BtnICloud_Click(object sender, RoutedEventArgs e)
        {
            var credentials = await PromptForICloudCredentialsAsync();
            if (credentials == null) return;

            await ConnectAccountAsync("iCloud", provider =>
            {
                if (provider is not ICloudSyncProvider iCloud)
                    throw new InvalidOperationException("The iCloud calendar provider is unavailable.");
                return iCloud.ConnectWithCredentialsAsync(credentials.Value.AccountName, credentials.Value.Password);
            });
        }

        private async Task<(string AccountName, string Password)?> PromptForICloudCredentialsAsync()
        {
            var accountBox = new TextBox
            {
                Header = _loader.GetStringOrDefault("AddAccount_ICloudAccountHeader") ?? "Apple Account",
                PlaceholderText = _loader.GetStringOrDefault("AddAccount_ICloudAccountPlaceholder") ?? "name@example.com",
                MaxLength = 320
            };
            var passwordBox = new PasswordBox
            {
                Header = _loader.GetStringOrDefault("AddAccount_ICloudPasswordHeader") ?? "App-specific password",
                PasswordRevealMode = PasswordRevealMode.Peek,
                MaxLength = 256
            };
            var validationText = new TextBlock
            {
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.IndianRed),
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed
            };
            var helpButton = new HyperlinkButton
            {
                Content = _loader.GetStringOrDefault("AddAccount_ICloudPasswordHelp") ?? "Manage app-specific passwords",
                Padding = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            helpButton.Click += async (_, _) =>
                await SafeUriLauncher.TryLaunchExternalHttpUriAsync("https://account.apple.com/");

            double availableWidth = XamlRoot?.Size.Width ?? 420;
            var panel = new StackPanel
            {
                Spacing = 12,
                Width = Math.Max(160, Math.Min(360, availableWidth - 96))
            };
            panel.Children.Add(new TextBlock
            {
                Text = _loader.GetStringOrDefault("AddAccount_ICloudExplanation")
                    ?? "Use your Apple Account email and an app-specific password. Your main Apple Account password is not accepted or stored.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });
            panel.Children.Add(accountBox);
            panel.Children.Add(passwordBox);
            panel.Children.Add(helpButton);
            panel.Children.Add(validationText);

            var dialog = new ContentDialog
            {
                Title = _loader.GetStringOrDefault("AddAccount_ICloudDialogTitle") ?? "Connect iCloud Calendar",
                Content = panel,
                PrimaryButtonText = _loader.GetStringOrDefault("TextConnect") ?? "Connect",
                CloseButtonText = _loader.GetStringOrDefault("CalendarDialog.CloseButtonText") ?? "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };
            dialog.PrimaryButtonClick += (_, args) =>
            {
                try
                {
                    ICloudCalDavClient.ValidateCredentials(new ICloudCredentialEnvelope(accountBox.Text.Trim(), passwordBox.Password.Trim()));
                }
                catch (ArgumentException)
                {
                    args.Cancel = true;
                    validationText.Text = _loader.GetStringOrDefault("AddAccount_ICloudValidation")
                        ?? "Enter a valid Apple Account and app-specific password.";
                    validationText.Visibility = Visibility.Visible;
                }
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return null;

            string accountName = accountBox.Text.Trim();
            string password = passwordBox.Password.Trim();
            passwordBox.Password = "";
            return (accountName, password);
        }

        private async Task ConnectGoogleAccountAsync()
        {
            if (App.Current is not App app) return;

            AuthProgress.IsActive = true;
            StatusText.Text = _loader.GetStringOrDefault("TextAuthorizing") ?? "Authorizing...";
            BtnGoogle.IsEnabled = false;
            BtnMicrosoft.IsEnabled = false;
            BtnICloud.IsEnabled = false;

            try
            {
                var mailAccount = await app.ConnectNewGoogleAccountAsync();
                StatusText.Text = string.Format(
                    _loader.GetStringOrDefault("TextAccountAdded") ?? "Added {0}",
                    mailAccount.Subtitle);
                CompleteAccountConnection();
            }
            catch (Exception ex)
            {
                StatusText.Text = UserSafeErrorMessage.FromException(
                    ex,
                    _loader.GetStringOrDefault("TextAuthFailed") ?? "Authentication failed.");
                System.Diagnostics.Debug.WriteLine($"Google account connection failed: {ex}");
            }
            finally
            {
                AuthProgress.IsActive = false;
                UpdateButtonStates();
            }
        }

        private async Task ConnectAccountAsync(string providerName, Func<ISyncProvider, Task>? connect = null)
        {
            var mgr = GetAccountManager();
            var syncManager = GetSyncManager();
            if (mgr == null || syncManager == null) return;

            AuthProgress.IsActive = true;
            StatusText.Text = _loader.GetStringOrDefault("TextAuthorizing") ?? "Authorizing...";
            BtnGoogle.IsEnabled = false;
            BtnMicrosoft.IsEnabled = false;
            BtnICloud.IsEnabled = false;

            try
            {
                var provider = syncManager.GetProvider(providerName);
                if (provider == null) throw new Exception($"Provider {providerName} not registered");

                if (connect != null)
                    await connect(provider);
                else
                    await provider.ConnectInteractivelyAsync();

                if (App.Current is App app)
                {
                    if (providerName == "Microsoft")
                        await app.MailService.AddOutlookAccountAsync();
                }

                // Create account entry
                var account = mgr.GetAccount(providerName, provider.AccountId) ?? new ConnectedAccountInfo
                {
                    ProviderName = provider.ProviderName,
                    AccountId = provider.AccountId,
                    DisplayName = provider.AccountDisplayName
                };
                var capabilities = SyncProviderCapabilityPolicy.ForProvider(providerName);
                account.ShowEvents = capabilities.SupportsEvents;
                account.ShowTasks = capabilities.SupportsTasks;

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

                if (!mgr.IsConnected(providerName, provider.AccountId)) mgr.AddAccount(account); else mgr.Save();
                CompleteAccountConnection();
            }
            catch (Exception ex)
            {
                StatusText.Text = UserSafeErrorMessage.FromException(
                    ex,
                    _loader.GetStringOrDefault("TextAuthFailed") ?? "Authentication failed.");
                System.Diagnostics.Debug.WriteLine($"Auth failed for {providerName}: {ex}");
            }
            finally
            {
                AuthProgress.IsActive = false;
                UpdateButtonStates();
            }
        }

        private void CompleteAccountConnection()
        {
            Windows.Storage.ApplicationData.Current.LocalSettings.Values[OnboardingPolicy.CompletedVersionKey] = OnboardingPolicy.CurrentVersion;
            if (App.MyMainWindow is not MainWindow mainWin) return;

            _ = mainWin.RefreshAccountListAsync();
            mainWin.DispatcherQueue.TryEnqueue(() =>
            {
                if (Frame == null) return;

                Frame.Navigate(typeof(CalendarPage));
                mainWin.DispatcherQueue.TryEnqueue(() =>
                {
                    if (Frame?.Content is CalendarPage page) page.ForceSync();
                });
            });
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
