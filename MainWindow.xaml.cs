using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Linq;
using Task_Flyout.Models;
using Task_Flyout.Views;
using Windows.Storage;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Task_Flyout
{
    public sealed partial class MainWindow : Window
    {
        private ResourceLoader _loader;

        public MainWindow()
        {
            this.InitializeComponent();
            this.AppWindow.SetIcon(System.IO.Path.Combine(System.AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
            _loader = new ResourceLoader();

            SystemBackdrop = new MicaBackdrop() { Kind = MicaKind.BaseAlt };
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(null);
            this.AppWindow.Closing += AppWindow_Closing;

            MainNav.PaneOpening += (s, e) => FooterContentPanel.Visibility = Visibility.Visible;
            MainNav.PaneClosing += (s, e) => FooterContentPanel.Visibility = Visibility.Collapsed;
            FooterContentPanel.Visibility = MainNav.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;

            ContentFrame.Navigated += ContentFrame_Navigated;

            RefreshAccountList();

            var calendarItem = MainNav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault();
            if (calendarItem != null) MainNav.SelectedItem = calendarItem;
            ContentFrame.Navigate(typeof(Views.CalendarPage));
        }

        private string GetSafeString(string key, string fallbackText)
        {
            if (_loader == null) return fallbackText;
            try
            {
                string safeKey = key.Replace(".", "/");
                string result = _loader.GetString(safeKey);
                return string.IsNullOrEmpty(result) ? fallbackText : result;
            }
            catch
            {
                return fallbackText;
            }
        }

        private void ContentFrame_Navigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            MainNav.IsBackEnabled = ContentFrame.CanGoBack;

            if (ContentFrame.SourcePageType == typeof(Views.SettingsPage))
            {
                MainNav.SelectedItem = MainNav.SettingsItem;
            }
            else if (ContentFrame.SourcePageType == typeof(Views.CalendarPage))
            {
                MainNav.SelectedItem = MainNav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag?.ToString() == "Calendar");
            }
            else if (ContentFrame.SourcePageType == typeof(Views.WeatherPage))
            {
                MainNav.SelectedItem = MainNav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag?.ToString() == "Weather");
            }
            else
            {
                MainNav.SelectedItem = null;
            }
        }

        private void MainNav_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        {
            if (ContentFrame.CanGoBack)
            {
                ContentFrame.GoBack();
            }
        }

        private void AppWindow_Closing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
        {
            bool runInBackground = ApplicationData.Current.LocalSettings.Values["RunInBackground"] as bool? ?? true;

            if (runInBackground)
            {
                args.Cancel = true;
                sender.Hide();
            }
        }

        private Services.AccountManager GetAccountManager()
        {
            if (App.Current is App app) return app.SyncManager.AccountManager;
            return null;
        }

        public async void RefreshAccountList()
        {
            var mgr = GetAccountManager();
            if (mgr == null) return;

            AccountListRepeater.ItemsSource = null;
            AccountListRepeater.ItemsSource = mgr.Accounts;

            if (App.Current is App app)
            {
                await app.SyncManager.SyncAllCalendarsAsync();
                AccountListRepeater.ItemsSource = null;
                AccountListRepeater.ItemsSource = mgr.Accounts;
            }
        }

        private void BtnAddAccount_Click(object sender, RoutedEventArgs e)
        {
            ContentFrame.Navigate(typeof(Views.AddAccountPage));
        }

        private async void BtnRemoveAccount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string providerName)
            {
                var dialog = new ContentDialog
                {
                    Title = GetSafeString("TextRemoveAccountTitle", "移除账户"),
                    Content = string.Format(GetSafeString("TextRemoveAccountContent", "确定要移除 {0} 账户吗？"), providerName),
                    PrimaryButtonText = GetSafeString("TextConfirm", "确定"),
                    CloseButtonText = GetSafeString("CalendarDialog/CloseButtonText", "取消"),
                    XamlRoot = this.Content.XamlRoot,
                    DefaultButton = ContentDialogButton.Close
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    var mgr = GetAccountManager();
                    mgr?.RemoveAccount(providerName);
                    RefreshAccountList();

                    if (ContentFrame.Content is CalendarPage page) page.ForceSync();
                }
            }
        }

        private void AccountToggle_Toggled(object sender, RoutedEventArgs e)
        {
            var mgr = GetAccountManager();
            if (mgr == null) return;
            mgr.Save();
            BroadcastFilterChange();
        }

        private void CalendarToggle_Toggled(object sender, RoutedEventArgs e)
        {
            var mgr = GetAccountManager();
            if (mgr == null) return;
            mgr.Save();
            BroadcastFilterChange();
        }

        private void BroadcastFilterChange()
        {
            if (ContentFrame.Content is CalendarPage page) page.ReloadFilters();

            if (App.Current is App app && app.GetType().GetProperty("MyFlyoutWindow")?.GetValue(app) is FlyoutWindow flyout)
            {
                flyout.ReloadFilters();
            }
        }

        private void BtnForceSync_Click(object sender, RoutedEventArgs e)
        {
            if (ContentFrame.Content is CalendarPage page) page.ForceSync();
        }

        private void MainNav_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.IsSettingsInvoked) ContentFrame.Navigate(typeof(SettingsPage));
            else if (args.InvokedItemContainer is NavigationViewItem item && item.Tag?.ToString() == "Calendar")
                ContentFrame.Navigate(typeof(CalendarPage));
            else if (args.InvokedItemContainer is NavigationViewItem itemW && itemW.Tag?.ToString() == "Weather")
                ContentFrame.Navigate(typeof(WeatherPage));
        }

        public void NavigateToSettings()
        {
            MainNav.SelectedItem = MainNav.SettingsItem;
            ContentFrame.Navigate(typeof(Views.SettingsPage));
        }

        public void NavigateToWeather()
        {
            var weatherItem = MainNav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag?.ToString() == "Weather");
            if (weatherItem != null) MainNav.SelectedItem = weatherItem;
            ContentFrame.Navigate(typeof(Views.WeatherPage));
        }

        public void NavigateToCalendarAndEdit(AgendaItem itemToEdit)
        {
            var calendarItem = MainNav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault();
            if (calendarItem != null) MainNav.SelectedItem = calendarItem;
            ContentFrame.Navigate(typeof(Views.CalendarPage));

            DispatcherQueue.TryEnqueue(() =>
            {
                if (ContentFrame.Content is Views.CalendarPage calendarPage) calendarPage.OpenEditDialogFromExternal(itemToEdit);
            });
        }

        public void RefreshCalendarColors()
        {
            if (ContentFrame.Content is Views.CalendarPage page)
            {
                page.ReloadFilters();
            }
        }

        private void CalendarColorDot_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Border border && border.Tag is SubscribedCalendarInfo calInfo)
            {
                var hex = calInfo.ColorHex;
                border.Background = !string.IsNullOrEmpty(hex)
                    ? new SolidColorBrush(Services.ColorHelper.ParseHex(hex))
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 150, 150));
            }
        }

        private void TaskColorDot_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Border border && border.Tag is ConnectedAccountInfo accountInfo)
            {
                var hex = accountInfo.TaskColorHex;
                border.Background = !string.IsNullOrEmpty(hex)
                    ? new SolidColorBrush(Services.ColorHelper.ParseHex(hex))
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 150, 150));
            }
        }
    }
}