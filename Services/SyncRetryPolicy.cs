using System;

namespace Task_Flyout.Services
{
    internal static class SyncRetryPolicy
    {
        public const int MaximumRetryCount = 3;

        public static bool ShouldRetryHttpStatus(int statusCode, int retriesCompleted)
            => retriesCompleted < MaximumRetryCount &&
               (statusCode == 408 || statusCode == 429 || statusCode is >= 500 and <= 599);

        public static TimeSpan GetDelay(int retriesCompleted)
        {
            int boundedRetry = Math.Clamp(retriesCompleted, 0, MaximumRetryCount - 1);
            return TimeSpan.FromMilliseconds(500 * (1 << boundedRetry));
        }
    }
}
