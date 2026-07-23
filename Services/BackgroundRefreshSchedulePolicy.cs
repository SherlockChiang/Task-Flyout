using System;

namespace Task_Flyout.Services
{
    internal static class BackgroundRefreshSchedulePolicy
    {
        public static bool IsMailPollDue(
            DateTimeOffset now,
            DateTimeOffset lastStarted,
            int intervalMinutes,
            bool enabled,
            bool hasAccounts)
        {
            if (!enabled || !hasAccounts) return false;
            if (lastStarted == DateTimeOffset.MinValue) return true;
            return now - lastStarted >= TimeSpan.FromMinutes(Math.Clamp(intervalMinutes, 1, 240));
        }
    }
}
