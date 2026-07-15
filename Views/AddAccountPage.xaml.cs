using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using System;
using System.Linq;
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
            UpdateButtonStates();
            UpdateChecklist();
        }

        private void UpdateButtonStates()
        {
            var mgr = GetAccountManager();
            if (mgr == null) return;

            BtnGoogle.IsEnabled = !mgr.IsConnected("Google");
            BtnMicrosoft.IsEnabled = !mgr.IsConnected("Microsoft");

            if (!BtnGoogle.IsEnabled)
                BtnGoogle.Content = CreateDisabledContent("Google", "#EA4335",
                    _loader.GetStringOrDefault("AddAccount_AlreadyConnected") ?? "Already connected");
            if (!BtnMicrosoft.IsEnabled)
                BtnMicrosoft.Content = CreateDisabledContent("Microsoft", "#0078D4",
                    _loader.GetStringOrDefault("AddAccount_AlreadyConnected") ?? "Already connected");

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

                await provider.EnsureAuthorizedAsync();

                // Create account entry
                var account = new ConnectedAccountInfo { ProviderName = providerName };

                // Fetch subscribed calendars
                try
                {
                    var calendars = await provider.FetchCalendarListAsync();
                    foreach (var cal in calendars)
                        account.Calendars.Add(cal);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to fetch calendar list for {providerName}: {ex.Message}");
                }

                mgr.AddAccount(account);
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
