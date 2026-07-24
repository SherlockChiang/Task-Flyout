using System;

namespace Task_Flyout.Services
{
    internal static class WeatherAlertTextPolicy
    {
        public static string FormatCompact(string label, int hoursAhead, int? minutesUntilEnd = null)
        {
            label = label?.Trim() ?? string.Empty;
            if (hoursAhead > 0) return $"{label} · {hoursAhead}h";
            if (!minutesUntilEnd.HasValue) return label;

            int minutes = Math.Max(1, minutesUntilEnd.Value);
            string duration = minutes <= 5
                ? "≤5m"
                : minutes < 60
                    ? $"{minutes}m"
                    : $"{Math.Max(1, (int)Math.Round(minutes / 60.0))}h";
            return $"{label} · {duration}";
        }
    }
}
