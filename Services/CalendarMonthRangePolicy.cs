using System;

namespace Task_Flyout.Services
{
    internal readonly record struct CalendarMonthRange(
        DateTime Start,
        DateTime EndExclusive,
        DateTime MonthStart,
        DateTime MonthEndExclusive)
    {
        public bool Contains(DateTime date) => date.Date >= Start && date.Date < EndExclusive;

        public bool ContainsDateKey(string dateKey)
            => TryParseDateKey(dateKey, out var date) && Contains(date);

        public bool ContainsMonthDateKey(string dateKey)
            => TryParseDateKey(dateKey, out var date)
               && date >= MonthStart
               && date < MonthEndExclusive;

        private static bool TryParseDateKey(string dateKey, out DateTime date)
            => DateTime.TryParseExact(
                   dateKey,
                   "yyyy-MM-dd",
                   System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.None,
                   out date);
    }

    internal static class CalendarMonthRangePolicy
    {
        public static CalendarMonthRange GetWeekRange(DateTime displayedDate, DayOfWeek firstDayOfWeek)
        {
            var start = LocalizationHelper.GetWeekStart(displayedDate.Date, firstDayOfWeek);
            return new CalendarMonthRange(
                start,
                start.AddDays(7),
                start,
                start.AddDays(7));
        }

        public static CalendarMonthRange GetRange(DateTime displayedMonth, DayOfWeek firstDayOfWeek)
        {
            var firstOfMonth = new DateTime(displayedMonth.Year, displayedMonth.Month, 1);
            var start = LocalizationHelper.GetWeekStart(firstOfMonth, firstDayOfWeek);
            int offset = LocalizationHelper.GetDayOffset(firstOfMonth.DayOfWeek, firstDayOfWeek);
            int totalCells = offset + DateTime.DaysInMonth(firstOfMonth.Year, firstOfMonth.Month);
            int cellCount = (int)Math.Ceiling(totalCells / 7.0) * 7;
            return new CalendarMonthRange(
                start.Date,
                start.Date.AddDays(cellCount),
                firstOfMonth,
                firstOfMonth.AddMonths(1));
        }

        public static CalendarMonthRange GetYearRange(int year)
        {
            var start = new DateTime(year, 1, 1);
            return new CalendarMonthRange(start, start.AddYears(1), start, start.AddYears(1));
        }
    }
}
