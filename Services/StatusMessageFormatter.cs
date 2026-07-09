using System;

namespace Task_Flyout.Services
{
    internal static class StatusMessageFormatter
    {
        public static string Format(string message, DateTimeOffset? lastSuccess, bool includeLastSuccess)
        {
            message ??= "";
            if (!includeLastSuccess || !lastSuccess.HasValue)
                return message;

            return $"{message} · Last success: {lastSuccess.Value.LocalDateTime:g}";
        }
    }
}
