using System;
using System.Collections.Generic;
using System.Linq;

namespace Task_Flyout.Services
{
    internal static class ProviderAuthorizationScopePolicy
    {
        public static readonly string[] GoogleAllFeatures =
        {
            "https://www.googleapis.com/auth/calendar",
            "https://www.googleapis.com/auth/tasks",
            "https://www.googleapis.com/auth/gmail.modify"
        };

        public static bool HasAllScopes(
            IEnumerable<string>? grantedScopes,
            IEnumerable<string>? requiredScopes)
        {
            if (grantedScopes == null || requiredScopes == null)
                return false;

            var granted = grantedScopes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            return requiredScopes.All(scope => granted.Contains(scope));
        }

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
