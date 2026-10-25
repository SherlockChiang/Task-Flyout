using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Task_Flyout.Services
{
    internal static class NotificationDeliveryPolicy
    {
        public static int SnoozeMinutes(int value) => value is 5 or 10 or 15 or 30 or 60 ? value : 10;

        public static int ClockMinutes(int value, int fallback)
            => value is >= 0 and < 1440 ? value : fallback;

        // Equal endpoints mean a full quiet day. The end is exclusive.
        public static bool IsQuiet(bool enabled, int start, int end, TimeSpan localTime)
        {
            if (!enabled) return false;
            start = ClockMinutes(start, 22 * 60);
            end = ClockMinutes(end, 8 * 60);
            int minute = (int)localTime.TotalMinutes;
            return start == end || (start < end
                ? minute >= start && minute < end
                : minute >= start || minute < end);
        }

        public static bool AllowsAgenda(bool highOnly, bool isTask, bool isHighPriority)
            => !highOnly || !isTask || isHighPriority;

        public static bool AllowsMail(bool highOnly, bool flagged, string? importance)
            => !highOnly || flagged || string.Equals(importance, "high", StringComparison.OrdinalIgnoreCase);

        public static string AgendaTag(string providerKey, string listId, string itemId, string dateKey, DateTime start)
        {
            string identity = JsonSerializer.Serialize(new[]
            {
                providerKey.ToUpperInvariant(), listId, itemId, dateKey,
                start.ToString("yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture)
            });
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0, 8));
        }

        public static bool TryReadUtcTicks(string value, out DateTimeOffset timestamp)
        {
            timestamp = default;
            if (!long.TryParse(value, out long ticks) || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
                return false;
            timestamp = new DateTimeOffset(ticks, TimeSpan.Zero);
            return true;
        }
    }
}
