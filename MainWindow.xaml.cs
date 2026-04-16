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
            RefreshWeatherNavIconAsync();

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

        public void RefreshAccountListUI()
        {
            var mgr = GetAccountManager();
            if (mgr == null) return;
            AccountListRepeater.ItemsSource = null;
            AccountListRepeater.ItemsSource = mgr.Accounts;
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

            // Delay visual tree walk until after ItemsRepeater has finished layout
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateSidebarColorDots(AccountListRepeater);
            });
        }

        private void UpdateSidebarColorDots(DependencyObject root)
        {
            if (root == null) return;
            int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
                if (child is Border border && border.Tag != null)
                {
                    string hex = null;
                    if (border.Tag is SubscribedCalendarInfo calInfo) hex = calInfo.ColorHex;
                    else if (border.Tag is ConnectedAccountInfo accInfo) hex = accInfo.TaskColorHex;

                    if (hex != null)
                    {
                        border.Background = !string.IsNullOrEmpty(hex)
                            ? new SolidColorBrush(Services.ColorHelper.ParseHex(hex))
                            : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 150, 150));
                    }
                }
                UpdateSidebarColorDots(child);
            }
        }

        private void ApplyColorToBorder(Border border, string hex)
        {
            border.Background = !string.IsNullOrEmpty(hex)
                ? new SolidColorBrush(Services.ColorHelper.ParseHex(hex))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 150, 150));
        }

        private void CalendarColorDot_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Border border)
            {
                if (border.Tag is SubscribedCalendarInfo calInfo)
                {
                    ApplyColorToBorder(border, calInfo.ColorHex);
                }
                else
                {
                    // Tag binding not yet evaluated, wait for it
                    long token = 0;
                    token = border.RegisterPropertyChangedCallback(FrameworkElement.TagProperty, (s, dp) =>
                    {
                        if (s is Border b && b.Tag is SubscribedCalendarInfo ci)
                        {
                            ApplyColorToBorder(b, ci.ColorHex);
                            b.UnregisterPropertyChangedCallback(FrameworkElement.TagProperty, token);
                        }
                    });
                }
            }
        }

        private void TaskColorDot_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Border border)
            {
                if (border.Tag is ConnectedAccountInfo accountInfo)
                {
                    ApplyColorToBorder(border, accountInfo.TaskColorHex);
                }
                else
                {
                    long token = 0;
                    token = border.RegisterPropertyChangedCallback(FrameworkElement.TagProperty, (s, dp) =>
                    {
                        if (s is Border b && b.Tag is ConnectedAccountInfo accInfo)
                        {
                            ApplyColorToBorder(b, accInfo.TaskColorHex);
                            b.UnregisterPropertyChangedCallback(FrameworkElement.TagProperty, token);
                        }
                    });
                }
            }
        }

        private const string FluentIconsFont = "ms-appx:///Assets/FluentSystemIcons-Filled.ttf#FluentSystemIcons-Filled";

        public async void RefreshWeatherNavIconAsync()
        {
            var weatherService = (App.Current as App)?.WeatherService;
            if (weatherService == null || !weatherService.IsEnabled) return;

            var info = await weatherService.GetWeatherAsync();
            if (info == null) return;

            DispatcherQueue.TryEnqueue(() =>
            {
                WeatherNavIcon.Glyph = WeatherCodeToFluentGlyph(info.RawWeatherCode);
                WeatherNavIcon.FontFamily = new FontFamily(FluentIconsFont);
            });
        }

        private static string WeatherCodeToFluentGlyph(int code)
        {
            // FluentSystemIcons-Filled codepoints for weather conditions
            // Supports both Open-Meteo WMO codes (0-99) and wttr.in codes (113-395)
            return code switch
            {
                // Sunny / Clear
                0 or 113 => "\uF8BA",                          // weather_sunny
                // Partly cloudy
                1 or 2 or 116 => "\uF899",                     // weather_partly_cloudy_day
                // Cloudy / Overcast
                3 or 119 or 122 => "\uF887",                    // weather_cloudy
                // Fog / Mist
                45 or 48 or 143 or 248 or 260 => "\uF88D",     // weather_fog
                // Drizzle / Light rain
                51 or 53 or 55 or 56 or 57
                    or 176 or 263 or 266 or 293 or 296 => "\uF8A2", // weather_rain_showers_day
                // Rain
                61 or 63 or 65 or 66 or 67 or 80 or 81 or 82
                    or 299 or 302 or 305 or 308
                    or 356 or 359 => "\uF89F",                  // weather_rain
                // Snow
                71 or 73 or 75 or 77 or 85 or 86
                    or 179 or 227 or 323 or 326 or 329 or 332
                    or 335 or 338 or 368 or 371 => "\uF8AB",   // weather_snow
                // Thunderstorm
                95 or 96 or 99
                    or 200 or 386 or 389 or 392 or 395 => "\uF8B7", // weather_squalls (thunder)
                // Sleet / Ice
                182 or 185 or 281 or 284 or 311 or 314
                    or 317 or 350 or 362 or 365
                    or 374 or 377 => "\uF8A8",                  // weather_rain_snow
                _ => "\uF8BA"                                    // default: sunny
            };
        }
    }
}