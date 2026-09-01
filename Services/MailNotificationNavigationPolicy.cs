using System;
using System.Collections.Generic;
using System.Linq;

namespace Task_Flyout.Services
{
    /// <summary>
    /// Pure identity matching rules used when a mail toast is activated.
    ///
    /// Folder identifiers are provider-owned values. Inbox identifiers can be
    /// localized, so a toast target accepts only exact identifiers and explicit
    /// Inbox aliases. Message identifiers can be used across folders for
    /// Gmail/Outlook, while an IMAP UID is only meaningful in its source folder.
    /// </summary>
    internal static class MailNotificationNavigationPolicy
    {
        public static bool FolderMatches(MailFolder? folder, string? requestedFolderId)
        {
            if (folder == null || string.IsNullOrWhiteSpace(requestedFolderId))
                return false;

            return string.Equals(folder.Id, requestedFolderId, StringComparison.Ordinal)
                || (IsInboxAlias(requestedFolderId)
                    && (IsInboxAlias(folder.Id) || IsInboxAlias(folder.DisplayName)));
        }

        public static bool MessageMatches(
            MailItem? item,
            string? accountId,
            string? folderId,
            string? messageId)
        {
            if (item == null
                || string.IsNullOrWhiteSpace(accountId)
                || string.IsNullOrWhiteSpace(folderId)
                || string.IsNullOrWhiteSpace(messageId))
                return false;

            if (!string.Equals(item.AccountId, accountId, StringComparison.Ordinal)
                || !string.Equals(item.Id, messageId, StringComparison.Ordinal))
                return false;

            return string.Equals(item.FolderId, folderId, StringComparison.Ordinal);
        }

        public static bool CanUseCrossFolderIdentity(MailAccountKind accountKind)
            => accountKind is MailAccountKind.Google or MailAccountKind.Outlook;

        public static List<MailItem> MergePolledMessages(
            IEnumerable<MailItem>? existing,
            IEnumerable<MailItem>? polled,
            bool unreadOnly,
            int maxItems)
        {
            if (maxItems <= 0) return new List<MailItem>();

            var eligiblePolled = (polled ?? Enumerable.Empty<MailItem>())
                .Where(item => !unreadOnly || !item.IsRead);
            return eligiblePolled
                .Concat(existing ?? Enumerable.Empty<MailItem>())
                .Where(item => !unreadOnly || !item.IsRead)
                .GroupBy(item => item.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderByDescending(item => item.RawReceivedTime)
                .Take(maxItems)
                .ToList();
        }

        private static bool IsInboxAlias(string? value)
            => string.Equals(value, "INBOX", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "Inbox", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "收件箱", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "收件匣", StringComparison.OrdinalIgnoreCase);
    }
}
