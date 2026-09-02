using System;
using System.Collections.Generic;
using System.Linq;

namespace Task_Flyout.Services
{
    internal sealed record MailUnreadSnapshotResult(
        List<MailItem> Items,
        IReadOnlyCollection<MailItem> RemovedItems);

    internal static class MailUnreadSnapshotPolicy
    {
        public static MailUnreadSnapshotResult Reconcile(
            IEnumerable<MailItem>? existingUnreadItems,
            IEnumerable<MailItem>? providerItems,
            bool isComplete,
            MailAccountKind providerKind,
            string accountId,
            string folderId,
            IEnumerable<PendingMailMutation>? pendingMutations,
            int maximumItems)
        {
            var existing = (existingUnreadItems ?? Enumerable.Empty<MailItem>())
                .Where(item => !item.IsRead &&
                               string.Equals(item.AccountId, accountId, StringComparison.Ordinal) &&
                               string.Equals(item.FolderId, folderId, StringComparison.Ordinal))
                .ToList();
            var pending = CurrentPendingMutations(pendingMutations, providerKind, accountId);
            var provider = (providerItems ?? Enumerable.Empty<MailItem>())
                .Where(item => string.Equals(item.AccountId, accountId, StringComparison.Ordinal) &&
                               string.Equals(item.FolderId, folderId, StringComparison.Ordinal))
                .ToList();

            IEnumerable<MailItem> candidates = provider;
            if (isComplete)
            {
                candidates = candidates.Concat(existing.Where(item => HasPendingUnreadIntent(item, pending, providerKind)));
            }
            else
            {
                candidates = candidates.Concat(existing);
            }

            var reconciled = Deduplicate(candidates, providerKind).ToList();
            ApplyPendingMutations(reconciled, providerKind, accountId, pending);
            reconciled = reconciled
                .Where(item => !item.IsRead)
                .OrderByDescending(item => item.RawReceivedTime)
                .Take(Math.Max(0, maximumItems))
                .ToList();

            var retainedIdentities = reconciled
                .Select(item => GetIdentityKey(item, providerKind))
                .ToHashSet(StringComparer.Ordinal);
            var removed = existing
                .Where(item => !retainedIdentities.Contains(GetIdentityKey(item, providerKind)))
                .Where(item => isComplete || HasPendingReadIntent(item, pending, providerKind))
                .GroupBy(item => GetIdentityKey(item, providerKind), StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();

            return new MailUnreadSnapshotResult(reconciled, removed);
        }

        public static void ApplyPendingMutations(
            IEnumerable<MailItem> items,
            MailAccountKind providerKind,
            string accountId,
            IEnumerable<PendingMailMutation>? pendingMutations)
        {
            var materialized = items as IReadOnlyCollection<MailItem> ?? items.ToList();
            foreach (var mutation in CurrentPendingMutations(pendingMutations, providerKind, accountId))
            {
                var target = new MailItem
                {
                    AccountId = mutation.AccountId,
                    FolderId = mutation.FolderId,
                    Id = mutation.MessageId,
                    ImapUidValidity = mutation.ImapUidValidity
                };
                MailMutationCachePolicy.Apply(materialized, providerKind, target, mutation.Kind, mutation.Value);
            }
        }

        private static List<PendingMailMutation> CurrentPendingMutations(
            IEnumerable<PendingMailMutation>? pendingMutations,
            MailAccountKind providerKind,
            string accountId)
            => (pendingMutations ?? Enumerable.Empty<PendingMailMutation>())
                .Where(mutation => mutation.ProviderKind == providerKind &&
                                   string.Equals(mutation.AccountId, accountId, StringComparison.Ordinal) &&
                                   mutation.Kind is MailMutationKind.SetReadState or MailMutationKind.SetFlagged)
                .OrderBy(mutation => mutation.CreatedUtcTicks)
                .ToList();

        private static bool HasPendingUnreadIntent(
            MailItem item,
            IEnumerable<PendingMailMutation> pending,
            MailAccountKind providerKind)
            => pending.Any(mutation =>
                mutation.Kind == MailMutationKind.SetReadState &&
                !mutation.Value &&
                MailMutationCachePolicy.IsSameProviderIdentity(item, ToTarget(mutation), providerKind));

        private static bool HasPendingReadIntent(
            MailItem item,
            IEnumerable<PendingMailMutation> pending,
            MailAccountKind providerKind)
            => pending.Any(mutation =>
                mutation.Kind == MailMutationKind.SetReadState &&
                mutation.Value &&
                MailMutationCachePolicy.IsSameProviderIdentity(item, ToTarget(mutation), providerKind));

        private static MailItem ToTarget(PendingMailMutation mutation)
            => new()
            {
                AccountId = mutation.AccountId,
                FolderId = mutation.FolderId,
                Id = mutation.MessageId,
                ImapUidValidity = mutation.ImapUidValidity
            };

        private static IEnumerable<MailItem> Deduplicate(IEnumerable<MailItem> items, MailAccountKind providerKind)
            => items
                .GroupBy(item => GetIdentityKey(item, providerKind), StringComparer.Ordinal)
                .Select(group => group.First());

        private static string GetIdentityKey(MailItem item, MailAccountKind providerKind)
            => providerKind == MailAccountKind.Imap
                ? $"{item.AccountId}\u001f{item.FolderId}\u001f{item.ImapUidValidity?.ToString() ?? "?"}\u001f{item.Id}"
                : $"{item.AccountId}\u001f{item.Id}";
    }
}
