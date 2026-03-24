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

            LoadAccountStates();

            var calendarItem = MainNav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault();
            if (calendarItem != null) MainNav.SelectedItem = calendarItem;
            ContentFrame.Navigate(typeof(Views.CalendarPage));
        }

        private void AppWindow_Closing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
        {
            bool runInBackground = Windows.Storage.ApplicationData.Current.LocalSettings.Values["RunInBackground"] as bool? ?? true;

            if (runInBackground)
            {
                args.Cancel = true; 
                sender.Hide();
            }
        }

        private void LoadAccountStates()
        {
            var settings = ApplicationData.Current.LocalSettings;

            bool isGoogleConnected = settings.Values["IsGoogleConnected"] as bool? ?? false;
            BtnConnectGoogle.Visibility = isGoogleConnected ? Visibility.Collapsed : Visibility.Visible;
            PanelGoogleToggles.Visibility = isGoogleConnected ? Visibility.Visible : Visibility.Collapsed;

            bool isMSConnected = settings.Values["IsMSConnected"] as bool? ?? false;
            BtnConnectMS.Visibility = isMSConnected ? Visibility.Collapsed : Visibility.Visible;
            PanelMSToggles.Visibility = isMSConnected ? Visibility.Visible : Visibility.Collapsed;

            // 加载开关的历史状态
            TglGoogleEvents.IsOn = settings.Values["ShowGoogleEvents"] as bool? ?? true;
            TglGoogleTasks.IsOn = settings.Values["ShowGoogleTasks"] as bool? ?? true;
            TglMSEvents.IsOn = settings.Values["ShowMSEvents"] as bool? ?? true;
            TglMSTasks.IsOn = settings.Values["ShowMSTasks"] as bool? ?? true;
        }

        private async void BtnConnectGoogle_Click(object sender, RoutedEventArgs e)
        {
            BtnConnectGoogle.Content = _loader.GetString("TextAuthorizing"); // 👉 替换
            try
            {
                if (App.Current is App app)
                {
                    var provider = app.SyncManager.Providers.FirstOrDefault(p => p.ProviderName == "Google");
                    if (provider != null) await provider.EnsureAuthorizedAsync();
                    await app.SyncManager.GetAllDataAsync(DateTime.Today, DateTime.Today.AddDays(1));
                }
                Windows.Storage.ApplicationData.Current.LocalSettings.Values["IsGoogleConnected"] = true;
                LoadAccountStates();
                if (ContentFrame.Content is Views.CalendarPage page) page.ForceSync();
            }
            catch { BtnConnectGoogle.Content = _loader.GetString("TextAuthFailed"); } // 👉 替换
        }

        private async void BtnConnectMS_Click(object sender, RoutedEventArgs e)
        {
            BtnConnectMS.Content = _loader.GetString("TextAuthorizing"); // 👉 替换
            try
            {
                if (App.Current is App app)
                {
                    var provider = app.SyncManager.Providers.FirstOrDefault(p => p.ProviderName == "Microsoft");
                    if (provider == null) throw new Exception("未注册 MicrosoftSyncProvider");

                    await provider.EnsureAuthorizedAsync();
                    await app.SyncManager.GetAllDataAsync(DateTime.Today, DateTime.Today.AddDays(1));
                }
                Windows.Storage.ApplicationData.Current.LocalSettings.Values["IsMSConnected"] = true;
                LoadAccountStates();
                if (ContentFrame.Content is Views.CalendarPage page) page.ForceSync();
            }
            catch (Exception ex)
            {
                BtnConnectMS.Content = _loader.GetString("TextAuthFailed"); // 👉 替换
                System.Diagnostics.Debug.WriteLine($"============== 微软授权失败 ==============");
                System.Diagnostics.Debug.WriteLine(ex.ToString());
            }
        }

        private void Filter_Toggled(object sender, RoutedEventArgs e)
        {
            if (TglGoogleEvents == null || TglGoogleTasks == null ||
                TglMSEvents == null || TglMSTasks == null ||
                ContentFrame == null)
                return;
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values["ShowGoogleEvents"] = TglGoogleEvents.IsOn;
            settings.Values["ShowGoogleTasks"] = TglGoogleTasks.IsOn;
            settings.Values["ShowMSEvents"] = TglMSEvents.IsOn;
            settings.Values["ShowMSTasks"] = TglMSTasks.IsOn;

            if (ContentFrame.Content is CalendarPage page) page.ReloadFilters();
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
        }

        public void NavigateToSettings()
        {
            MainNav.SelectedItem = MainNav.SettingsItem;
            ContentFrame.Navigate(typeof(Views.SettingsPage));
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
    }
}