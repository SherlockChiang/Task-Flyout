using System;

namespace Task_Flyout.Services
{
    internal static class CalendarWeekLayoutPolicy
    {
        public const int TimeLabelIntervalHours = 3;

        public static bool ShouldShowTimeLabel(int hour)
            => hour is >= 0 and <= 24 && hour % TimeLabelIntervalHours == 0;

        public static string FormatEventTime(
            DateTime? start,
            DateTime? end,
            bool isAllDay,
            string allDayText)
        {
            if (isAllDay)
                return allDayText;
            if (start == null)
                return "";
            if (end == null || end <= start)
                return start.Value.ToString("HH:mm");

            return $"{start.Value:HH:mm}\u2013{end.Value:HH:mm}";
        }

        public static string FormatCompactEventText(string title, string timeText)
        {
            title = title?.Trim() ?? "";
            timeText = timeText?.Trim() ?? "";

            if (string.IsNullOrEmpty(timeText))
                return title;
            if (string.IsNullOrEmpty(title))
                return timeText;
            return $"{timeText}  {title}";
        }
    }
}
