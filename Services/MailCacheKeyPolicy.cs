using System;

namespace Task_Flyout.Services
{
    internal static class MailCacheKeyPolicy
    {
        public static string Build(string accountId, string folderId, bool unreadOnly)
            => $"{accountId}|{folderId}|{unreadOnly}";

        public static bool TryCanonicalizeLegacy(string key, out string canonicalKey)
        {
            canonicalKey = key;
            var separator = key.LastIndexOf('|');
            if (separator <= 0 || !int.TryParse(key[(separator + 1)..], out _)) return false;

            var candidate = key[..separator];
            if (!candidate.EndsWith("|True", StringComparison.OrdinalIgnoreCase) &&
                !candidate.EndsWith("|False", StringComparison.OrdinalIgnoreCase))
                return false;

            canonicalKey = candidate;
            return true;
        }
    }
}
