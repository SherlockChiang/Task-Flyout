using System;
using System.Collections.Generic;
using System.Linq;

namespace Task_Flyout.Services
{
    internal sealed record MailMutationCacheResult(
        bool Changed,
        IReadOnlyCollection<string> ReadStateChangedFolderIds);

    internal static class MailMutationCachePolicy
    {
        public static MailMutationCacheResult Apply(
            IEnumerable<MailItem> items,
            MailAccountKind providerKind,
            MailItem target,
            MailMutationKind kind,
            bool value)
        {
            bool changed = false;
            var readStateChangedFolderIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items.Where(item => IsSameProviderIdentity(item, target, providerKind)))
            {
                if (kind == MailMutationKind.SetReadState && item.IsRead != value)
                {
                    item.IsRead = value;
                    changed = true;
                    if (!string.IsNullOrWhiteSpace(item.FolderId))
                        readStateChangedFolderIds.Add(item.FolderId);
                }

                if (kind == MailMutationKind.SetFlagged && item.IsFlagged != value)
                {
                    item.IsFlagged = value;
                    changed = true;
                }
            }

            return new MailMutationCacheResult(
                changed,
                readStateChangedFolderIds.OrderBy(id => id, StringComparer.Ordinal).ToArray());
        }

        public static int? AdjustUnreadCount(int? unreadCount, bool previousRead, bool newRead)
        {
            if (!unreadCount.HasValue || previousRead == newRead) return unreadCount;
            return Math.Max(0, unreadCount.Value + (newRead ? -1 : 1));
        }

        private static bool IsSameProviderIdentity(MailItem item, MailItem target, MailAccountKind providerKind)
        {
            if (!string.Equals(item.AccountId, target.AccountId, StringComparison.Ordinal) ||
                !string.Equals(item.Id, target.Id, StringComparison.Ordinal))
                return false;

            if (providerKind is MailAccountKind.Google or MailAccountKind.Outlook)
                return true;

            if (ReferenceEquals(item, target))
                return true;

            return string.Equals(item.FolderId, target.FolderId, StringComparison.Ordinal) &&
                   target.ImapUidValidity.HasValue &&
                   item.ImapUidValidity == target.ImapUidValidity;
        }
    }
}
