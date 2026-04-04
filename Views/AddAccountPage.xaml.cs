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
        }

        private void UpdateButtonStates()
        {
            var mgr = GetAccountManager();
            if (mgr == null) return;

            BtnGoogle.IsEnabled = !mgr.IsConnected("Google");
            BtnMicrosoft.IsEnabled = !mgr.IsConnected("Microsoft");

            if (!BtnGoogle.IsEnabled)
                BtnGoogle.Content = CreateDisabledContent("Google", "#EA4335",
                    _loader.GetString("AddAccount_AlreadyConnected") ?? "已连接");
            if (!BtnMicrosoft.IsEnabled)
                BtnMicrosoft.Content = CreateDisabledContent("Microsoft", "#0078D4",
                    _loader.GetString("AddAccount_AlreadyConnected") ?? "已连接");
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
            StatusText.Text = _loader.GetString("TextAuthorizing") ?? "授权中...";
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

                // Refresh the MainWindow pane
                if (App.MyMainWindow is MainWindow mainWin)
                {
                    mainWin.RefreshAccountList();

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
                StatusText.Text = _loader.GetString("TextAuthFailed") ?? "授权失败";
                System.Diagnostics.Debug.WriteLine($"Auth failed for {providerName}: {ex}");
            }
            finally
            {
                AuthProgress.IsActive = false;
                UpdateButtonStates();
            }
        }

        private static AccountManager GetAccountManager()
        {
            if (App.Current is App app) return app.SyncManager.AccountManager;
            return null;
        }

        private static SyncManager GetSyncManager()
        {
            if (App.Current is App app) return app.SyncManager;
            return null;
        }
    }
}
