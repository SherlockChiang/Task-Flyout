using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Microsoft.Windows.ApplicationModel.Resources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Task_Flyout.Models;
using Microsoft.UI.Xaml;
using Windows.Storage;

namespace Task_Flyout.Services
{
    public class NotificationService
    {
        private readonly SyncManager _syncManager;
        private readonly HashSet<string> _notifiedIds = new();
        private readonly ResourceLoader _loader = new();
        private DispatcherTimer? _timer;
        private int _reminderMinutes;
        private Dictionary<string, List<AgendaItem>>? _cachedItems;
        private DateTime _cacheReadTime = DateTime.MinValue;
        private static readonly TimeSpan CacheRefreshInterval = TimeSpan.FromMinutes(5);

        public NotificationService(SyncManager syncManager)
        {
            _syncManager = syncManager;
            _reminderMinutes = ApplicationData.Current.LocalSettings.Values["NotifyMinutes"] as int? ?? 15;
        }

        public void Initialize()
        {
            try
            {
                var notificationManager = AppNotificationManager.Default;
                notificationManager.NotificationInvoked += OnNotificationInvoked;
                notificationManager.Register();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Notification init failed: {ex.Message}");
            }
        }

        public bool IsEnabled => ApplicationData.Current.LocalSettings.Values["NotifyEnabled"] as bool? ?? true;

        public void SetReminderMinutes(int minutes)
        {
            _reminderMinutes = minutes;
        }

        public void StartPeriodicCheck()
        {
            if (_timer != null)
            {
                _timer.Stop();
            }
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _timer.Tick += (s, e) => CheckUpcomingEvents();
            _timer.Start();

            CheckUpcomingEvents();
        }

        public void StopTimer()
        {
            _timer?.Stop();
        }

        public void Stop()
        {
            _timer?.Stop();
            try
            {
                AppNotificationManager.Default.Unregister();
            }
            catch { }
        }

        public void CheckUpcomingEvents()
        {
            if (!IsEnabled) return;

            try
            {
                var cache = LoadCacheItems();
                if (cache == null || cache.Count == 0) return;

                var now = DateTime.Now;
                var todayKey = now.ToString("yyyy-MM-dd");
                var tomorrowKey = now.AddDays(1).ToString("yyyy-MM-dd");

                var keysToCheck = new[] { todayKey, tomorrowKey };

                foreach (var dateKey in keysToCheck)
                {
                    if (!cache.ContainsKey(dateKey)) continue;

                    foreach (var item in cache[dateKey])
                    {
                        if (!IsItemVisible(item)) continue;
                        if (string.IsNullOrEmpty(item.Id)) continue;

                        var notifKey = $"{item.Id}_{dateKey}";
                        if (_notifiedIds.Contains(notifKey)) continue;

                        var eventStart = GetEventStartTime(item, dateKey);
                        if (eventStart == null) continue;

                        var minutesUntilStart = (eventStart.Value - now).TotalMinutes;

                        if (minutesUntilStart > 0 && minutesUntilStart <= _reminderMinutes)
                        {
                            SendNotification(item, eventStart.Value);
                            _notifiedIds.Add(notifKey);
                        }
                    }
                }

                if (_notifiedIds.Count > 500)
                {
                    var toRemove = _notifiedIds.Take(_notifiedIds.Count - 200).ToList();
                    foreach (var id in toRemove) _notifiedIds.Remove(id);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Notification check error: {ex.Message}");
            }
        }

        private DateTime? GetEventStartTime(AgendaItem item, string dateKey)
        {
            if (item.StartDateTime.HasValue)
                return item.StartDateTime.Value;

            if (!DateTime.TryParse(dateKey, out var date)) return null;

            var subtitle = item.Subtitle?.Trim();
            var allDayText = _loader.GetString("TextAllDay") ?? "All Day";
            if (string.IsNullOrEmpty(subtitle) || subtitle == "全天" || subtitle == "All day" || subtitle == allDayText)
                return null;

            var timePart = subtitle.Split('-')[0].Trim();
            if (TimeSpan.TryParse(timePart, out var timeSpan))
                return date.Add(timeSpan);

            return null;
        }

        private void SendNotification(AgendaItem item, DateTime startTime)
        {
            try
            {
                var minutesLeft = (int)Math.Ceiling((startTime - DateTime.Now).TotalMinutes);
                var timeText = minutesLeft <= 1
                    ? (_loader.GetString("TextOneMinuteLater") ?? "Starts in 1 minute")
                    : string.Format(_loader.GetString("TextMinutesLater") ?? "Starts in {0} minutes", minutesLeft);

                var builder = new AppNotificationBuilder()
                    .AddText(item.Title ?? (_loader.GetString("TextEventReminder") ?? "Event Reminder"))
                    .AddText($"{timeText} · {startTime:HH:mm}");

                if (!string.IsNullOrEmpty(item.Location))
                    builder.AddText($"\ud83d\udccd {item.Location}");

                var notification = builder.BuildNotification();
                AppNotificationManager.Default.Show(notification);

                System.Diagnostics.Debug.WriteLine($"Notification sent: {item.Title} at {startTime:HH:mm}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Send notification failed: {ex.Message}");
            }
        }

        private Dictionary<string, List<AgendaItem>>? LoadCacheItems()
        {
            if (_cachedItems != null && DateTime.Now - _cacheReadTime < CacheRefreshInterval)
                return _cachedItems;

            try
            {
                var cache = _syncManager.GetLocalCache();
                _cachedItems = cache.DayItems;
                _cacheReadTime = DateTime.Now;
                return _cachedItems;
            }
            catch { return _cachedItems; }
        }

        private bool IsItemVisible(AgendaItem item)
        {
            return _syncManager?.AccountManager?.IsItemVisible(item) ?? true;
        }

        private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
        {
            OpenFromActivationArguments(args.Argument);
        }

        public static void OpenFromActivationArguments(string argument)
        {
            var arguments = ParseArguments(argument);
            App.MainDispatcherQueue?.TryEnqueue(() =>
            {
                if (arguments.TryGetValue("action", out var action) &&
                    action == "openMail" &&
                    arguments.TryGetValue("accountId", out var accountId) &&
                    arguments.TryGetValue("folderId", out var folderId) &&
                    arguments.TryGetValue("messageId", out var messageId))
                {
                    App.OpenMainWindowInternal(window => window.NavigateToMailMessage(accountId, folderId, messageId));
                }
                else
                {
                    App.OpenMainWindowInternal();
                }
            });
        }

        private static Dictionary<string, string> ParseArguments(string argument)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(argument)) return result;

            argument = WebUtility.HtmlDecode(argument).Trim().Trim('"');
            const string activationPrefix = "----AppNotificationActivated:";
            var prefixIndex = argument.IndexOf(activationPrefix, StringComparison.OrdinalIgnoreCase);
            if (prefixIndex >= 0)
                argument = argument[(prefixIndex + activationPrefix.Length)..].Trim().Trim('"');

            foreach (var pair in argument.Split(new[] { '&', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length != 2) continue;
                result[WebUtility.UrlDecode(parts[0])] = WebUtility.UrlDecode(parts[1]);
            }

            return result;
        }

    }
}
