using System;
using System.Linq;

namespace Task_Flyout.Services
{
    internal readonly record struct CalendarEditorTimeState(
        bool IsAllDay,
        TimeSpan? StartTime,
        TimeSpan? EndTime);

    internal static class CalendarEventTimePolicy
    {
        public static CalendarEditorTimeState CreateEditorState(
            DateTime? startDateTime,
            DateTime? endDateTime,
            bool isAllDay,
            string? legacySubtitle = null)
        {
            if (isAllDay)
                return new CalendarEditorTimeState(true, null, null);

            if (startDateTime.HasValue)
            {
                var start = startDateTime.Value.TimeOfDay;
                var end = endDateTime?.TimeOfDay ?? NormalizeTimeOfDay(start.Add(TimeSpan.FromHours(1)));
                return new CalendarEditorTimeState(false, start, end);
            }

            var legacyTimes = ParseLegacyTimes(legacySubtitle);
            return new CalendarEditorTimeState(false, legacyTimes.StartTime, legacyTimes.EndTime);
        }

        public static string FormatTimeRange(
            DateTime? startDateTime,
            DateTime? endDateTime,
            bool isAllDay,
            string allDayText,
            string fallbackText)
        {
            if (isAllDay)
                return allDayText;
            if (!startDateTime.HasValue)
                return fallbackText;

            string start = startDateTime.Value.ToString("HH:mm");
            return endDateTime.HasValue
                ? $"{start} - {endDateTime.Value:HH:mm}"
                : start;
        }

        private static (TimeSpan? StartTime, TimeSpan? EndTime) ParseLegacyTimes(string? subtitle)
        {
            string timeText = subtitle?.Split('\n').LastOrDefault()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(timeText))
                return (null, null);

            var parts = timeText.Split(['-', '\u2013', '\u2014'], StringSplitOptions.TrimEntries);
            if (!TimeSpan.TryParse(parts[0], out var start))
                return (null, null);

            var end = parts.Length > 1 && TimeSpan.TryParse(parts[1], out var parsedEnd)
                ? parsedEnd
                : NormalizeTimeOfDay(start.Add(TimeSpan.FromHours(1)));
            return (start, end);
        }

        private static TimeSpan NormalizeTimeOfDay(TimeSpan value)
        {
            var ticks = value.Ticks % TimeSpan.TicksPerDay;
            if (ticks < 0) ticks += TimeSpan.TicksPerDay;
            return TimeSpan.FromTicks(ticks);
        }
    }
}
