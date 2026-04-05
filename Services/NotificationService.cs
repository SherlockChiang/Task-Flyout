using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System;
using System.Collections.Generic;
using System.Linq;
using Task_Flyout.Models;
using Microsoft.UI.Xaml;

namespace Task_Flyout.Services
{
    public class NotificationService
    {
        private readonly SyncManager _syncManager;
        private readonly HashSet<string> _notifiedIds = new();
        private DispatcherTimer _timer;
        private int _reminderMinutes = 15;

        public NotificationService(SyncManager syncManager)
        {
            _syncManager = syncManager;
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

        public void StartPeriodicCheck()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _timer.Tick += (s, e) => CheckUpcomingEvents();
            _timer.Start();

            // Check immediately on start
            CheckUpcomingEvents();
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

                // Clean up old notification IDs (keep last 500)
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
            if (string.IsNullOrEmpty(subtitle) || subtitle == "全天" || subtitle == "All day")
                return null;

            // Parse time from subtitle like "09:00 - 10:00" or "09:00"
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
                var timeText = minutesLeft <= 1 ? "1 分钟后" : $"{minutesLeft} 分钟后";

                var builder = new AppNotificationBuilder()
                    .AddText(item.Title ?? "日程提醒")
                    .AddText($"{timeText}开始 · {startTime:HH:mm}");

                if (!string.IsNullOrEmpty(item.Location))
                    builder.AddText($"📍 {item.Location}");

                var notification = builder.BuildNotification();
                AppNotificationManager.Default.Show(notification);

                System.Diagnostics.Debug.WriteLine($"Notification sent: {item.Title} at {startTime:HH:mm}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Send notification failed: {ex.Message}");
            }
        }

        private Dictionary<string, List<AgendaItem>> LoadCacheItems()
        {
            try
            {
                var cachePath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "TaskFlyout", "local_cache_winui3.json");

                if (!System.IO.File.Exists(cachePath)) return null;

                var json = System.IO.File.ReadAllText(cachePath);
                var cache = System.Text.Json.JsonSerializer.Deserialize<AppCacheDto>(json);
                return cache?.DayItems;
            }
            catch { return null; }
        }

        private bool IsItemVisible(AgendaItem item)
        {
            return _syncManager?.AccountManager?.IsItemVisible(item) ?? true;
        }

        private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
        {
            // When user clicks the notification, show the main window
            App.MainDispatcherQueue?.TryEnqueue(() =>
            {
                App.OpenMainWindowInternal();
            });
        }

        // DTO to deserialize cache without depending on FlyoutWindow's AppCache
        private class AppCacheDto
        {
            public HashSet<string> MarkedDates { get; set; } = new();
            public Dictionary<string, List<AgendaItem>> DayItems { get; set; } = new();
        }
    }
}
