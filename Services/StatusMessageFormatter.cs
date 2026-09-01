using System;

namespace Task_Flyout.Services
{
    internal static class StatusMessageFormatter
    {
        public static string Format(
            string message,
            DateTimeOffset? lastSuccess,
            bool includeLastSuccess,
            string lastSuccessFormat,
            IFormatProvider? formatProvider = null)
        {
            message ??= "";
            if (!includeLastSuccess || !lastSuccess.HasValue)
                return message;

            ArgumentException.ThrowIfNullOrWhiteSpace(lastSuccessFormat);
            string suffix = string.Format(
                formatProvider,
                lastSuccessFormat,
                lastSuccess.Value.LocalDateTime);
            return $"{message} · {suffix}";
        }
    }
}
