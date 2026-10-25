using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Task_Flyout.Models;
using Task_Flyout.Services;

namespace Task_Flyout.Views;

public sealed partial class TodayPage : Page
{
    private readonly ResourceLoader _loader = new();
    private readonly RssService _rss = new();
    private readonly Dictionary<TodayCard, long> _versions = new();
    private readonly HashSet<TodayCard> _running = new();
    private readonly HashSet<TodayCard> _pending = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };
    private App? _app;
    private long _version;

    public TodayPage()
    {
        InitializeComponent();
        Configure(AgendaCard, "Agenda", () => Frame.Navigate(typeof(CalendarPage)));
        Configure(TasksCard, "Tasks", () => Frame.Navigate(typeof(TasksPage)));
        Configure(MailCard, "Mail", () => Frame.Navigate(typeof(MailPage)));
        Configure(WeatherCard, "Weather", () => Frame.Navigate(typeof(WeatherPage)));
        Configure(RssCard, "Rss", () => Frame.Navigate(typeof(RssPage)));
        Loaded += Page_Loaded;
        Unloaded += Page_Unloaded;
        _timer.Tick += SourcesChanged;
    }

    private void Configure(TodayCard card, string key, Action open)
    {
        card.Configure(Text("Today_" + key), Text("Today_Open" + key), "Today" + key);
        card.OpenRequested += open;
        card.RetryRequested += () => _ = RefreshCardAsync(card);
        card.ItemRequested += item =>
        {
            if (_app?.SyncManager.AccountManager.IsItemVisible(item) == true)
                App.MyMainWindow?.NavigateToCalendarAndEdit(item);
        };
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_app != null || Application.Current is not App app) return;
        _app = app;
        app.SyncManager.CachePublished += AgendaChanged;
        app.SyncManager.AccountManager.Changed += SourcesChanged;
        app.MailService.CachePublished += MailChanged;
        app.MailService.AccountsChanged += SourcesChanged;
        app.WeatherService.LocationsChanged += SourcesChanged;
        RssService.SearchMetadataChanged += SourcesChanged;
        RssService.LocalDataCleared += SourcesChanged;
        _timer.Start();
        _ = RefreshAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_app is not App app) return;
        _app = null;
        _versions.Clear(); // In-flight reads must never publish into a detached page.
        _pending.Clear();
        _timer.Stop();
        app.SyncManager.CachePublished -= AgendaChanged;
        app.SyncManager.AccountManager.Changed -= SourcesChanged;
        app.MailService.CachePublished -= MailChanged;
        app.MailService.AccountsChanged -= SourcesChanged;
        app.WeatherService.LocationsChanged -= SourcesChanged;
        RssService.SearchMetadataChanged -= SourcesChanged;
        RssService.LocalDataCleared -= SourcesChanged;
    }

    private void AgendaChanged(object? sender, AgendaCachePublishedEventArgs e) => SourcesChanged(sender, EventArgs.Empty);
    private void MailChanged(object? sender, MailCachePublishedEventArgs e) => SourcesChanged(sender, EventArgs.Empty);
    private void SourcesChanged(object? sender, object e)
        => DispatcherQueue.TryEnqueue(() => { if (_app != null) _ = RefreshAsync(); });

    private Task RefreshAsync()
    {
        DateText.Text = DateTime.Now.ToString("D", LocalizationHelper.AppCulture);
        return Task.WhenAll(Cards.Select(RefreshCardAsync));
    }

    private TodayCard[] Cards => new[] { AgendaCard, TasksCard, MailCard, WeatherCard, RssCard };

    private async Task RefreshCardAsync(TodayCard card)
    {
        if (_app is not App app) return;
        long version = ++_version;
        _versions[card] = version;
        if (!_running.Add(card))
        {
            _pending.Add(card);
            return;
        }
        card.SetLoading(Text("Today_Loading"));
        bool IsCurrent() => _app == app && _versions.TryGetValue(card, out long current) && current == version;

        try
        {
            if (card == AgendaCard || card == TasksCard)
            {
                var isVisible = app.SyncManager.AccountManager.CaptureSearchVisibility();
                var now = DateTime.Now;
                bool tasks = card == TasksCard;
                var result = await Task.Run(() =>
                {
                    var items = (tasks ? app.SyncManager.GetTaskCacheSnapshot() : app.SyncManager.GetLocalCache())
                        .DayItems.Values.SelectMany(bucket => bucket).Where(isVisible)
                        .Where(item => item.IsTask || item.IsEvent)
                        .DistinctBy(Identity).ToDictionary(Identity);
                    var entries = items.Values.Select(item => new TodayAgendaEntry(Identity(item), item.Title,
                        item.Subtitle, item.DateKey, item.StartDateTime, item.EndDateTime, item.IsTask, item.IsCompleted));
                    var selected = tasks ? TodayDashboardPolicy.DueTasks(entries, now) : TodayDashboardPolicy.Upcoming(entries, now);
                    return (Count: selected.Count, Items: selected.Take(5).Select(item => items[item.Key]).ToArray());
                });
                if (!IsCurrent()) return;
                var rows = result.Items.Select(item => new TodayCardRow(item.Title, RowSubtitle(item), item)).ToArray();
                card.SetResult(tasks ? result.Count.ToString(LocalizationHelper.AppCulture) : "",
                    Text(result.Count == 0 ? tasks ? "Today_NoTasks" : "Today_NoAgenda" : tasks ? "Today_TaskHint" : "Today_AgendaHint"), rows);
            }
            else if (card == MailCard)
            {
                var accounts = app.MailService.GetAccounts().Where(account => account.IsSetupComplete).Select(account => account.Id).ToArray();
                var counts = await Task.Run(() => accounts.Select(id => TodayDashboardPolicy.InboxUnread(
                    app.MailService.GetCachedFolderSnapshot(id).Select(folder => (folder.Id, folder.DisplayName, folder.UnreadCount)))).ToArray());
                if (!IsCurrent()) return;
                bool known = counts.Length > 0 && counts.All(count => count.HasValue);
                long total = counts.Sum(count => count ?? 0);
                card.SetResult(known ? total.ToString(LocalizationHelper.AppCulture) : "—",
                    Text(accounts.Length == 0 ? "Today_NoMailAccount" : !known ? "Today_MailUnknown" : total == 0 ? "Today_NoUnreadMail" : "Today_MailHint"));
            }
            else if (card == WeatherCard)
            {
                var weather = await Task.Run(app.WeatherService.GetActiveCachedWeatherState);
                if (!IsCurrent()) return;
                card.SetResult(weather.Info == null ? "—" : string.Join(" · ", new[] { weather.Info.Temperature, weather.Info.Description }.Where(value => !string.IsNullOrWhiteSpace(value))),
                    weather.Info == null ? Text("Today_NoWeather") : string.Format(LocalizationHelper.AppCulture, Text("Today_WeatherUpdated"),
                        weather.UpdatedUtc.ToLocalTime().ToString("g", LocalizationHelper.AppCulture)));
            }
            else
            {
                long count = await _rss.GetUnreadCountAsync();
                if (!IsCurrent()) return;
                card.SetResult(count.ToString(LocalizationHelper.AppCulture), Text(count == 0 ? "Today_NoUnreadRss" : "Today_RssHint"));
            }
        }
        catch (Exception ex)
        {
            if (!IsCurrent()) return;
            card.SetError(Text("Today_ReadFailed"), Text("Today_RetryHint"));
            System.Diagnostics.Debug.WriteLine($"Today card read failed: {ex.GetType().Name}");
        }
        finally
        {
            _running.Remove(card);
            if (_pending.Remove(card) && _app != null) _ = RefreshCardAsync(card);
        }
    }

    private static string Identity(AgendaItem item) => GlobalSearchPolicy.CreateAgendaId(
        item.Provider, item.AccountId, item.CalendarId, item.Id, item.DateKey, item.IsTask);

    private string RowSubtitle(AgendaItem item)
    {
        string date = item.StartDateTime?.ToString("g", LocalizationHelper.AppCulture)
            ?? (DateTime.TryParseExact(item.DateKey, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                ? day.ToString("d", LocalizationHelper.AppCulture) : item.DateKey);
        string account = _app?.SyncManager.AccountManager.GetAccount(item.Provider, item.AccountId)?.DisplayName ?? item.Provider;
        return string.Join(" · ", new[] { date, account, item.CalendarName }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private string Text(string key) => _loader.GetStringOrDefault(key) ?? key;
    private void RefreshButton_Click(object sender, RoutedEventArgs e) => _ = RefreshAsync();
    private void NewTaskButton_Click(object sender, RoutedEventArgs e) => App.MyMainWindow?.NavigateToCalendarAndCreate(true);
    private void NewEventButton_Click(object sender, RoutedEventArgs e) => App.MyMainWindow?.NavigateToCalendarAndCreate(false);
    private void ComposeButton_Click(object sender, RoutedEventArgs e) => App.MyMainWindow?.NavigateToMailCompose();

    private void CardsGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool narrow = e.NewSize.Width < 680;
        var cards = Cards;
        for (int index = 0; index < cards.Length; index++)
        {
            Grid.SetRow(cards[index], narrow ? index : index / 2);
            Grid.SetColumn(cards[index], narrow ? 0 : index % 2);
            Grid.SetColumnSpan(cards[index], narrow ? 2 : 1);
        }
        var actions = new[] { NewTaskButton, NewEventButton, ComposeButton };
        for (int index = 0; index < actions.Length; index++)
        {
            Grid.SetRow(actions[index], narrow ? index : 0);
            Grid.SetColumn(actions[index], narrow ? 0 : index);
            Grid.SetColumnSpan(actions[index], narrow ? 3 : 1);
        }
    }
}
