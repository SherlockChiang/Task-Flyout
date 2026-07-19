namespace Task_Flyout.Services
{
    internal static class ProviderAuthorizationScopePolicy
    {
        public static readonly string[] GoogleAllFeatures =
        {
            "https://www.googleapis.com/auth/calendar",
            "https://www.googleapis.com/auth/tasks",
            "https://www.googleapis.com/auth/gmail.readonly",
            "https://www.googleapis.com/auth/gmail.modify",
            "https://www.googleapis.com/auth/gmail.send"
        };

        public static readonly string[] MicrosoftAllFeatures =
        {
            "User.Read",
            "Calendars.ReadWrite",
            "Tasks.ReadWrite",
            "Mail.ReadWrite",
            "Mail.Send"
        };
    }
}
