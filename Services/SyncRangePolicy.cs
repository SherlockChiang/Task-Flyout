using System;

namespace Task_Flyout.Services
{
    internal readonly record struct HalfOpenDateRange(DateTime StartDate, DateTime EndDate);

    internal static class SyncRangePolicy
    {
        public static HalfOpenDateRange NormalizeHalfOpenDateRange(DateTime start, DateTime end)
        {
            var normalizedStart = start.Date;
            var normalizedEnd = end.Date;
            if (normalizedEnd < normalizedStart)
                normalizedEnd = normalizedStart;

            return new HalfOpenDateRange(normalizedStart, normalizedEnd);
        }

        public static bool IsInHalfOpenDateRange(DateTime date, DateTime start, DateTime end)
        {
            var range = NormalizeHalfOpenDateRange(start, end);
            var day = date.Date;
            return day >= range.StartDate && day < range.EndDate;
        }

        public static bool ShouldIncludeTask(DateTime taskDate, bool isCompleted, DateTime start, DateTime end)
            => isCompleted || IsInHalfOpenDateRange(taskDate, start, end);
    }
}
