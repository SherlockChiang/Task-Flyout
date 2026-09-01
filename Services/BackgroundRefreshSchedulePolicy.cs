using System;

namespace Task_Flyout.Services
{
    internal static class BackgroundRefreshSchedulePolicy
    {
        internal const int AgendaPastDays = 14;
        internal const int AgendaFutureDays = 90;

        public static bool IsMailPollDue(
            DateTimeOffset now,
            DateTimeOffset lastStarted,
            int intervalMinutes,
            bool enabled,
            bool hasAccounts)
            => IsDue(now, lastStarted, intervalMinutes, enabled && hasAccounts);

        public static bool IsAgendaRefreshDue(
            DateTimeOffset now,
            DateTimeOffset lastStarted,
            int intervalMinutes,
            bool hasAccounts)
            => IsDue(now, lastStarted, intervalMinutes, hasAccounts);

        public static (DateTime Min, DateTime Max) GetAgendaRefreshRange(DateTime anchor)
            => (anchor.Date.AddDays(-AgendaPastDays), anchor.Date.AddDays(AgendaFutureDays));

        private static bool IsDue(
            DateTimeOffset now,
            DateTimeOffset lastStarted,
            int intervalMinutes,
            bool enabled)
        {
            if (!enabled) return false;
            if (lastStarted == DateTimeOffset.MinValue) return true;
            return now - lastStarted >= TimeSpan.FromMinutes(Math.Clamp(intervalMinutes, 1, 240));
        }
    }
}
