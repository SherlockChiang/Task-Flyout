using System;

namespace Task_Flyout.Services
{
    internal readonly record struct SyncEventTimeWindow(DateTime Start, DateTime End, bool IsAllDay);

    internal static class SyncEventTimePolicy
    {
        public static SyncEventTimeWindow Create(DateTime targetDate, TimeSpan? startTime, TimeSpan? endTime)
        {
            var date = targetDate.Date;
            if (!startTime.HasValue)
                return new SyncEventTimeWindow(date, date.AddDays(1), IsAllDay: true);

            var start = date.Add(startTime.Value);
            var end = endTime.HasValue ? date.Add(endTime.Value) : start.AddHours(1);
            if (end < start)
                end = end.AddDays(1);

            return new SyncEventTimeWindow(start, end, IsAllDay: false);
        }
    }
}
