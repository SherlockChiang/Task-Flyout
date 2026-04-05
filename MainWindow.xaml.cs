using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Linq;
using Task_Flyout.Models;
using Task_Flyout.Services;
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

        private void CalendarColorDot_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Border border && border.Tag is SubscribedCalendarInfo calInfo)
            {
                var hex = calInfo.ColorHex;
                border.Background = !string.IsNullOrEmpty(hex)
                    ? new SolidColorBrush(ColorHelper.ParseHex(hex))
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 150, 150));
            }
        }

        private void TaskColorDot_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Border border && border.Tag is ConnectedAccountInfo accountInfo)
            {
                var hex = accountInfo.TaskColorHex;
                border.Background = !string.IsNullOrEmpty(hex)
                    ? new SolidColorBrush(ColorHelper.ParseHex(hex))
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 150, 150, 150));
            }
        }

        private void CalendarColorDot_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is Border border && border.Tag is SubscribedCalendarInfo calInfo)
            {
                ShowColorPickerFlyout(border, calInfo.ColorHex, selectedColor =>
                {
                    calInfo.ColorHex = selectedColor;
                    border.Background = new SolidColorBrush(ColorHelper.ParseHex(selectedColor));
                    var mgr = GetAccountManager();
                    mgr?.Save();
                    BroadcastFilterChange();
                });
            }
        }

        private void TaskColorDot_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is Border border && border.Tag is ConnectedAccountInfo accountInfo)
            {
                ShowColorPickerFlyout(border, accountInfo.TaskColorHex, selectedColor =>
                {
                    accountInfo.TaskColorHex = selectedColor;
                    border.Background = new SolidColorBrush(ColorHelper.ParseHex(selectedColor));
                    var mgr = GetAccountManager();
                    mgr?.Save();
                    BroadcastFilterChange();
                });
            }
        }

        private void ShowColorPickerFlyout(FrameworkElement anchor, string currentColor, Action<string> onColorSelected)
        {
            var flyout = new Flyout();
            var panel = new StackPanel { Spacing = 12, Padding = new Thickness(8), MaxWidth = 340 };

            var presetHeader = new TextBlock
            {
                Text = GetSafeString("TextPresetColors", "预设颜色"),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 14
            };
            panel.Children.Add(presetHeader);

            var presetGrid = new GridView
            {
                SelectionMode = ListViewSelectionMode.None,
                IsItemClickEnabled = true,
                Margin = new Thickness(-4, 0, -4, 0)
            };
            presetGrid.ItemsPanel = (ItemsPanelTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                "<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
                "<ItemsWrapGrid MaximumRowsOrColumns='6' Orientation='Horizontal'/>" +
                "</ItemsPanelTemplate>");

            string[] monetColors = {
                "#D5A5A1", "#B38B8D", "#C3D1C6", "#9CB4A3", "#758A7A", "#E6D4B8",
                "#D2B88F", "#B49665", "#BBD0D9", "#92A6B9", "#6A7B92", "#B19FB6"
            };

            var colorPicker = new ColorPicker
            {
                Color = ColorHelper.ParseHex(currentColor),
                IsAlphaEnabled = false,
                IsColorChannelTextInputVisible = true,
                IsHexInputVisible = true,
                Margin = new Thickness(0, -8, 0, 0)
            };

            foreach (var hex in monetColors)
            {
                var swatch = new Border
                {
                    Width = 32,
                    Height = 32,
                    CornerRadius = new CornerRadius(16),
                    Background = new SolidColorBrush(ColorHelper.ParseHex(hex)),
                    Margin = new Thickness(4),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.LightGray)
                };

                swatch.Tapped += (s, args) =>
                {
                    colorPicker.Color = ColorHelper.ParseHex(hex);
                };
                presetGrid.Items.Add(swatch);
            }
            panel.Children.Add(presetGrid);

            var customHeader = new TextBlock
            {
                Text = GetSafeString("TextCustomColor", "自定义颜色 (RGB/HEX)"),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 14,
                Margin = new Thickness(0, 8, 0, 0)
            };
            panel.Children.Add(customHeader);

            panel.Children.Add(colorPicker);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };

            var cancelBtn = new Button { Content = GetSafeString("CalendarDialog/CloseButtonText", "取消") };
            cancelBtn.Click += (s, e) => flyout.Hide();

            var applyBtn = new Button
            {
                Content = GetSafeString("TextConfirm", "确定"),
                Style = (Style)Application.Current.Resources["AccentButtonStyle"]
            };
            applyBtn.Click += (s, e) =>
            {
                var c = colorPicker.Color;
                string newHex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                onColorSelected(newHex);
                flyout.Hide();
            };

            btnPanel.Children.Add(cancelBtn);
            btnPanel.Children.Add(applyBtn);
            panel.Children.Add(btnPanel);

            flyout.Content = panel;
            flyout.ShowAt(anchor);
        }
    }
}