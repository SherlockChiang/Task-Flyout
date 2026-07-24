using System;
using System.Collections.Generic;
using System.Linq;

namespace Task_Flyout.Services
{
    internal static class MailMoveCachePolicy
    {
        public static IReadOnlyList<string> InvalidatedWindowKeys(string accountId, string sourceFolderId, string destinationFolderId)
        {
            var keys = new List<string>();
            foreach (var folderId in new[] { sourceFolderId, destinationFolderId })
            {
                foreach (bool unreadOnly in new[] { false, true })
                {
                    string key = MailCacheKeyPolicy.Build(accountId, folderId, unreadOnly);
                    if (!keys.Contains(key, StringComparer.Ordinal))
                        keys.Add(key);
                }
            }
            return keys;
        }

        public static int? AdjustSourceUnreadCount(int? unreadCount, bool messageIsRead)
            => !unreadCount.HasValue || messageIsRead ? unreadCount : Math.Max(0, unreadCount.Value - 1);

        public static int? AdjustDestinationUnreadCount(int? unreadCount, bool messageIsRead)
            => !unreadCount.HasValue || messageIsRead ? unreadCount : unreadCount.Value + 1;
    }
}
