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

            RefreshAccountList();

            var calendarItem = MainNav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault();
            if (calendarItem != null) MainNav.SelectedItem = calendarItem;
            ContentFrame.Navigate(typeof(Views.CalendarPage));
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

        public void RefreshAccountList()
        {
            var mgr = GetAccountManager();
            if (mgr == null) return;
            AccountListRepeater.ItemsSource = null;
            AccountListRepeater.ItemsSource = mgr.Accounts;
        }

        private void BtnAddAccount_Click(object sender, RoutedEventArgs e)
        {
            ContentFrame.Navigate(typeof(Views.AddAccountPage));
            // Deselect nav items since AddAccountPage isn't a nav item
            MainNav.SelectedItem = null;
        }

        private async void BtnRemoveAccount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string providerName)
            {
                var dialog = new ContentDialog
                {
                    Title = _loader.GetString("TextRemoveAccountTitle") ?? "移除账户",
                    Content = string.Format(_loader.GetString("TextRemoveAccountContent") ?? "确定要移除 {0} 账户吗？", providerName),
                    PrimaryButtonText = _loader.GetString("TextConfirm") ?? "确定",
                    CloseButtonText = _loader.GetString("CalendarDialog/CloseButtonText") ?? "取消",
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

            if (ContentFrame.Content is CalendarPage page) page.ReloadFilters();
        }

        private void CalendarToggle_Toggled(object sender, RoutedEventArgs e)
        {
            var mgr = GetAccountManager();
            if (mgr == null) return;

            mgr.Save();

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
