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
        private bool _isActive;
        private bool _authorizationBusy;
        private readonly HashSet<string> _retrying = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (StackPanel Root, TextBlock Title, ContentState Status, Button Retry, Button Reconnect)> _healthRows = new(StringComparer.Ordinal);

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
            _isActive = true;
            if (GetSyncManager() is { } syncManager)
                syncManager.ProviderHealthChanged += SyncManager_ProviderHealthChanged;
            if (App.Current is App app)
            {
                app.TaskMutations.StateChanged += TaskMutations_StateChanged;
                app.MailService.SyncHealthChanged += SyncManager_ProviderHealthChanged;
                app.MailService.AccountsChanged += SyncManager_ProviderHealthChanged;
                app.MailService.CachePublished += MailService_CachePublished;
            }
            UpdateChecklist();
        }

        private void AddAccountPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _isActive = false;
            if (GetSyncManager() is { } syncManager)
                syncManager.ProviderHealthChanged -= SyncManager_ProviderHealthChanged;
            if (App.Current is App app)
            {
                app.TaskMutations.StateChanged -= TaskMutations_StateChanged;
                app.MailService.SyncHealthChanged -= SyncManager_ProviderHealthChanged;
                app.MailService.AccountsChanged -= SyncManager_ProviderHealthChanged;
                app.MailService.CachePublished -= MailService_CachePublished;
            }
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
            => QueueHealthUpdate();

        private void TaskMutations_StateChanged(object? sender, EventArgs e)
            => QueueHealthUpdate();

        private void MailService_CachePublished(object? sender, MailCachePublishedEventArgs e) => QueueHealthUpdate();

        private void QueueHealthUpdate() => DispatcherQueue.TryEnqueue(() =>
        {
            if (_isActive) UpdateChecklist();
        });

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

            var currentKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var account in mgr.Accounts.ToList())
            {
                var health = app.SyncManager.GetProviderHealth(
                    account.ProviderName,
                    account.AccountId);
                var counts = app.TaskMutations.GetAccountCounts(health.ProviderKey);
                string key = "agenda:" + health.ProviderKey;
                currentKeys.Add(key);
                UpdateHealthRow(key, account.DisplayTitle + " · " + account.ProviderName,
                    FormatProviderHealthState(health) + Environment.NewLine + string.Format(
                        _loader.GetStringOrDefault("SyncCenter_TaskChanges") ?? "Pending uploads: {0}; failed changes: {1}. Retry failed edits from their original task view.", counts.Pending, counts.Failed),
                    account.AccountId, false, health.Kind == ProviderHealthKind.Syncing,
                    health.Kind is ProviderHealthKind.Cached or ProviderHealthKind.Failed or ProviderHealthKind.ReconnectRequired, health.Kind == ProviderHealthKind.ReconnectRequired);
            }

            var lines = new List<string>();
            int taskPending = app.TaskMutations.PendingCount;
            int taskFailed = app.TaskMutations.FailedCount;
            int mailPending = app.MailService.GetPendingMutationCount();
            foreach (var account in app.MailService.GetAccounts())
            {
                var health = app.MailService.GetSyncHealth(account.Id);
                string mailState = !account.IsSetupComplete
                    ? _loader.GetStringOrDefault("AddAccount_MailSetupIncomplete") ?? "Setup incomplete"
                    : health.State switch
                    {
                        AccountSyncState.Syncing => _loader.GetStringOrDefault("AddAccount_HealthSyncing") ?? "Syncing",
                        AccountSyncState.Failed => _loader.GetStringOrDefault("AddAccount_HealthFailed") ?? "Sync failed",
                        AccountSyncState.Canceled => _loader.GetStringOrDefault("SyncCenter_Canceled") ?? "Sync canceled",
                        _ => _loader.GetStringOrDefault("AddAccount_HealthReady") ?? "Connected"
                    };
                int accountPending = app.MailService.GetPendingMutationCount(account.Id);
                string state = string.Format(
                    _loader.GetStringOrDefault("AddAccount_MailHealth") ?? "Mail {0}: {1}; pending changes {2}",
                    account.DisplayTitle,
                    mailState,
                    accountPending);
                string key = "mail:" + account.Id;
                currentKeys.Add(key);
                UpdateHealthRow(key, account.DisplayTitle + " · " + account.ProviderName,
                    state + Environment.NewLine + FormatSyncObservation(health.LastAttemptUtc, health.LastSuccessUtc, health.Failure),
                    account.Id, true, health.State == AccountSyncState.Syncing,
                    !account.IsSetupComplete || health.State == AccountSyncState.Failed,
                    !account.IsSetupComplete || health.Failure == SyncFailureKind.Authorization, !account.IsSetupComplete);
            }
            foreach (string key in _healthRows.Keys.Where(key => !currentKeys.Contains(key)).ToList())
            {
                ConnectedAccountActions.Children.Remove(_healthRows[key].Root);
                _healthRows.Remove(key);
            }
            ConnectedAccountsTitle.Visibility = currentKeys.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ConnectedAccountActions.Visibility = ConnectedAccountsTitle.Visibility;
            lines.Add(string.Format(
                _loader.GetStringOrDefault("AddAccount_PendingChanges") ?? "Pending changes: tasks {0}, failed tasks {1}, mail {2}",
                taskPending,
                taskFailed,
                mailPending));
            lines.Add(_loader.GetStringOrDefault("SyncCenter_ScopeHint") ?? "Times describe successful refreshes in this session. Mail retry refreshes folders and Inbox; queued mail changes retry automatically. Task counts cover tracked edits in this session.");
            HealthText.Text = string.Join(Environment.NewLine, lines);
            RetrySyncButton.Visibility = mgr.Accounts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (currentKeys.Count == 0)
                SyncLoadState.SetEmpty(ContentState.Text("ContentState_NoAccounts", "No connected accounts."),
                    ContentState.Text("ContentState_AccountSetupHint", "Choose a provider below, or open Mail setup to connect a mailbox."));
            else if (!SyncLoadState.IsError && !SyncLoadState.IsLoading) SyncLoadState.Clear();
        }

        private string FormatProviderHealthState(ProviderHealthSnapshot health)
        {
            string state = health.Kind switch
            {
                ProviderHealthKind.Syncing => _loader.GetStringOrDefault("AddAccount_HealthSyncing") ?? "Syncing",
                ProviderHealthKind.Cached => _loader.GetStringOrDefault("AddAccount_HealthCached") ?? "Offline or unavailable; cached data is available",
                ProviderHealthKind.ReconnectRequired => _loader.GetStringOrDefault("AddAccount_HealthReconnect") ?? "Reconnect required",
                ProviderHealthKind.Failed => _loader.GetStringOrDefault("AddAccount_HealthFailed") ?? "Sync failed",
                _ => _loader.GetStringOrDefault("AddAccount_HealthReady") ?? "Connected"
            };
            if (health.HasCachedData && health.Kind != ProviderHealthKind.Ready)
            {
                state += " · " + (_loader.GetStringOrDefault("AddAccount_CacheAvailable") ?? "Cached data available");
            }
            return state + Environment.NewLine + FormatSyncObservation(health.LastAttemptUtc, health.LastSuccessUtc, health.Failure);
        }

        private string FormatSyncObservation(DateTimeOffset? attempt, DateTimeOffset? success, SyncFailureKind failure)
        {
            string text = success.HasValue ? string.Format(
                _loader.GetStringOrDefault("AddAccount_LastSuccess") ?? "Last success: {0}", success.Value.LocalDateTime.ToString("g"))
                : _loader.GetStringOrDefault("SyncCenter_NotSynced") ?? "No successful sync in this session";
            if (attempt.HasValue) text += " · " + string.Format(
                _loader.GetStringOrDefault("SyncCenter_LastAttempt") ?? "Last attempt: {0}", attempt.Value.LocalDateTime.ToString("g"));
            if (failure != SyncFailureKind.None) text += Environment.NewLine + FailureText(failure);
            return text;
        }

        private string FailureText(SyncFailureKind failure) => failure switch
        {
            SyncFailureKind.Authorization => _loader.GetStringOrDefault("SyncCenter_AuthFailure") ?? "Authorization expired or access was denied. Reconnect this account.",
            SyncFailureKind.Network => _loader.GetStringOrDefault("SyncCenter_NetworkFailure") ?? "The service could not be reached. Check your connection and retry.",
            SyncFailureKind.Timeout => _loader.GetStringOrDefault("SyncCenter_TimeoutFailure") ?? "The request timed out. Retry when the service is available.",
            SyncFailureKind.Storage => _loader.GetStringOrDefault("SyncCenter_StorageFailure") ?? "Local data could not be saved. Check available storage and retry.",
            _ => _loader.GetStringOrDefault("SyncCenter_UnknownFailure") ?? "Sync could not finish. Retry, or reconnect if the problem persists."
        };

        private void UpdateHealthRow(string key, string title, string state, string accountId, bool mail, bool syncing, bool failed, bool reconnectRequired, bool setupIncomplete = false)
        {
            if (!_healthRows.TryGetValue(key, out var row))
            {
                var root = new StackPanel { Spacing = 6, Padding = new Thickness(0, 8, 0, 8) };
                var heading = new TextBlock { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
                var status = new ContentState();
                var actions = new StackPanel { Spacing = VisualResources.Number("SpaceSmall") };
                var retry = new Button { Content = _loader.GetStringOrDefault("SyncCenter_RetryAccount") ?? "Retry sync", MinHeight = VisualResources.Number("ButtonHeight") };
                retry.Click += async (_, _) => await RetryAccountAsync(key, accountId, mail);
                var reconnect = new Button { Tag = accountId, MinHeight = VisualResources.Number("ButtonHeight") };
                if (mail) reconnect.Click += ReconnectMailButton_Click;
                else reconnect.Click += ReconnectAccountButton_Click;
                actions.Children.Add(retry);
                actions.Children.Add(reconnect);
                root.Children.Add(heading);
                root.Children.Add(status);
                root.Children.Add(actions);
                row = (root, heading, status, retry, reconnect);
                _healthRows[key] = row;
                ConnectedAccountActions.Children.Add(root);
            }
            row.Title.Text = title;
            if (syncing || _retrying.Contains(key)) row.Status.SetLoading(state, hasContent: true);
            else if (failed) row.Status.SetError(state, reconnectRequired
                ? ContentState.Text("ContentState_ReconnectHint", "Reconnect the affected account in Accounts and sync.") : ContentState.RetryHint);
            else row.Status.SetEmpty(state, "");
            row.Retry.IsEnabled = !setupIncomplete && !syncing && !_retrying.Contains(key) && !_authorizationBusy;
            row.Reconnect.IsEnabled = !_authorizationBusy && !_retrying.Contains(key);
            string reconnectLabel = _loader.GetStringOrDefault("AddAccount_ReconnectAccount") ?? "Reconnect";
            row.Reconnect.Content = reconnectLabel;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(row.Retry, $"{row.Retry.Content} {title}");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(row.Reconnect, $"{reconnectLabel} {title}");
        }

        private async Task RetryAccountAsync(string key, string accountId, bool mail)
        {
            if (App.Current is not App app || !_retrying.Add(key)) return;
            StatusText.Text = "";
            UpdateChecklist();
            try
            {
                if (mail) await app.MailService.RefreshAccountAsync(accountId);
                else if (GetAccountManager()?.GetAccountById(accountId) is { } account)
                    await app.SyncManager.SyncAccountAsync(account.ProviderName, account.AccountId, DateTime.Today.AddMonths(-1), DateTime.Today.AddMonths(2));
            }
            catch (Exception ex)
            {
                if (_isActive) StatusText.Text = FailureText(SyncFailurePolicy.Classify(ex));
            }
            finally
            {
                _retrying.Remove(key);
                if (_isActive) UpdateChecklist();
            }
        }

        private async void ReconnectMailButton_Click(object sender, RoutedEventArgs e)
        {
            if (App.Current is not App app || sender is not Button { Tag: string id }) return;
            var account = app.MailService.GetAccounts().SingleOrDefault(candidate => candidate.Id == id);
            if (account == null) return;
            if (account.Kind == MailAccountKind.Google)
            {
                SetAuthorizationControlsEnabled(false);
                try { await app.ReconnectGoogleAccountAsync(AccountIdentityPolicy.ResolveAccountId("Google", account.ProviderAccountId)); }
                catch (Exception ex) { if (_isActive) StatusText.Text = FailureText(SyncFailurePolicy.Classify(ex)); }
                finally { SetAuthorizationControlsEnabled(true); if (_isActive) UpdateChecklist(); }
            }
            else Frame?.Navigate(typeof(MailPage), new MailAccountSetupRequest(id));
        }

        private async void RetrySyncButton_Click(object sender, RoutedEventArgs e)
            => await RetryAllSyncAsync();

        private async Task RetryAllSyncAsync()
        {
            if (GetSyncManager() is not { } syncManager || !RetrySyncButton.IsEnabled) return;
            StatusText.Text = "";
            RetrySyncButton.IsEnabled = false;
            AuthProgress.IsActive = true;
            SyncLoadState.SetLoading(_loader.GetStringOrDefault("TextLoading") ?? "Loading", hasContent: true);
            try
            {
                await syncManager.GetAllDataAsync(DateTime.Today.AddMonths(-1), DateTime.Today.AddMonths(2), forceRefresh: true);
                if (!_isActive) return;
                UpdateChecklist();
                SyncContentFeedback.ShowResult(SyncLoadState, syncManager, RetryAllSyncAsync);
            }
            catch (Exception ex)
            {
                if (_isActive) SyncLoadState.SetError(FailureText(SyncFailurePolicy.Classify(ex)), ContentState.RetryHint, ContentState.RetryLabel, RetryAllSyncAsync);
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
            Frame?.Navigate(typeof(TodayPage));
        }

        private StackPanel CreateDisabledContent(string name, string color, string status)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = VisualResources.Number("SpaceLarge") };
            sp.Children.Add(new FontIcon
            {
                Glyph = AppIcons.Person,
                FontFamily = VisualResources.IconFont,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.Gray),
                FontSize = 24
            });
            var inner = new StackPanel { Spacing = VisualResources.Number("SpaceTiny"), VerticalAlignment = VerticalAlignment.Center, MinWidth = 0 };
            inner.Children.Add(new TextBlock { Text = name, FontSize = VisualResources.Number("FontSection"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            inner.Children.Add(new TextBlock
            {
                Text = status,
                FontSize = VisualResources.Number("FontCaption"),
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

        private async void ReconnectAccountButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string accountId } ||
                GetAccountManager()?.GetAccountById(accountId) is not ConnectedAccountInfo account)
            {
                return;
            }

            if (string.Equals(account.ProviderName, "Google", StringComparison.OrdinalIgnoreCase))
            {
                await ReconnectGoogleAccountAsync(account);
                return;
            }

            if (string.Equals(account.ProviderName, "iCloud", StringComparison.OrdinalIgnoreCase))
            {
                var credentials = await PromptForICloudCredentialsAsync();
                if (credentials == null) return;
                await ConnectAccountAsync(
                    "iCloud",
                    provider =>
                    {
                        if (provider is not ICloudSyncProvider iCloud)
                            throw new InvalidOperationException("The iCloud calendar provider is unavailable.");
                        return iCloud.ConnectWithCredentialsAsync(
                            credentials.Value.AccountName,
                            credentials.Value.Password);
                    },
                    account.AccountId,
                    account.DisplayTitle);
                return;
            }

            await ConnectAccountAsync(
                account.ProviderName,
                accountId: account.AccountId,
                accountTitle: account.DisplayTitle);
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
                Spacing = VisualResources.Number("SpaceMedium"),
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
            SetAuthorizationControlsEnabled(false);

            try
            {
                var mailAccount = await app.ConnectNewGoogleAccountAsync();
                StatusText.Text = string.Format(
                    _loader.GetStringOrDefault("TextAccountAdded") ?? "Added {0}",
                    mailAccount.Subtitle);
                CompleteAccountConnection();
            }
            catch (GoogleAccountAlreadyConnectedException ex)
            {
                StatusText.Text = string.Format(
                    _loader.GetStringOrDefault("AddAccount_GoogleAlreadyConnected") ?? "Google account {0} is already connected.",
                    ex.Address);
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
                SetAuthorizationControlsEnabled(true);
                UpdateButtonStates();
            }
        }

        private async Task ReconnectGoogleAccountAsync(ConnectedAccountInfo account)
        {
            if (App.Current is not App app) return;

            AuthProgress.IsActive = true;
            StatusText.Text = string.Format(
                _loader.GetStringOrDefault("AddAccount_ReconnectingAccount") ?? "Reconnecting {0}...",
                account.DisplayTitle);
            SetAuthorizationControlsEnabled(false);

            try
            {
                var mailAccount = await app.ReconnectGoogleAccountAsync(account.AccountId);
                StatusText.Text = string.Format(
                    _loader.GetStringOrDefault("AddAccount_AccountReconnected") ?? "Reconnected {0}",
                    mailAccount.Subtitle);
                CompleteAccountConnection();
            }
            catch (GoogleAccountMismatchException ex)
            {
                StatusText.Text = string.Format(
                    _loader.GetStringOrDefault("AddAccount_GoogleAccountMismatch")
                        ?? "Signed in as {1}, but {0} was selected. The sign-in was not linked and the selected account remains disconnected.",
                    ex.ExpectedAddress,
                    ex.ActualAddress);
            }
            catch (GoogleAccountAlreadyConnectedException ex)
            {
                StatusText.Text = string.Format(
                    _loader.GetStringOrDefault("AddAccount_GoogleAlreadyConnected") ?? "Google account {0} is already connected.",
                    ex.Address);
            }
            catch (Exception ex)
            {
                StatusText.Text = UserSafeErrorMessage.FromException(
                    ex,
                    _loader.GetStringOrDefault("TextAuthFailed") ?? "Authentication failed.");
                System.Diagnostics.Debug.WriteLine($"Google account reconnect failed: {ex}");
            }
            finally
            {
                AuthProgress.IsActive = false;
                SetAuthorizationControlsEnabled(true);
                UpdateButtonStates();
            }
        }

        private async Task ConnectAccountAsync(
            string providerName,
            Func<ISyncProvider, Task>? connect = null,
            string? accountId = null,
            string? accountTitle = null)
        {
            var mgr = GetAccountManager();
            var syncManager = GetSyncManager();
            if (mgr == null || syncManager == null) return;

            AuthProgress.IsActive = true;
            bool isReconnect = !string.IsNullOrWhiteSpace(accountId);
            StatusText.Text = isReconnect
                ? string.Format(
                    _loader.GetStringOrDefault("AddAccount_ReconnectingAccount") ?? "Reconnecting {0}...",
                    accountTitle ?? providerName)
                : _loader.GetStringOrDefault("TextAuthorizing") ?? "Authorizing...";
            SetAuthorizationControlsEnabled(false);

            try
            {
                var provider = syncManager.GetProvider(providerName, accountId);
                if (provider == null) throw new Exception($"Provider {providerName} not registered");

                if (connect != null)
                    await connect(provider);
                else
                    await provider.ConnectInteractivelyAsync();

                // Create account entry
                var account = mgr.GetAccount(providerName, provider.AccountId);
                bool isNewAccount = account == null;
                account ??= new ConnectedAccountInfo
                {
                    ProviderName = provider.ProviderName,
                    AccountId = provider.AccountId,
                    DisplayName = provider.AccountDisplayName
                };
                if (isNewAccount)
                {
                    var capabilities = SyncProviderCapabilityPolicy.ForProvider(providerName);
                    account.ShowEvents = capabilities.SupportsEvents;
                    account.ShowTasks = capabilities.SupportsTasks;
                }

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
                StatusText.Text = string.Format(
                    isReconnect
                        ? _loader.GetStringOrDefault("AddAccount_AccountReconnected") ?? "Reconnected {0}"
                        : _loader.GetStringOrDefault("TextAccountAdded") ?? "Added {0}",
                    account.DisplayTitle);
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
                SetAuthorizationControlsEnabled(true);
                UpdateButtonStates();
            }
        }

        private void SetAuthorizationControlsEnabled(bool isEnabled)
        {
            _authorizationBusy = !isEnabled;
            BtnGoogle.IsEnabled = isEnabled;
            BtnMicrosoft.IsEnabled = isEnabled;
            BtnICloud.IsEnabled = isEnabled;
            RetrySyncButton.IsEnabled = isEnabled;
            ConnectedAccountActions.IsHitTestVisible = isEnabled;
            ConnectedAccountActions.Opacity = isEnabled ? 1 : 0.65;
            UpdateChecklist();
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
