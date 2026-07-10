namespace Task_Flyout.Services
{
    internal static class MailMutationRetryPolicy
    {
        public static bool IsTransientStatusCode(int statusCode)
            => statusCode is 408 or 429 || statusCode >= 500;
    }
}
