using System;
using System.Collections.Generic;
using System.Linq;

namespace Task_Flyout.Services
{
    public readonly record struct CalendarDotRenderKey(
        int DisplayedYear,
        int DisplayedMonth,
        long AgendaCacheVersion,
        int DisplayMode,
        long ContainerGeneration,
        long FilterVersion);

    public readonly record struct RealizedDayVisibility(DateTime Date, double Top, double Bottom);

    public static class CalendarDotRenderPolicy
    {
        public static bool RequiresSemanticRender(CalendarDotRenderKey? previous, CalendarDotRenderKey current)
            => previous != current;

        public static DateTime? DetectDisplayedMonth(
            IEnumerable<RealizedDayVisibility> realizedDays,
            double viewportHeight)
        {
            if (viewportHeight <= 0) return null;

            var visible = realizedDays
                .Select(day => new
                {
                    day.Date,
                    VisibleHeight = Math.Max(0, Math.Min(day.Bottom, viewportHeight) - Math.Max(day.Top, 0)),
                    CenterDistance = Math.Abs(((day.Top + day.Bottom) / 2) - (viewportHeight / 2))
                })
                .Where(day => day.VisibleHeight > 0)
                .ToList();
            if (visible.Count == 0) return null;

            var month = visible
                .GroupBy(day => new { day.Date.Year, day.Date.Month })
                .Select(group => new
                {
                    group.Key.Year,
                    group.Key.Month,
                    VisibleArea = group.Sum(day => day.VisibleHeight),
                    CenterDistance = group.Min(day => day.CenterDistance)
                })
                .OrderByDescending(group => group.VisibleArea)
                .ThenBy(group => group.CenterDistance)
                .First();

            return new DateTime(month.Year, month.Month, 1);
        }
    }
}
