namespace Task_Flyout.Services
{
    internal static class GoogleItemContainerPolicy
    {
        public static string ResolveCalendarId(string? calendarId)
            => string.IsNullOrWhiteSpace(calendarId) ? "primary" : calendarId;

        public static string ResolveTaskListId(string? taskListId)
            => string.IsNullOrWhiteSpace(taskListId) ? "@default" : taskListId;
    }
}
