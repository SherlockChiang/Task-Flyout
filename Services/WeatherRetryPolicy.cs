using System;

namespace Task_Flyout.Services
{
    internal static class WeatherRetryPolicy
    {
        public static TimeSpan GetDelay(int failureCount) => failureCount switch
        {
            <= 0 => TimeSpan.Zero,
            1 => TimeSpan.FromMinutes(1),
            2 => TimeSpan.FromMinutes(2),
            3 => TimeSpan.FromMinutes(5),
            _ => TimeSpan.FromMinutes(15)
        };

        public static bool IsBackedOff(
            string requestKey,
            string? failureKey,
            long failureUtcTicks,
            int failureCount,
            DateTimeOffset nowUtc)
        {
            if (!string.Equals(requestKey, failureKey, StringComparison.Ordinal) ||
                failureCount <= 0 || failureUtcTicks <= 0)
                return false;

            try
            {
                var failedAt = new DateTimeOffset(failureUtcTicks, TimeSpan.Zero);
                return nowUtc - failedAt < GetDelay(failureCount);
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }
    }
}
