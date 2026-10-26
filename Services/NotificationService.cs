using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Microsoft.Windows.ApplicationModel.Resources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Task_Flyout.Models;
using Windows.Storage;

namespace Task_Flyout.Services
{
    public class NotificationService
    {
        private readonly SyncManager _syncManager;
        // UI-thread only: all mutation paths (ctor LoadNotifiedIds, the app heartbeat
        // that drives CheckUpcomingEvents, and OpenFromActivationArguments which
        // marshals through MainDispatcherQueue) execute on the main UI thread. Do not
        // add background-thread callers without wrapping access in a lock first.
        private readonly Dictionary<string, DateTimeOffset> _notifiedIds = new(StringComparer.Ordinal);
        private readonly ResourceLoader _loader = new();
        private int _reminderMinutes;
        private Dictionary<string, List<AgendaItem>>? _cachedItems;
        private DateTime _cacheReadTime = DateTime.MinValue;
        private const string NotifiedIdsSettingsKey = "NotificationService.NotifiedIds";
        private const string SnoozedActionsSettingsKey = "NotificationService.SnoozedActions";
        private readonly Dictionary<string, DateTimeOffset> _snoozedActions = new(StringComparer.Ordinal);
        private bool _processingSnoozes;
        private readonly HashSet<string> _activeActions = new(StringComparer.Ordinal);
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
            CheckUpcomingEvents();
        }

        public void StopTimer() { }

        public void Stop()
        {
            try
            {
                AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
                AppNotificationManager.Default.Unregister();
            }
            catch { }
        }

        public void CheckUpcomingEvents(bool forceCacheRefresh = false)
        {
            if (forceCacheRefresh)
            {
                _cachedItems = null;
                _cacheReadTime = DateTime.MinValue;
            }

            if (!IsEnabled) return;

            try
            {
                var now = DateTime.Now;
                _ = ProcessDueSnoozesAsync();
                var cache = LoadCacheItems();
                if (cache == null || cache.Count == 0) return;
                var todayKey = now.ToString("yyyy-MM-dd");
                var tomorrowKey = now.AddDays(1).ToString("yyyy-MM-dd");

                var keysToCheck = new[] { todayKey, tomorrowKey };
                bool notifiedIdsChanged = false;

                foreach (var dateKey in keysToCheck)
                {
                    if (!cache.ContainsKey(dateKey)) continue;

                    foreach (var item in cache[dateKey])
                    {
                        if (!IsItemVisible(item)) continue;
                        if (!NotificationDeliveryPolicy.AllowsAgenda(NotificationPreferences.HighPriorityOnly, item.IsTask, item.IsHighPriority)) continue;
                        if (string.IsNullOrEmpty(item.Id)) continue;

                        var eventStart = GetEventStartTime(item, dateKey);
                        if (eventStart == null) continue;

                        var notifKey = BuildNotificationKey(item, dateKey, eventStart.Value);
                        if (_notifiedIds.ContainsKey(notifKey)) continue;

                        var minutesUntilStart = (eventStart.Value - now).TotalMinutes;

                        if (minutesUntilStart > 0 && minutesUntilStart <= _reminderMinutes)
                        {
                            if (SendNotification(item, eventStart.Value))
                            {
                                _notifiedIds[notifKey] = DateTimeOffset.UtcNow;
                                notifiedIdsChanged = true;
                            }
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

        private bool SendNotification(AgendaItem item, DateTime startTime, bool snoozed = false)
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
                if (snoozed)
                    timeText = _loader.GetStringOrDefault("Notification_SnoozedReminder") ?? "Snoozed reminder · original time";
                bool quiet = NotificationPreferences.IsQuietNow;

                var builder = new AppNotificationBuilder()
                    .AddText(HideNotificationContent
                        ? (item.IsTask
                            ? (_loader.GetStringOrDefault("TextTaskReminder") ?? "Task Reminder")
                            : (_loader.GetStringOrDefault("TextEventReminder") ?? "Event Reminder"))
                        : item.Title ?? (_loader.GetStringOrDefault("TextEventReminder") ?? "Event Reminder"))
                    .AddText($"{timeText} · {startTime.ToString("g", LocalizationHelper.AppCulture)}");
                if (quiet) builder.MuteAudio();

                var actions = NotificationActionPolicy.GetActions(item.IsTask, item.IsCompleted);
                if (!(string.Equals(item.Provider, "Google", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.Provider, "Microsoft", StringComparison.OrdinalIgnoreCase))
                    || string.IsNullOrWhiteSpace(item.CalendarId))
                    actions &= ~NotificationActionMask.Complete;
                string tag = BuildNotificationKey(item, item.DateKey, startTime);
                string group = item.IsTask ? "tasks" : "calendar";
                var target = new NotificationActionTarget(
                    NotificationActionTarget.CurrentSchemaVersion,
                    item.Provider,
                    item.Id,
                    item.DateKey,
                    actions,
                    DateTimeOffset.UtcNow.AddDays(2),
                    AccountId: item.AccountId, ContainerId: item.CalendarId, IsTask: item.IsTask,
                    NotificationTag: tag, NotificationGroup: group, SnoozeMinutes: NotificationPreferences.SnoozeMinutes);
                var token = NotificationActionStore.Store(target);
                builder.AddArgument("action", "openAgenda").AddArgument("token", token);
                if (actions.HasFlag(NotificationActionMask.Snooze))
                    builder.AddButton(new AppNotificationButton(string.Format(
                        _loader.GetStringOrDefault("Notification_SnoozeMinutes") ?? "Snooze {0} min", target.SnoozeMinutes))
                        .AddArgument("action", "snoozeAgenda").AddArgument("token", token));
                if (actions.HasFlag(NotificationActionMask.Complete))
                    builder.AddButton(new AppNotificationButton(_loader.GetStringOrDefault("Notification_Complete") ?? "Complete")
                        .AddArgument("action", "completeTask").AddArgument("token", token));

                if (!HideNotificationContent && !string.IsNullOrEmpty(item.Location))
                    builder.AddText($"\ud83d\udccd {item.Location}");

                var notification = builder.BuildNotification();
                notification.Tag = tag;
                notification.Group = group;
                notification.SuppressDisplay = quiet;
                AppNotificationManager.Default.Show(notification);

                System.Diagnostics.Debug.WriteLine($"Agenda notification sent at {startTime:HH:mm}");
                return notification.Id != 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Send notification failed: {ex.Message}");
                return false;
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

                if (parts.Length == 2 && NotificationDeliveryPolicy.TryReadUtcTicks(parts[1], out var timestamp))
                    _notifiedIds[key] = timestamp;
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
            => NotificationDeliveryPolicy.AgendaTag(item.ProviderKey, item.CalendarId, item.Id, dateKey, eventStart);

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
            OpenFromActivationArguments(args.Argument, sdkEncoded: true);
        }

        public static void OpenFromActivationArguments(string argument, bool sdkEncoded = false)
        {
            var route = NotificationActivationRouter.Parse(argument, sdkEncoded);
            if (route.Kind == NotificationActivationKind.Ignore) return;
            App.MainDispatcherQueue?.TryEnqueue(async () =>
            {
                switch (route.Kind)
                {
                    case NotificationActivationKind.AgendaAction:
                        if (App.Current is App app)
                            await app.NotificationService.HandleAgendaActionAsync(route.Action, route.Token);
                        break;
                    case NotificationActivationKind.CopyCode:
                        if (await VerificationCodeStore.TakeAsync(route.Token) is { } code)
                            CopyVerificationCodeToClipboard(code);
                        break;
                    case NotificationActivationKind.MailAction:
                        if (App.Current is App mailApp)
                            await mailApp.NotificationService.HandleMailActionAsync(route.Token);
                        break;
                    case NotificationActivationKind.OpenMail:
                        App.OpenMainWindowInternal(window => window.NavigateToMailMessage(
                            route.AccountId, route.FolderId, route.MessageId));
                        break;
                    case NotificationActivationKind.OpenMain:
                        App.OpenMainWindowInternal();
                        break;
                }
            });
        }

        private async Task HandleAgendaActionAsync(string action, string token)
        {
            if (!_activeActions.Add(token)) return;
            try { await ExecuteAgendaActionAsync(action, token); }
            catch
            {
                ShowActionResult("Notification_ActionFailed", true, () => HandleAgendaActionAsync(action, token));
            }
            finally { _activeActions.Remove(token); }
        }

        private async Task ExecuteAgendaActionAsync(string action, string token)
        {
            var required = action switch
            {
                "openAgenda" => NotificationActionMask.Open,
                "snoozeAgenda" => NotificationActionMask.Snooze,
                "completeTask" => NotificationActionMask.Complete,
                _ => NotificationActionMask.None
            };
            if (required == NotificationActionMask.None) return;
            var target = await NotificationActionStore.ReadAsync(token, required, consume: false);
            var item = target == null ? null : ResolveTarget(target);
            if (target == null || item == null || !IsItemVisible(item))
            {
                ShowActionResult("Notification_TargetUnavailable", true);
                return;
            }

            if (action == "snoozeAgenda")
            {
                if (item.IsCompleted || _snoozedActions.ContainsKey(token)) return;
                var due = await NotificationActionStore.ScheduleSnoozeAsync(token);
                if (due == null)
                {
                    ShowActionResult("Notification_TargetUnavailable", true);
                    return;
                }
                _snoozedActions[token] = due.Value;
                try { SaveSnoozedActions(); }
                catch { _snoozedActions.Remove(token); throw; }
                await RemoveNotificationAsync(target);
                return;
            }

            if (action == "openAgenda")
            {
                if (item.IsTask) App.OpenMainWindowInternal(window => window.NavigateToTasks());
                else App.OpenMainWindowInternal(window => window.NavigateToCalendarAndEdit(item));
                return;
            }

            if (!item.IsTask || !target.IsTask || string.IsNullOrWhiteSpace(item.CalendarId)
                || !(string.Equals(item.Provider, "Google", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.Provider, "Microsoft", StringComparison.OrdinalIgnoreCase))
                || !_syncManager.AccountManager.IsConnected(item.Provider, item.AccountId))
            {
                ShowActionResult("Notification_TargetUnavailable", true);
                return;
            }
            if (App.Current is not App app) return;
            var key = $"{item.ProviderKey}|{item.CalendarId}|{item.Id}";
            var result = await app.TaskMutations.ExecuteAsync(key, async () =>
            {
                // The coordinator can retry this operation later.
                // Revalidate the capability and account on every attempt.
                var currentTarget = await NotificationActionStore.ReadAsync(token, required, consume: false);
                var current = currentTarget == null ? null : ResolveTarget(currentTarget);
                if (current == null || !IsItemVisible(current)
                    || !_syncManager.AccountManager.IsConnected(current.Provider, current.AccountId))
                    throw new InvalidOperationException("Notification target is no longer available.");
                if (!current.IsCompleted)
                {
                    await _syncManager.UpdateTaskStatusAsync(current.Provider, current.Id, true, current.CalendarId, current.AccountId);
                    await _syncManager.SetCachedTaskCompletionAsync(current, true);
                }
                // Keep cleanup inside the retry operation so a retry from another
                // page also invalidates the original notification.
                await NotificationActionStore.ReadAsync(token, required, consume: true);
                _snoozedActions.Remove(token);
                try { SaveSnoozedActions(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Snooze cleanup failed: {ex.Message}"); }
                await RemoveNotificationAsync(target);
            }, providerKey: item.ProviderKey);
            if (result.Phase != TaskMutationPhase.Succeeded)
            {
                ShowActionResult("Notification_ActionFailed", true, () => HandleAgendaActionAsync(action, token));
                return;
            }
            ShowActionResult("Notification_TaskCompleted", false);
        }

        private AgendaItem? ResolveTarget(NotificationActionTarget target)
            => _syncManager.GetDayItemsSnapshot(new[] { target.DateKey })
                .SelectMany(pair => pair.Value)
                .FirstOrDefault(item => NotificationTargetPolicy.MatchesAgenda(target, item.Provider, item.AccountId,
                    item.CalendarId, item.Id, item.DateKey, item.IsTask));

        private async Task ProcessDueSnoozesAsync()
        {
            if (_processingSnoozes) return;
            _processingSnoozes = true;
            bool changed = false;
            try
            {
                foreach (var entry in _snoozedActions.Where(entry => entry.Value <= DateTimeOffset.UtcNow).ToList())
                {
                    if (!_activeActions.Add(entry.Key)) continue;
                    try
                    {
                        var target = await NotificationActionStore.ReadAsync(entry.Key, NotificationActionMask.Snooze, consume: false);
                        var item = target == null ? null : ResolveTarget(target);
                        // The cache may not yet be hydrated after startup. Keep a
                        // valid snooze until the item returns or the token expires.
                        if (target != null && item == null) continue;
                        if (item != null && IsItemVisible(item) && !item.IsCompleted)
                        {
                            var start = GetEventStartTime(item, item.DateKey);
                            if (start != null && !SendNotification(item, start.Value, snoozed: true)) continue;
                        }
                        await NotificationActionStore.ReadAsync(entry.Key, NotificationActionMask.Snooze, consume: true);
                        _snoozedActions.Remove(entry.Key);
                        changed = true;
                    }
                    finally { _activeActions.Remove(entry.Key); }
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
                if (parts.Length == 2 && NotificationActivationParser.IsOpaqueToken(parts[0])
                    && NotificationDeliveryPolicy.TryReadUtcTicks(parts[1], out var timestamp))
                    _snoozedActions[parts[0]] = timestamp;
            }
        }

        private void SaveSnoozedActions()
            => ApplicationData.Current.LocalSettings.Values[SnoozedActionsSettingsKey] = string.Join('\n', _snoozedActions.Select(entry => $"{entry.Key}|{entry.Value.UtcTicks}"));

        private async Task HandleMailActionAsync(string token)
        {
            if (!_activeActions.Add(token)) return;
            try
            {
                var target = await NotificationActionStore.ReadAsync(token, NotificationActionMask.MarkRead, consume: false);
                if (App.Current is not App app) return;
                var account = app.MailService.GetAccounts().FirstOrDefault(account =>
                    account.Id == target?.AccountId);
                var item = target == null ? null : app.MailService.TryGetCachedMessage(target.AccountId, target.ContainerId, target.ItemId);
                if (target == null || account == null || item == null
                    || !NotificationTargetPolicy.MatchesMail(target, account.Id, account.Address,
                        account.Kind, account.IsSetupComplete, item))
                {
                    ShowActionResult("Notification_TargetUnavailable", true);
                    return;
                }
                bool queued = false;
                if (!item.IsRead)
                {
                    try { await app.MailService.SetReadStateAsync(account, item, true); }
                    catch (MailMutationSyncQueuedException) { queued = true; }
                    app.MailService.ApplyCachedMutation(item, MailMutationKind.SetReadState, true);
                }
                await NotificationActionStore.ReadAsync(token, NotificationActionMask.MarkRead, consume: true);
                await RemoveNotificationAsync(target);
                ShowActionResult(queued ? "Notification_MailQueued" : "Notification_MailRead", false);
            }
            catch
            {
                ShowActionResult("Notification_ActionFailed", true, () => HandleMailActionAsync(token));
            }
            finally { _activeActions.Remove(token); }
        }

        private void ShowActionResult(string resourceKey, bool error, Func<Task>? retry = null)
            => App.OpenMainWindowInternal(window => window.ShowNotificationActionResult(
                _loader.GetStringOrDefault(resourceKey) ?? resourceKey, error, retry));

        private static async Task RemoveNotificationAsync(NotificationActionTarget target)
        {
            if (string.IsNullOrEmpty(target.NotificationTag) || string.IsNullOrEmpty(target.NotificationGroup)) return;
            try { await AppNotificationManager.Default.RemoveByTagAndGroupAsync(target.NotificationTag, target.NotificationGroup); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Notification cleanup failed: {ex.Message}"); }
        }

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
