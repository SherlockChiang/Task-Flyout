using System;

namespace Task_Flyout.Services
{
    internal static class NotificationTargetPolicy
    {
        public static bool MatchesAgenda(NotificationActionTarget target, string provider, string accountId,
            string listId, string itemId, string dateKey, bool isTask)
            => AccountIdentityPolicy.Matches(provider, accountId, target.Provider, target.AccountId)
                && listId == target.ContainerId && itemId == target.ItemId
                && dateKey == target.DateKey && isTask == target.IsTask;

        public static bool MatchesMail(NotificationActionTarget target, string accountId, string address,
            MailAccountKind kind, bool setupComplete, MailItem item)
            => setupComplete && target.AllowedActions == NotificationActionMask.MarkRead
                && accountId == target.AccountId && item.AccountId == accountId
                && kind.ToString() == target.Provider
                && string.Equals(address, target.AccountAddress, StringComparison.OrdinalIgnoreCase)
                && item.FolderId == target.ContainerId && item.Id == target.ItemId
                && (kind != MailAccountKind.Imap || (target.ImapUidValidity.HasValue
                    && target.ImapUidValidity.Value != 0 && item.ImapUidValidity == target.ImapUidValidity));
    }
}
