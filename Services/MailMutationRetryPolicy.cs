namespace Task_Flyout.Services
{
    internal static class MailMutationRetryPolicy
    {
        public static bool IsTransientStatusCode(int statusCode)
            => statusCode is 408 or 429 || statusCode >= 500;

        public static System.TimeSpan GetRetryDelay(int failureCount)
        {
            int minutes = 1 << System.Math.Min(System.Math.Max(failureCount - 1, 0), 6);
            return System.TimeSpan.FromMinutes(minutes);
        }

        public static bool IsExpired(long createdUtcTicks, System.DateTimeOffset now)
            => createdUtcTicks <= 0 || now - new System.DateTimeOffset(createdUtcTicks, System.TimeSpan.Zero) > System.TimeSpan.FromDays(7);

        public static System.TimeSpan GetSchedulerDelay(long nextAttemptUtcTicks, System.DateTimeOffset now)
        {
            long delayTicks = System.Math.Max(
                System.TimeSpan.FromSeconds(1).Ticks,
                nextAttemptUtcTicks - now.UtcTicks);
            return System.TimeSpan.FromTicks(System.Math.Min(delayTicks, System.TimeSpan.FromDays(1).Ticks));
        }
    }
}
