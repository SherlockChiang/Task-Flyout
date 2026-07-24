using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Task_Flyout.Models;
using Task_Flyout.Services;
using Task_Flyout.Views;
using Windows.Storage;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.Graphics;

namespace Task_Flyout
{
    public sealed partial class MainWindow : Window
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

        private ResourceLoader _loader;
        private bool _pendingMailMessageNavigation;
        private bool _pendingCachedMailNavigation;
        private bool _isClampingToWorkArea;
        private Type _lastContentPageType = typeof(Views.CalendarPage);
        private readonly ObservableCollection<GlobalSearchResultGroup> _globalSearchGroups = new();
        private IReadOnlyList<GlobalSearchCandidate> _globalSearchSnapshot = Array.Empty<GlobalSearchCandidate>();
        private readonly Dictionary<string, object> _globalSearchPayloads = new(StringComparer.Ordinal);
        private DependencyObject? _globalSearchPreviousFocus;

        public MainWindow()
        {
            this.InitializeComponent();
            if (this.Content is FrameworkElement fe)
            {
                fe.Language = Windows.Globalization.ApplicationLanguages.Languages[0];
                fe.Loaded += (_, _) =>
                {
                    if (fe.XamlRoot != null)
                        fe.XamlRoot.Changed += (_, _) => EnsureWithinCurrentWorkArea();
                };
            }
            this.AppWindow.SetIcon(System.IO.Path.Combine(System.AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
            _loader = new ResourceLoader();

            SystemBackdrop = new MicaBackdrop() { Kind = MicaKind.BaseAlt };
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(null);
            this.AppWindow.Closing += AppWindow_Closing;
            SizeToCurrentWorkArea();

            ContentFrame.Navigated += ContentFrame_Navigated;
            GlobalSearchResultsSource.Source = _globalSearchGroups;

            _ = RefreshWeatherNavIconAsync();

            var calendarItem = MainNav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault();
            if (calendarItem != null) MainNav.SelectedItem = calendarItem;
            ContentFrame.Navigate(ShouldShowOnboardingOnLaunch() ? typeof(Views.AddAccountPage) : typeof(Views.CalendarPage));
        }

        private void EnsureWithinCurrentWorkArea()
        {
            if (_isClampingToWorkArea) return;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var display = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(windowId, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);
            var workArea = display.WorkArea;
            int margin = (int)Math.Ceiling(12 * Math.Max(1, GetDpiForWindow(hWnd) / 96d));
            int width = Math.Min(AppWindow.Size.Width, Math.Max(1, workArea.Width - margin * 2));
            int height = Math.Min(AppWindow.Size.Height, Math.Max(1, workArea.Height - margin * 2));
            int x = Math.Clamp(AppWindow.Position.X, workArea.X + margin, workArea.X + workArea.Width - width - margin);
            int y = Math.Clamp(AppWindow.Position.Y, workArea.Y + margin, workArea.Y + workArea.Height - height - margin);
            if (width == AppWindow.Size.Width && height == AppWindow.Size.Height
                && x == AppWindow.Position.X && y == AppWindow.Position.Y) return;

            _isClampingToWorkArea = true;
            try
            {
                AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
            }
            finally
            {
                _isClampingToWorkArea = false;
            }
        }

        private void SizeToCurrentWorkArea()
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var display = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(windowId, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
            var workArea = display.WorkArea;
            var scale = Math.Max(1, GetDpiForWindow(hWnd) / 96d);
            var size = WindowSizingPolicy.Calculate(1200, 800, scale, workArea.Width, workArea.Height, 24);
            int width = size.Width;
            int height = size.Height;
            int x = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
            int y = workArea.Y + Math.Max(0, (workArea.Height - height) / 2);
            AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
        }

        private bool ShouldShowOnboardingOnLaunch()
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            return OnboardingPolicy.ShouldShow(
                values[OnboardingPolicy.CompletedVersionKey],
                values[OnboardingPolicy.LegacyCompletedKey]);
        }

        private string GetSafeString(string key, string fallbackText)
        {
            if (_loader == null) return fallbackText;
            try
            {
                var result = _loader.GetStringOrDefault(key);
                if (string.IsNullOrEmpty(result) && key.Contains('.'))
                    result = _loader.GetStringOrDefault(key.Replace(".", "/"));
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
            if (e.SourcePageType != null)
                _lastContentPageType = e.SourcePageType;

            if (ContentFrame.SourcePageType == typeof(Views.SettingsPage))
            {
                MainNav.SelectedItem = MainNav.SettingsItem;
            }
            else if (ContentFrame.SourcePageType == typeof(Views.CalendarPage))
            {
                MainNav.SelectedItem = MainNav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag?.ToString() == "Calendar");
            }
            else if (ContentFrame.SourcePageType == typeof(Views.TasksPage))
            {
                MainNav.SelectedItem = MainNav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag?.ToString() == "Tasks");
            }
            else if (ContentFrame.SourcePageType == typeof(Views.WeatherPage))
            {
                MainNav.SelectedItem = MainNav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag?.ToString() == "Weather");
            }
            else if (ContentFrame.SourcePageType == typeof(Views.MailPage))
            {
                MainNav.SelectedItem = MainNav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag?.ToString() == "Mail");
                if ((_pendingMailMessageNavigation || _pendingCachedMailNavigation) && e.Content is Views.MailPage mailPage)
                {
                    mailPage.IsOpeningFromNotification = true;
                }
            }
            else if (ContentFrame.SourcePageType == typeof(Views.RssPage))
            {
                MainNav.SelectedItem = MainNav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag?.ToString() == "Rss");
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
                if (App.Current is App app)
                    app.MailService.UpdateMailPollingSettings();
                ReleaseContentForBackground();
                sender.Hide();
                App.UpdateEfficiencyMode(); // back to tray — throttle
            }
            else
            {
                bool confirmed = ApplicationData.Current.LocalSettings.Values["CloseToExitConfirmed"] as bool? ?? false;
                if (confirmed)
                {
                    // Pass this window so the exit path doesn't re-enter the Close()
                    // we're already inside.
                    App.ExitApp(this);
                    return;
                }

                // First close-to-exit: confirm so the user doesn't quit by accident.
                args.Cancel = true;
                _ = ConfirmCloseToExitAsync();
            }
        }

        private async System.Threading.Tasks.Task ConfirmCloseToExitAsync()
        {
            try
            {
                var dialog = new ContentDialog
                {
                    Title = GetSafeString("CloseToExit_Title", "Exit Task Flyout?"),
                    Content = GetSafeString("CloseToExit_Content", "Closing the window will quit Task Flyout. You can keep it running in the background from Settings."),
                    PrimaryButtonText = GetSafeString("CloseToExit_Exit", "Exit"),
                    SecondaryButtonText = GetSafeString("CloseToExit_Background", "Run in background"),
                    CloseButtonText = GetSafeString("CalendarDialog.CloseButtonText", "Cancel"),
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.Content.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    ApplicationData.Current.LocalSettings.Values["CloseToExitConfirmed"] = true;
                    App.ExitApp(this);
                }
                else if (result == ContentDialogResult.Secondary)
                {
                    // Switch to background mode permanently and minimise to tray now.
                    ApplicationData.Current.LocalSettings.Values["RunInBackground"] = true;
                    if (App.Current is App app)
                        app.MailService.UpdateMailPollingSettings();
                    ReleaseContentForBackground();
                    AppWindow.Hide();
                    App.UpdateEfficiencyMode();
                }
                // Cancel: leave the window open, do nothing.
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Close-to-exit confirm failed: {ex.Message}");
                App.ExitApp(this);
            }
        }

        public void EnsureContentLoaded()
        {
            if (ContentFrame.Content != null) return;

            var pageType = _lastContentPageType;
            if (pageType == typeof(Views.AddAccountPage))
                pageType = typeof(Views.CalendarPage);

            ContentFrame.Navigate(pageType);
        }

        public string GetDiagnosticsCurrentPageName()
        {
            if (ContentFrame.Content == null)
                return _lastContentPageType.Name + " (released)";

            return ContentFrame.Content.GetType().Name;
        }

        public bool HasDiagnosticsWebView2()
        {
            if (ContentFrame.Content is not DependencyObject content)
                return false;

            return ContainsWebView2(content);
        }

        public void ReleaseMailForMemoryPressure()
        {
            if (ContentFrame.Content is Views.MailPage mailPage)
                mailPage.ReleaseForMemoryPressure();
        }

        private void ReleaseContentForBackground()
        {
            if (ContentFrame.Content == null) return;

            if (ContentFrame.SourcePageType != null)
                _lastContentPageType = ContentFrame.SourcePageType;

            if (ContentFrame.Content is Views.MailPage mailPage)
                mailPage.DisposeLikeCleanup();
            else if (ContentFrame.Content is Views.RssPage rssPage)
                rssPage.DisposeLikeCleanup();

            ContentFrame.BackStack.Clear();
            ContentFrame.Content = null;
            MainNav.SelectedItem = null;
            MainNav.IsBackEnabled = false;
        }

        private static bool ContainsWebView2(DependencyObject root)
        {
            if (root is WebView2) return true;

            var childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                if (ContainsWebView2(VisualTreeHelper.GetChild(root, i)))
                    return true;
            }

            return false;
        }

        private Services.AccountManager? GetAccountManager()
        {
            if (App.Current is App app) return app.SyncManager.AccountManager;
            return null;
        }

        public async Task RefreshAccountListAsync()
        {
            try
            {
                if (ContentFrame.Content is CalendarPage page)
                {
                    page.RefreshAccountList();
                    return;
                }

                if (ContentFrame.Content is TasksPage tasksPage)
                {
                    tasksPage.RefreshAccountList();
                    return;
                }

                if (ContentFrame.Content is MailPage mailPage)
                {
                    await mailPage.RefreshAfterProviderDisconnectAsync();
                    return;
                }

                if (App.Current is App app)
                    await app.SyncManager.SyncAllCalendarsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RefreshAccountListAsync failed: {ex.Message}");
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
                    Title = GetSafeString("TextRemoveAccountTitle", "Remove Account"),
                    Content = string.Format(GetSafeString("TextProviderRemovalContent", "Remove {0} from Calendar and Tasks only, or disconnect it completely from Calendar, Tasks, and Mail?"), providerName),
                    PrimaryButtonText = GetSafeString("TextRemoveAgendaOnly", "Remove Calendar/Tasks only"),
                    SecondaryButtonText = GetSafeString("TextDisconnectProvider", "Disconnect completely"),
                    CloseButtonText = GetSafeString("CalendarDialog.CloseButtonText", "Cancel"),
                    XamlRoot = this.Content.XamlRoot,
                    DefaultButton = ContentDialogButton.Close
                };

                var result = await dialog.ShowAsync();
                if (result is ContentDialogResult.Primary or ContentDialogResult.Secondary)
                {
                    try
                    {
                        if (App.Current is App app)
                        {
                            if (result == ContentDialogResult.Secondary)
                                await app.DisconnectProviderCompletelyAsync(providerName);
                            else
                                await app.SyncManager.RemoveAgendaAccountAsync(providerName);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Account removal failed: {ex.Message}");
                        var errorDialog = new ContentDialog
                        {
                            Title = GetSafeString("TextRemoveAccountFailedTitle", "Account Not Removed"),
                            Content = GetSafeString("TextDisconnectProviderFailed", "The account change could not be completed. Existing account data was preserved so you can try again."),
                            CloseButtonText = GetSafeString("CalendarDialog.CloseButtonText", "Close"),
                            XamlRoot = this.Content.XamlRoot
                        };
                        await errorDialog.ShowAsync();
                        return;
                    }
                    _ = RefreshAccountListAsync();

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
            if (ContentFrame.Content is TasksPage tasksPage) tasksPage.ReloadFilters();

            if (App.MyFlyoutWindow is FlyoutWindow flyout)
            {
                flyout.ReloadFilters();
            }
        }

        private void BtnForceSync_Click(object sender, RoutedEventArgs e)
        {
            if (ContentFrame.Content is CalendarPage page) page.ForceSync();
            if (ContentFrame.Content is TasksPage tasksPage) tasksPage.ForceSync();
        }

        private void MainNav_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.IsSettingsInvoked) ContentFrame.Navigate(typeof(SettingsPage));
            else if (args.InvokedItemContainer is NavigationViewItem item && item.Tag?.ToString() == "Calendar")
                ContentFrame.Navigate(typeof(CalendarPage));
            else if (args.InvokedItemContainer is NavigationViewItem itemT && itemT.Tag?.ToString() == "Tasks")
                ContentFrame.Navigate(typeof(TasksPage));
            else if (args.InvokedItemContainer is NavigationViewItem itemW && itemW.Tag?.ToString() == "Weather")
                ContentFrame.Navigate(typeof(WeatherPage));
            else if (args.InvokedItemContainer is NavigationViewItem itemM && itemM.Tag?.ToString() == "Mail")
                ContentFrame.Navigate(typeof(MailPage));
            else if (args.InvokedItemContainer is NavigationViewItem itemR && itemR.Tag?.ToString() == "Rss")
                ContentFrame.Navigate(typeof(RssPage));
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

        public void NavigateToMail()
        {
            var mailItem = MainNav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag?.ToString() == "Mail");
            if (mailItem != null) MainNav.SelectedItem = mailItem;
            ContentFrame.Navigate(typeof(Views.MailPage));
        }

        public void NavigateToMailCompose()
        {
            if (ContentFrame.Content is Views.MailPage existingMailPage)
            {
                _ = existingMailPage.StartComposeAsync();
                return;
            }

            void ComposeAfterNavigate(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs args)
            {
                if (args.SourcePageType != typeof(Views.MailPage)) return;
                ContentFrame.Navigated -= ComposeAfterNavigate;
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (ContentFrame.Content is Views.MailPage mailPage)
                        _ = mailPage.StartComposeAsync();
                });
            }

            ContentFrame.Navigated += ComposeAfterNavigate;
            MainNav.SelectedItem = MainNav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag?.ToString() == "Mail");
            ContentFrame.Navigate(typeof(Views.MailPage));
        }

        public void NavigateToTasks()
        {
            var tasksItem = MainNav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag?.ToString() == "Tasks");
            if (tasksItem != null) MainNav.SelectedItem = tasksItem;
            ContentFrame.Navigate(typeof(Views.TasksPage));
        }

        public void NavigateToMailMessage(string accountId, string folderId, string messageId)
        {
            if (ContentFrame.Content is Views.MailPage existingMailPage)
            {
                _pendingMailMessageNavigation = true;
                OpenMailMessageOnPage(existingMailPage, accountId, folderId, messageId);
                return;
            }

            _pendingMailMessageNavigation = true;

            void OpenAfterNavigate(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs args)
            {
                if (args.SourcePageType != typeof(Views.MailPage)) return;

                ContentFrame.Navigated -= OpenAfterNavigate;
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (ContentFrame.Content is Views.MailPage mailPage)
                        OpenMailMessageOnPage(mailPage, accountId, folderId, messageId);
                });
            }

            ContentFrame.Navigated += OpenAfterNavigate;
            MainNav.SelectedItem = MainNav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag?.ToString() == "Mail");
            ContentFrame.Navigate(typeof(Views.MailPage));
        }

        public void NavigateToCachedMailMessage(string accountId, string folderId, string messageId)
        {
            async void Open(Views.MailPage page)
            {
                try { await page.OpenCachedMessageAsync(accountId, folderId, messageId); }
                finally
                {
                    page.IsOpeningFromNotification = false;
                    _pendingCachedMailNavigation = false;
                }
            }
            if (ContentFrame.Content is Views.MailPage existing)
            {
                Open(existing);
                return;
            }

            void OpenAfterNavigate(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs args)
            {
                if (args.SourcePageType != typeof(Views.MailPage)) return;
                ContentFrame.Navigated -= OpenAfterNavigate;
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (ContentFrame.Content is Views.MailPage page)
                    {
                        page.IsOpeningFromNotification = true;
                        Open(page);
                    }
                });
            }

            _pendingCachedMailNavigation = true;
            ContentFrame.Navigated += OpenAfterNavigate;
            MainNav.SelectedItem = MainNav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag?.ToString() == "Mail");
            ContentFrame.Navigate(typeof(Views.MailPage));
        }

        private void GlobalSearchAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            if (GlobalSearchOverlay.Visibility == Visibility.Visible)
                CloseGlobalSearch();
            else
                OpenGlobalSearch();
        }

        private void OpenGlobalSearch()
        {
            EnsureContentLoaded();
            _globalSearchPreviousFocus = FocusManager.GetFocusedElement(RootGrid.XamlRoot) as DependencyObject;
            BuildGlobalSearchSnapshot();
            GlobalSearchBox.Text = "";
            ApplyGlobalSearch("");
            MainNav.IsEnabled = false;
            GlobalSearchOverlay.Visibility = Visibility.Visible;
            UpdateGlobalSearchSize(RootGrid.ActualWidth, RootGrid.ActualHeight);
            DispatcherQueue.TryEnqueue(() => GlobalSearchBox.Focus(FocusState.Programmatic));
        }

        private void CloseGlobalSearch()
        {
            if (GlobalSearchOverlay.Visibility != Visibility.Visible) return;
            GlobalSearchOverlay.Visibility = Visibility.Collapsed;
            MainNav.IsEnabled = true;
            GlobalSearchResultsList.SelectedItem = null;
            if (_globalSearchPreviousFocus is Control control && control.IsEnabled && control.Visibility == Visibility.Visible)
                control.Focus(FocusState.Programmatic);
            else
                MainNav.Focus(FocusState.Programmatic);
            _globalSearchPreviousFocus = null;
        }

        private void BuildGlobalSearchSnapshot()
        {
            _globalSearchPayloads.Clear();
            var candidates = new List<GlobalSearchCandidate>();
            AddGlobalSearchCommands(candidates);

            if (App.Current is App app)
            {
                var accountManager = app.SyncManager.AccountManager;
                foreach (var item in app.SyncManager.GetLocalCache().DayItems.Values.SelectMany(items => items)
                             .Where(accountManager.IsItemVisible)
                             .GroupBy(item => $"{item.Provider}|{item.CalendarId}|{item.Id}|{item.DateKey}", StringComparer.Ordinal)
                             .Select(group => group.First()))
                {
                    var group = item.IsTask ? GlobalSearchGroupKind.Tasks : GlobalSearchGroupKind.Calendar;
                    var id = $"agenda:{group}:{item.Provider}:{item.CalendarId}:{item.Id}:{item.DateKey}";
                    _globalSearchPayloads[id] = item;
                    candidates.Add(new GlobalSearchCandidate(id, group, item.Title,
                        string.Join(" · ", new[] { item.DateKey, item.Location, item.Provider }.Where(value => !string.IsNullOrWhiteSpace(value))),
                        $"{item.Title} {item.Description} {item.Subtitle} {item.DateKey} {item.Location}",
                        item.StartDateTime.HasValue ? new DateTimeOffset(item.StartDateTime.Value) : null));
                }

                foreach (var mail in app.MailService.GetCachedMetadataSnapshot())
                {
                    var id = $"mail:{mail.AccountId}:{mail.FolderId}:{mail.MessageId}";
                    _globalSearchPayloads[id] = mail;
                    candidates.Add(new GlobalSearchCandidate(id, GlobalSearchGroupKind.Mail,
                        string.IsNullOrWhiteSpace(mail.Subject) ? GetSafeString("GlobalSearch_Untitled", "Untitled") : mail.Subject,
                        string.Join(" · ", new[] { mail.Sender, mail.ReceivedTime }.Where(value => !string.IsNullOrWhiteSpace(value))),
                        $"{mail.Subject} {mail.Sender} {mail.SenderAddress} {mail.Preview} {mail.ReceivedTime}", mail.ReceivedAt));
                }
            }

            var rssService = new RssService();
            var cachedArticles = new List<RssArticle>();
            for (int skip = 0; skip < 1_000; skip += 100)
            {
                var page = rssService.GetCachedArticlesPage(null, null, skip, 100);
                cachedArticles.AddRange(page);
                if (page.Count < 100) break;
            }
            foreach (var source in cachedArticles)
            {
                var article = new RssArticle
                {
                    Id = source.Id,
                    SubscriptionId = source.SubscriptionId,
                    FeedTitle = source.FeedTitle,
                    Title = source.Title,
                    Link = source.Link,
                    Summary = source.Summary,
                    HtmlContent = source.HtmlContent,
                    ImageUrl = source.ImageUrl,
                    LocalImagePath = source.LocalImagePath,
                    PublishedAt = source.PublishedAt,
                    IsRead = source.IsRead,
                    IsStarred = source.IsStarred
                };
                var id = $"rss:{article.SubscriptionId}:{article.Id}";
                _globalSearchPayloads[id] = article;
                candidates.Add(new GlobalSearchCandidate(id, GlobalSearchGroupKind.Rss, article.Title,
                    $"{article.FeedTitle} · {article.PublishedText}",
                    $"{article.Title} {article.Summary} {article.FeedTitle}", article.PublishedAt));
            }

            _globalSearchSnapshot = candidates;
        }

        private void AddGlobalSearchCommands(List<GlobalSearchCandidate> candidates)
        {
            AddCommand(candidates, "01-new-task", "GlobalSearch_CommandNewTask", "New task", "\uE73E");
            AddCommand(candidates, "02-new-event", "GlobalSearch_CommandNewEvent", "New event", "\uE787");
            AddCommand(candidates, "03-compose-mail", "GlobalSearch_CommandComposeMail", "Compose mail", "\uE70F");
            AddCommand(candidates, "04-sync-calendar", "GlobalSearch_CommandSyncCalendar", "Sync calendar", "\uE895");
            AddCommand(candidates, "05-sync-tasks", "GlobalSearch_CommandSyncTasks", "Sync tasks", "\uE895");
            AddCommand(candidates, "06-open-calendar", "GlobalSearch_CommandOpenCalendar", "Open calendar", "\uE787");
            AddCommand(candidates, "07-open-tasks", "GlobalSearch_CommandOpenTasks", "Open tasks", "\uE73E");
            AddCommand(candidates, "08-open-mail", "GlobalSearch_CommandOpenMail", "Open mail", "\uE715");
            AddCommand(candidates, "09-open-rss", "GlobalSearch_CommandOpenRss", "Open RSS", "\uE789");
            AddCommand(candidates, "10-open-settings", "GlobalSearch_CommandOpenSettings", "Open settings", "\uE713");
            AddCommand(candidates, "11-open-accounts", "GlobalSearch_CommandOpenAccounts", "Open accounts", "\uE77B");
            AddCommand(candidates, "12-cache-settings", "GlobalSearch_CommandCacheSettings", "Manage cache settings", "\uE74D");
            AddCommand(candidates, "13-open-weather", "GlobalSearch_CommandOpenWeather", "Open weather", "\uE706");
            AddCommand(candidates, "14-toggle-weather-bar", "GlobalSearch_CommandToggleWeatherBar", "Toggle Weather Bar", "\uE7F4");
        }

        private void AddCommand(List<GlobalSearchCandidate> candidates, string id, string resourceKey, string fallback, string glyph)
        {
            var title = GetSafeString(resourceKey, fallback);
            _globalSearchPayloads[id] = glyph;
            candidates.Add(new GlobalSearchCandidate(id, GlobalSearchGroupKind.Commands, title, "", title));
        }

        private void GlobalSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
                ApplyGlobalSearch(sender.Text);
        }

        private void ApplyGlobalSearch(string query)
        {
            _globalSearchGroups.Clear();
            foreach (var group in GlobalSearchPolicy.Search(_globalSearchSnapshot, query).GroupBy(item => item.Group))
            {
                var resultGroup = new GlobalSearchResultGroup(GetGlobalSearchGroupName(group.Key));
                foreach (var item in group)
                {
                    var glyph = item.Group == GlobalSearchGroupKind.Commands && _globalSearchPayloads.TryGetValue(item.Id, out var payload)
                        ? payload as string ?? "\uE71D"
                        : GetGlobalSearchGlyph(item.Group);
                    resultGroup.Add(new GlobalSearchResultViewModel(item, glyph));
                }
                _globalSearchGroups.Add(resultGroup);
            }
            GlobalSearchResultsList.SelectedItem = _globalSearchGroups.SelectMany(group => group).FirstOrDefault();
        }

        private string GetGlobalSearchGroupName(GlobalSearchGroupKind group) => group switch
        {
            GlobalSearchGroupKind.Commands => GetSafeString("GlobalSearch_GroupCommands", "Commands"),
            GlobalSearchGroupKind.Tasks => GetSafeString("GlobalSearch_GroupTasks", "Tasks"),
            GlobalSearchGroupKind.Calendar => GetSafeString("GlobalSearch_GroupCalendar", "Calendar"),
            GlobalSearchGroupKind.Mail => GetSafeString("GlobalSearch_GroupMail", "Mail"),
            _ => GetSafeString("GlobalSearch_GroupRss", "RSS")
        };

        private static string GetGlobalSearchGlyph(GlobalSearchGroupKind group) => group switch
        {
            GlobalSearchGroupKind.Tasks => "\uE73E",
            GlobalSearchGroupKind.Calendar => "\uE787",
            GlobalSearchGroupKind.Mail => "\uE715",
            GlobalSearchGroupKind.Rss => "\uE789",
            _ => "\uE71D"
        };

        private void GlobalSearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                CloseGlobalSearch();
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Enter)
            {
                ActivateSelectedGlobalSearchResult();
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Down)
            {
                GlobalSearchResultsList.Focus(FocusState.Programmatic);
                e.Handled = true;
            }
        }

        private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (GlobalSearchOverlay.Visibility == Visibility.Visible && e.Key == Windows.System.VirtualKey.Escape)
            {
                CloseGlobalSearch();
                e.Handled = true;
            }
        }

        private void GlobalSearchResultsList_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                ActivateSelectedGlobalSearchResult();
                e.Handled = true;
            }
        }

        private void GlobalSearchResultsList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is GlobalSearchResultViewModel result) ActivateGlobalSearchResult(result);
        }

        private void ActivateSelectedGlobalSearchResult()
        {
            if (GlobalSearchResultsList.SelectedItem is GlobalSearchResultViewModel result)
                ActivateGlobalSearchResult(result);
        }

        private void ActivateGlobalSearchResult(GlobalSearchResultViewModel result)
        {
            CloseGlobalSearch();
            var item = result.Candidate;
            if (item.Group == GlobalSearchGroupKind.Commands)
            {
                ExecuteGlobalSearchCommand(item.Id);
                return;
            }
            if (!_globalSearchPayloads.TryGetValue(item.Id, out var payload)) return;

            if (payload is AgendaItem agenda)
            {
                NavigateToCalendarAndEdit(agenda);
            }
            else if (payload is CachedMailMetadata mail)
            {
                NavigateToCachedMailMessage(mail.AccountId, mail.FolderId, mail.MessageId);
            }
            else if (payload is RssArticle article)
            {
                NavigateToCachedRssArticle(article);
            }
        }

        private void ExecuteGlobalSearchCommand(string id)
        {
            switch (id)
            {
                case "01-new-task": NavigateToCalendarAndCreate(isTask: true); break;
                case "02-new-event": NavigateToCalendarAndCreate(isTask: false); break;
                case "03-compose-mail": NavigateToMailCompose(); break;
                case "04-sync-calendar": NavigateToCalendarAndSync(); break;
                case "05-sync-tasks": NavigateToTasksAndSync(); break;
                case "06-open-calendar": NavigateToCalendar(); break;
                case "07-open-tasks": NavigateToTasks(); break;
                case "08-open-mail": NavigateToMail(); break;
                case "09-open-rss": NavigateToRss(); break;
                case "10-open-settings":
                case "12-cache-settings": NavigateToSettings(); break;
                case "11-open-accounts": NavigateToAddAccount(); break;
                case "13-open-weather": NavigateToWeather(); break;
                case "14-toggle-weather-bar": ToggleWeatherBarFromPalette(); break;
            }
        }

        private static void ToggleWeatherBarFromPalette()
        {
            bool enabled = ApplicationData.Current.LocalSettings.Values["WeatherBarEnabled"] as bool? ?? false;
            App.ToggleWeatherBar(!enabled);
        }

        private void NavigateToCalendar()
        {
            MainNav.SelectedItem = MainNav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag?.ToString() == "Calendar");
            ContentFrame.Navigate(typeof(Views.CalendarPage));
        }

        private void NavigateToRss()
        {
            MainNav.SelectedItem = MainNav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => i.Tag?.ToString() == "Rss");
            ContentFrame.Navigate(typeof(Views.RssPage));
        }

        private void NavigateToCalendarAndCreate(bool isTask)
        {
            NavigateToCalendar();
            DispatcherQueue.TryEnqueue(() =>
            {
                if (ContentFrame.Content is Views.CalendarPage page) page.OpenNewDialog(isTask);
            });
        }

        private void NavigateToCalendarAndSync()
        {
            NavigateToCalendar();
            DispatcherQueue.TryEnqueue(() => (ContentFrame.Content as Views.CalendarPage)?.ForceSync());
        }

        private void NavigateToTasksAndSync()
        {
            NavigateToTasks();
            DispatcherQueue.TryEnqueue(() => (ContentFrame.Content as Views.TasksPage)?.ForceSync());
        }

        private void NavigateToCachedRssArticle(RssArticle article)
        {
            NavigateToRss();
            DispatcherQueue.TryEnqueue(() =>
            {
                if (ContentFrame.Content is Views.RssPage page) _ = page.OpenCachedArticleAsync(article);
            });
        }

        private void GlobalSearchCloseButton_Click(object sender, RoutedEventArgs e) => CloseGlobalSearch();

        private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateGlobalSearchSize(e.NewSize.Width, e.NewSize.Height);

        private void UpdateGlobalSearchSize(double width, double height)
        {
            if (GlobalSearchPanel == null) return;
            GlobalSearchPanel.Width = Math.Max(280, Math.Min(720, width - 32));
            GlobalSearchPanel.MaxHeight = Math.Max(260, Math.Min(640, height - 96));
            GlobalSearchPanel.Margin = new Thickness(16, height < 560 ? 24 : 72, 16, 16);
        }

        private async void OpenMailMessageOnPage(Views.MailPage mailPage, string accountId, string folderId, string messageId)
        {
            mailPage.IsOpeningFromNotification = true;
            try
            {
                await mailPage.OpenMessageAsync(accountId, folderId, messageId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Open mail notification target failed: {ex.Message}");
            }
            finally
            {
                mailPage.IsOpeningFromNotification = false;
                _pendingMailMessageNavigation = false;
            }
        }

        public void NavigateToAddAccount()
        {
            ContentFrame.Navigate(typeof(Views.AddAccountPage));
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

        private const string FluentIconsFont = "ms-appx:///Assets/FluentSystemIcons-Filled.ttf#FluentSystemIcons-Filled";

        public async Task RefreshWeatherNavIconAsync()
        {
            try
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RefreshWeatherNavIconAsync failed: {ex.Message}");
            }
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

    public sealed class GlobalSearchResultGroup : ObservableCollection<GlobalSearchResultViewModel>
    {
        public GlobalSearchResultGroup(string name) => Name = name;
        public string Name { get; }
    }

    public sealed class GlobalSearchResultViewModel
    {
        public GlobalSearchResultViewModel(GlobalSearchCandidate candidate, string glyph)
        {
            Candidate = candidate;
            Glyph = glyph;
        }

        public GlobalSearchCandidate Candidate { get; }
        public string Title => Candidate.Title;
        public string Detail => Candidate.Detail;
        public string Glyph { get; }
        public string AutomationId => $"GlobalSearchResult_{Candidate.Id}";
    }
}
