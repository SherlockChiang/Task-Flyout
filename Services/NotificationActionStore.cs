using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Task_Flyout.Services
{
    internal sealed record NotificationActionTarget(
        int SchemaVersion,
        string Provider,
        string ItemId,
        string DateKey,
        NotificationActionMask AllowedActions,
        DateTimeOffset ExpiresAt,
        DateTimeOffset? SnoozedUntil = null,
        string AccountId = "",
        string ContainerId = "",
        bool IsTask = false,
        string AccountAddress = "",
        uint? ImapUidValidity = null,
        string NotificationTag = "",
        string NotificationGroup = "",
        int SnoozeMinutes = 10)
    {
        public const int CurrentSchemaVersion = 3;
    }

    [JsonSerializable(typeof(NotificationActionTarget))]
    internal partial class NotificationActionJsonContext : JsonSerializerContext { }

    internal static class NotificationActionStore
    {
        private const string Scope = "notification-action";
        private static readonly SemaphoreSlim Gate = new(1, 1);

        public static string Store(NotificationActionTarget target)
        {
            var token = Guid.NewGuid().ToString("N");
            var json = JsonSerializer.Serialize(target, NotificationActionJsonContext.Default.NotificationActionTarget);
            LocalSqliteStore.WriteProtectedText(Scope, token, json);
            return token;
        }

        public static async Task<NotificationActionTarget?> ReadAsync(string? token, NotificationActionMask requiredAction, bool consume)
        {
            if (!NotificationActivationParser.IsOpaqueToken(token)
                || requiredAction is not (NotificationActionMask.Open or NotificationActionMask.Snooze
                    or NotificationActionMask.Complete or NotificationActionMask.MarkRead)) return null;
            await Gate.WaitAsync();
            try
            {
                var json = LocalSqliteStore.ReadProtectedText(Scope, token!);
                if (string.IsNullOrWhiteSpace(json)) return null;
                var target = JsonSerializer.Deserialize(json, NotificationActionJsonContext.Default.NotificationActionTarget);
                if (target?.SchemaVersion != NotificationActionTarget.CurrentSchemaVersion
                    || target.ExpiresAt < DateTimeOffset.UtcNow
                    || !target.AllowedActions.HasFlag(requiredAction)) return null;
                if (consume) await LocalSqliteStore.DeleteProtectedTextAsync(Scope, token!);
                return target;
            }
            catch { return null; }
            finally { Gate.Release(); }
        }

        public static async Task<DateTimeOffset?> ScheduleSnoozeAsync(string token)
        {
            if (!NotificationActivationParser.IsOpaqueToken(token)) return null;
            await Gate.WaitAsync();
            try
            {
                var json = LocalSqliteStore.ReadProtectedText(Scope, token);
                if (json == null) return null;
                var target = JsonSerializer.Deserialize(json, NotificationActionJsonContext.Default.NotificationActionTarget);
                var now = DateTimeOffset.UtcNow;
                if (target?.SchemaVersion != NotificationActionTarget.CurrentSchemaVersion
                    || target.ExpiresAt <= now || !target.AllowedActions.HasFlag(NotificationActionMask.Snooze)) return null;
                var due = target.SnoozedUntil ?? now.AddMinutes(NotificationDeliveryPolicy.SnoozeMinutes(target.SnoozeMinutes));
                var updated = target with { SnoozedUntil = due, ExpiresAt = due.AddDays(1) };
                LocalSqliteStore.WriteProtectedText(Scope, token,
                    JsonSerializer.Serialize(updated, NotificationActionJsonContext.Default.NotificationActionTarget));
                return due;
            }
            finally { Gate.Release(); }
        }
    }
}
