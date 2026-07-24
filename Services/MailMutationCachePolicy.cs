using System;
using System.Collections.Generic;
using System.Linq;

namespace Task_Flyout.Services
{
    internal static class MailMutationCachePolicy
    {
        public static bool Apply(IEnumerable<MailItem> items, string accountId, string folderId, string messageId, MailMutationKind kind, bool value)
        {
            bool changed = false;
            foreach (var item in items.Where(item => item.AccountId == accountId && item.FolderId == folderId && item.Id == messageId))
            {
                if (kind == MailMutationKind.SetReadState && item.IsRead != value) { item.IsRead = value; changed = true; }
                if (kind == MailMutationKind.SetFlagged && item.IsFlagged != value) { item.IsFlagged = value; changed = true; }
            }
            return changed;
        }

        public static int? AdjustUnreadCount(int? unreadCount, bool previousRead, bool newRead)
        {
            if (!unreadCount.HasValue || previousRead == newRead) return unreadCount;
            return Math.Max(0, unreadCount.Value + (newRead ? -1 : 1));
        }
    }
}
