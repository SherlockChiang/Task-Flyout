using System;
using System.Collections.Generic;
using System.Linq;

namespace Task_Flyout.Services
{
    public enum MailAccountKind
    {
        Outlook,
        Google,
        Imap
    }

    public sealed class PendingMailMutation
    {
        public string AccountId { get; set; } = "";
        public string FolderId { get; set; } = "";
        public string MessageId { get; set; } = "";
        public MailAccountKind ProviderKind { get; set; }
        public uint? ImapUidValidity { get; set; }
        public int FailureCount { get; set; }
        public long CreatedUtcTicks { get; set; }
        public long NextAttemptUtcTicks { get; set; }
    }

    internal static class MailPendingMutationPolicy
    {
        public static PendingMailMutation Upsert(
            List<PendingMailMutation> queue,
            PendingMailMutation mutation,
            DateTimeOffset now,
            int maximumCount)
        {
            var pending = queue.FirstOrDefault(candidate => IsSame(candidate, mutation));
            if (pending == null)
            {
                pending = Clone(mutation);
                pending.CreatedUtcTicks = now.UtcTicks;
                queue.Add(pending);
            }
            else
            {
                pending.ProviderKind = mutation.ProviderKind;
                pending.ImapUidValidity = mutation.ImapUidValidity;
            }

            pending.FailureCount = Math.Max(1, pending.FailureCount + 1);
            pending.NextAttemptUtcTicks = (now + MailMutationRetryPolicy.GetRetryDelay(pending.FailureCount)).UtcTicks;
            if (queue.Count > maximumCount)
            {
                var retained = queue
                    .OrderByDescending(candidate => candidate.CreatedUtcTicks)
                    .Take(Math.Max(0, maximumCount))
                    .ToList();
                queue.Clear();
                queue.AddRange(retained);
            }
            return pending;
        }

        public static List<PendingMailMutation> SelectDue(
            IEnumerable<PendingMailMutation> queue,
            string accountId,
            MailAccountKind providerKind,
            DateTimeOffset now,
            int maximumCount)
            => queue
                .Where(mutation => mutation.AccountId == accountId &&
                                   mutation.ProviderKind == providerKind &&
                                   mutation.NextAttemptUtcTicks <= now.UtcTicks)
                .OrderBy(mutation => mutation.NextAttemptUtcTicks)
                .Take(Math.Max(0, maximumCount))
                .Select(Clone)
                .ToList();

        public static int RemoveExpired(List<PendingMailMutation> queue, DateTimeOffset now)
            => queue.RemoveAll(mutation => MailMutationRetryPolicy.IsExpired(mutation.CreatedUtcTicks, now));

        public static int RemoveAccount(List<PendingMailMutation> queue, string accountId)
            => queue.RemoveAll(mutation => mutation.AccountId == accountId);

        public static bool Remove(List<PendingMailMutation> queue, PendingMailMutation mutation)
            => queue.RemoveAll(candidate => IsSame(candidate, mutation)) > 0;

        public static PendingMailMutation? Find(List<PendingMailMutation> queue, PendingMailMutation mutation)
            => queue.FirstOrDefault(candidate => IsSame(candidate, mutation));

        public static bool IsSame(PendingMailMutation left, PendingMailMutation right)
            => left.AccountId == right.AccountId && left.FolderId == right.FolderId && left.MessageId == right.MessageId;

        public static PendingMailMutation Clone(PendingMailMutation mutation)
            => new()
            {
                AccountId = mutation.AccountId,
                FolderId = mutation.FolderId,
                MessageId = mutation.MessageId,
                ProviderKind = mutation.ProviderKind,
                ImapUidValidity = mutation.ImapUidValidity,
                FailureCount = mutation.FailureCount,
                CreatedUtcTicks = mutation.CreatedUtcTicks,
                NextAttemptUtcTicks = mutation.NextAttemptUtcTicks
            };
    }
}
