using System;

namespace Task_Flyout.Services
{
    internal static class MailPaginationPolicy
    {
        public static bool IsAllowedGraphNextLink(string? value)
            => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               uri.Scheme == Uri.UriSchemeHttps &&
               string.Equals(uri.Host, "graph.microsoft.com", StringComparison.OrdinalIgnoreCase);

        public static bool IsValidImapCursor(uint? cursorUidValidity, uint currentUidValidity, uint? beforeUid)
            => cursorUidValidity == currentUidValidity && beforeUid is > 1;

        public static bool IsValidImapMutation(uint? storedUidValidity, uint currentUidValidity, uint uid)
            => storedUidValidity is > 0 && storedUidValidity == currentUidValidity && uid > 0;
    }
}
