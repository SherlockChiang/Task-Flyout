using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Microsoft.Windows.ApplicationModel.Resources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Task_Flyout.Models;
using Microsoft.UI.Xaml;
using Windows.Storage;

namespace Task_Flyout.Services
{
    public class NotificationService
    {
        private readonly SyncManager _syncManager;
        // UI-thread only: all mutation paths (ctor LoadNotifiedIds, the DispatcherTimer
        // tick that drives CheckUpcomingEvents, and OpenFromActivationArguments which
        // marshals through MainDispatcherQueue) execute on the main UI thread. Do not
        // add background-thread callers without wrapping access in a lock first.
        private readonly Dictionary<string, DateTimeOffset> _notifiedIds = new(StringComparer.Ordinal);
        private readonly ResourceLoader _loader = new();
        private DispatcherTimer? _timer;
        private int _reminderMinutes;
        private Dictionary<string, List<AgendaItem>>? _cachedItems;
        private DateTime _cacheReadTime = DateTime.MinValue;
        private const string NotifiedIdsSettingsKey = "NotificationService.NotifiedIds";
        private const string SnoozedActionsSettingsKey = "NotificationService.SnoozedActions";
        private readonly Dictionary<string, DateTimeOffset> _snoozedActions = new(StringComparer.Ordinal);
        private bool _processingSnoozes;
        private static readonly TimeSpan CacheRefreshInterval = TimeSpan.FromMinutes(5);

        public NotificationService(SyncManager syncManager)
        {
            _syncManager = syncManager;
            _reminderMinutes = ApplicationData.Current.LocalSettings.Values["NotifyMinutes"] as int? ?? 15;
            LoadNotifiedIds();
            LoadSnoozedActions();
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

        private static bool HideNotificationContent
            => ApplicationData.Current.LocalSettings.Values["HideNotificationContent"] as bool? ?? true;

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
                AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
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
                bool notifiedIdsChanged = false;

                _ = ProcessDueSnoozesAsync(now);

                foreach (var dateKey in keysToCheck)
                {
                    if (!cache.ContainsKey(dateKey)) continue;

                    foreach (var item in cache[dateKey])
                    {
                        if (!IsItemVisible(item)) continue;
                        if (string.IsNullOrEmpty(item.Id)) continue;

                        var eventStart = GetEventStartTime(item, dateKey);
                        if (eventStart == null) continue;

                        var notifKey = BuildNotificationKey(item, dateKey, eventStart.Value);
                        if (_notifiedIds.ContainsKey(notifKey)) continue;

                        var minutesUntilStart = (eventStart.Value - now).TotalMinutes;

                        if (minutesUntilStart > 0 && minutesUntilStart <= _reminderMinutes)
                        {
                            SendNotification(item, eventStart.Value);
                            _notifiedIds[notifKey] = DateTimeOffset.UtcNow;
                            notifiedIdsChanged = true;
                        }
                    }
                }

                notifiedIdsChanged |= PruneNotifiedIds();
                if (notifiedIdsChanged)
                    SaveNotifiedIds();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Notification check error: {ex.Message}");
            }
        }

        private DateTime? GetEventStartTime(AgendaItem item, string dateKey)
        {
            var allDayText = _loader.GetStringOrDefault("TextAllDay") ?? "All Day";
            var subtitle = item.Subtitle?.Trim();
            if (!item.IsTask && (subtitle == allDayText || subtitle == "全天")) return null;
            return NotificationActionPolicy.GetReminderTime(item.IsTask, item.IsCompleted, item.StartDateTime, subtitle, dateKey);
        }

        private void SendNotification(AgendaItem item, DateTime startTime)
        {
            try
            {
                var minutesLeft = (int)Math.Ceiling((startTime - DateTime.Now).TotalMinutes);
                var timeText = item.IsTask
                    ? (minutesLeft <= 1
                        ? (_loader.GetStringOrDefault("TextTaskDueOneMinute") ?? "Due in 1 minute")
                        : string.Format(_loader.GetStringOrDefault("TextTaskDueMinutes") ?? "Due in {0} minutes", minutesLeft))
                    : (minutesLeft <= 1
                        ? (_loader.GetStringOrDefault("TextOneMinuteLater") ?? "Starts in 1 minute")
                        : string.Format(_loader.GetStringOrDefault("TextMinutesLater") ?? "Starts in {0} minutes", minutesLeft));

                var builder = new AppNotificationBuilder()
                    .AddText(HideNotificationContent
                        ? (item.IsTask
                            ? (_loader.GetStringOrDefault("TextTaskReminder") ?? "Task Reminder")
                            : (_loader.GetStringOrDefault("TextEventReminder") ?? "Event Reminder"))
                        : item.Title ?? (_loader.GetStringOrDefault("TextEventReminder") ?? "Event Reminder"))
                    .AddText($"{timeText} · {startTime:HH:mm}");

                var actions = NotificationActionPolicy.GetActions(item.IsTask, item.IsCompleted);
                if (!string.Equals(item.Provider, "Google", StringComparison.OrdinalIgnoreCase))
                    actions &= ~NotificationActionMask.Complete;
                var target = new NotificationActionTarget(
                    NotificationActionTarget.CurrentSchemaVersion,
                    item.Provider,
                    item.Id,
                    item.DateKey,
                    actions,
                    DateTimeOffset.UtcNow.AddDays(2));
                var token = NotificationActionStore.Store(target);
                builder.AddArgument("action", "openAgenda").AddArgument("token", token);
                if (actions.HasFlag(NotificationActionMask.Snooze))
                    builder.AddButton(new AppNotificationButton(_loader.GetStringOrDefault("Notification_Snooze") ?? "Snooze 10 min")
                        .AddArgument("action", "snoozeAgenda").AddArgument("token", token));
                if (actions.HasFlag(NotificationActionMask.Complete))
                    builder.AddButton(new AppNotificationButton(_loader.GetStringOrDefault("Notification_Complete") ?? "Complete")
                        .AddArgument("action", "completeTask").AddArgument("token", token));

                if (!HideNotificationContent && !string.IsNullOrEmpty(item.Location))
                    builder.AddText($"\ud83d\udccd {item.Location}");

                var notification = builder.BuildNotification();
                AppNotificationManager.Default.Show(notification);

                System.Diagnostics.Debug.WriteLine($"Agenda notification sent at {startTime:HH:mm}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Send notification failed: {ex.Message}");
            }
        }

        private void LoadNotifiedIds()
        {
            var raw = ApplicationData.Current.LocalSettings.Values[NotifiedIdsSettingsKey] as string ?? "";
            foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = line.Split('|', 2);
                var key = WebUtility.UrlDecode(parts[0]);
                if (string.IsNullOrWhiteSpace(key)) continue;

                if (parts.Length == 2 && long.TryParse(parts[1], out var ticks))
                    _notifiedIds[key] = new DateTimeOffset(ticks, TimeSpan.Zero);
                else
                    _notifiedIds[key] = DateTimeOffset.UtcNow;
            }
        }

        private void SaveNotifiedIds()
        {
            ApplicationData.Current.LocalSettings.Values[NotifiedIdsSettingsKey] = string.Join('\n',
                _notifiedIds.Select(kvp => $"{WebUtility.UrlEncode(kvp.Key)}|{kvp.Value.UtcTicks}"));
        }

        // Use minute-precision rather than ticks so that the same event re-fetched
        // with a different DateTime.Kind / timezone offset (e.g. roaming laptop) still
        // produces a stable dedupe key for a given local start time.
        private static string BuildNotificationKey(AgendaItem item, string dateKey, DateTime eventStart)
            => $"{item.Provider}|{item.Id}|{dateKey}|{eventStart:HHmm}";

        private bool PruneNotifiedIds()
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-14);
            var removed = false;

            foreach (var key in _notifiedIds.Where(kvp => kvp.Value < cutoff).Select(kvp => kvp.Key).ToList())
            {
                _notifiedIds.Remove(key);
                removed = true;
            }

            if (_notifiedIds.Count > 500)
            {
                foreach (var key in _notifiedIds
                             .OrderBy(kvp => kvp.Value)
                             .Take(_notifiedIds.Count - 300)
                             .Select(kvp => kvp.Key)
                             .ToList())
                {
                    _notifiedIds.Remove(key);
                    removed = true;
                }
            }

            return removed;
        }

        private Dictionary<string, List<AgendaItem>>? LoadCacheItems()
        {
            if (_cachedItems != null && DateTime.Now - _cacheReadTime < CacheRefreshInterval)
                return _cachedItems;

            try
            {
                var now = DateTime.Now;
                _cachedItems = _syncManager.GetDayItemsSnapshot(new[]
                {
                    now.ToString("yyyy-MM-dd"),
                    now.AddDays(1).ToString("yyyy-MM-dd")
                });
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
            var arguments = NotificationActivationParser.ParseArguments(argument);
            App.MainDispatcherQueue?.TryEnqueue(async () =>
            {
                if (NotificationActivationParser.TryParseAgendaAction(argument, out var agendaAction, out var agendaToken))
                {
                    if (App.Current is App app)
                        await app.NotificationService.HandleAgendaActionAsync(agendaAction, agendaToken);
                    return;
                }
                if (argument.Contains("openAgenda", StringComparison.Ordinal)
                    || argument.Contains("snoozeAgenda", StringComparison.Ordinal)
                    || argument.Contains("completeTask", StringComparison.Ordinal))
                    return;
                if (arguments.TryGetValue("action", out var copyAction) &&
                    copyAction == "copyCode" &&
                    arguments.TryGetValue("codeToken", out var codeToken) &&
                    await VerificationCodeStore.TakeAsync(codeToken) is { } code)
                {
                    CopyVerificationCodeToClipboard(code);
                    return;
                }

                if (arguments.TryGetValue("action", out var action) &&
                    action == "openMail" &&
                    arguments.TryGetValue("accountId", out var accountId) &&
                    arguments.TryGetValue("folderId", out var folderId) &&
                    arguments.TryGetValue("messageId", out var messageId) &&
                    NotificationActivationParser.IsSafeIdToken(accountId) &&
                    NotificationActivationParser.IsSafeIdToken(folderId) &&
                    NotificationActivationParser.IsSafeIdToken(messageId))
                {
                    App.OpenMainWindowInternal(window => window.NavigateToMailMessage(accountId, folderId, messageId));
                }
                else
                {
                    App.OpenMainWindowInternal();
                }
            });
        }

        private async Task HandleAgendaActionAsync(string action, string token)
        {
            var required = action switch
            {
                "openAgenda" => NotificationActionMask.Open,
                "snoozeAgenda" => NotificationActionMask.Snooze,
                "completeTask" => NotificationActionMask.Complete,
                _ => NotificationActionMask.None
            };
            if (required == NotificationActionMask.None) return;
            var target = await NotificationActionStore.ReadAsync(token, required, consume: action == "completeTask");
            if (target == null) return;

            if (action == "snoozeAgenda")
            {
                _snoozedActions[token] = DateTimeOffset.UtcNow.AddMinutes(10);
                SaveSnoozedActions();
                return;
            }

            var item = ResolveTarget(target);
            if (item == null || !IsItemVisible(item)) return;
            if (action == "openAgenda")
            {
                if (item.IsTask) App.OpenMainWindowInternal(window => window.NavigateToTasks());
                else App.OpenMainWindowInternal(window => window.NavigateToCalendarAndEdit(item));
                return;
            }

            if (!item.IsTask || item.IsCompleted
                || !string.Equals(item.Provider, "Google", StringComparison.OrdinalIgnoreCase)
                || !_syncManager.AccountManager.IsConnected(item.Provider)) return;
            if (App.Current is not App app) return;
            var key = $"{item.Provider}|{item.CalendarId}|{item.Id}";
            await app.TaskMutations.ExecuteAsync(key, async () =>
            {
                await _syncManager.UpdateTaskStatusAsync(item.Provider, item.Id, true, item.CalendarId);
                await _syncManager.SetCachedTaskCompletionAsync(item, true);
            });
            App.OpenMainWindowInternal(window => window.NavigateToTasks());
        }

        private AgendaItem? ResolveTarget(NotificationActionTarget target)
            => _syncManager.GetDayItemsSnapshot(new[] { target.DateKey })
                .SelectMany(pair => pair.Value)
                .FirstOrDefault(item => item.Id == target.ItemId && item.Provider == target.Provider && item.DateKey == target.DateKey);

        private async Task ProcessDueSnoozesAsync(DateTime now)
        {
            if (_processingSnoozes) return;
            _processingSnoozes = true;
            bool changed = false;
            try
            {
                foreach (var entry in _snoozedActions.Where(entry => entry.Value <= DateTimeOffset.UtcNow).ToList())
                {
                    var target = await NotificationActionStore.ReadAsync(entry.Key, NotificationActionMask.Snooze, consume: true);
                    var item = target == null ? null : ResolveTarget(target);
                    if (item != null && IsItemVisible(item) && !item.IsCompleted)
                        SendNotification(item, now.AddMinutes(1));
                    _snoozedActions.Remove(entry.Key);
                    changed = true;
                }
                if (changed) SaveSnoozedActions();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Snoozed notification processing failed: {ex.Message}");
            }
            finally { _processingSnoozes = false; }
        }

        private void LoadSnoozedActions()
        {
            var raw = ApplicationData.Current.LocalSettings.Values[SnoozedActionsSettingsKey] as string ?? "";
            foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('|', 2);
                if (parts.Length == 2 && NotificationActivationParser.IsOpaqueToken(parts[0]) && long.TryParse(parts[1], out var ticks))
                    _snoozedActions[parts[0]] = new DateTimeOffset(ticks, TimeSpan.Zero);
            }
        }

        private void SaveSnoozedActions()
            => ApplicationData.Current.LocalSettings.Values[SnoozedActionsSettingsKey] = string.Join('\n', _snoozedActions.Select(entry => $"{entry.Key}|{entry.Value.UtcTicks}"));

        // Copy a verification code straight to the clipboard from the toast button
        // without opening the main window. Keep it out of clipboard history/roaming when
        // the platform supports it, and clear it shortly after use if it is still there.
        private static void CopyVerificationCodeToClipboard(string code)
        {
            try
            {
                var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
                package.SetText(code);
                bool copied = false;
                try
                {
                    var options = new Windows.ApplicationModel.DataTransfer.ClipboardContentOptions
                    {
                        IsAllowedInHistory = false,
                        IsRoamable = false
                    };
                    copied = Windows.ApplicationModel.DataTransfer.Clipboard.SetContentWithOptions(package, options);
                }
                catch { }

                if (!copied)
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);

                _ = ClearVerificationCodeFromClipboardLaterAsync(code);

                var loader = new ResourceLoader();
                var confirm = new AppNotificationBuilder()
                    .AddText(loader.GetStringOrDefault("MailCodeCopied") ?? "Verification code copied")
                    .BuildNotification();
                AppNotificationManager.Default.Show(confirm);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Copy verification code failed: {ex.Message}");
            }
        }

        private static async Task ClearVerificationCodeFromClipboardLaterAsync(string code)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60));
                App.MainDispatcherQueue?.TryEnqueue(async () =>
                {
                    try
                    {
                        var content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
                        if (!content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
                            return;

                        string clipboardText = await content.GetTextAsync();
                        if (string.Equals(clipboardText, code, StringComparison.Ordinal))
                            Windows.ApplicationModel.DataTransfer.Clipboard.Clear();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Clear verification code clipboard failed: {ex.Message}");
                    }
                });
            }
            catch { }
        }

    }
}
