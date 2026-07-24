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

    public enum MailMutationKind
    {
        Unknown = 0,
        SetReadState = 1,
        SetFlagged = 2
    }

    public sealed class PendingMailMutation
    {
        public string AccountId { get; set; } = "";
        public string FolderId { get; set; } = "";
        public string MessageId { get; set; } = "";
        public MailAccountKind ProviderKind { get; set; }
        public uint? ImapUidValidity { get; set; }
        public MailMutationKind Kind { get; set; }
        public bool Value { get; set; }
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
            NormalizeLegacy(mutation);
            var pending = queue.FirstOrDefault(candidate => IsSame(candidate, mutation));
            if (pending == null)
            {
                pending = Clone(mutation);
                pending.CreatedUtcTicks = now.UtcTicks;
                queue.Add(pending);
            }
            else
            {
                bool replacesIntent = pending.Value != mutation.Value;
                pending.ProviderKind = mutation.ProviderKind;
                pending.ImapUidValidity = mutation.ImapUidValidity;
                pending.Value = mutation.Value;
                if (replacesIntent)
                {
                    pending.CreatedUtcTicks = now.UtcTicks;
                    pending.FailureCount = 0;
                }
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
            => queue.RemoveAll(candidate => IsSame(candidate, mutation) && candidate.Value == mutation.Value) > 0;

        public static bool RemoveIfCurrent(List<PendingMailMutation> queue, PendingMailMutation mutation)
            => queue.RemoveAll(candidate => IsCurrentIntent(candidate, mutation)) > 0;

        public static bool RemoveKind(List<PendingMailMutation> queue, PendingMailMutation mutation)
            => queue.RemoveAll(candidate => IsSame(candidate, mutation)) > 0;

        public static PendingMailMutation? Find(List<PendingMailMutation> queue, PendingMailMutation mutation)
            => queue.FirstOrDefault(candidate => IsSame(candidate, mutation));

        public static bool IsSame(PendingMailMutation left, PendingMailMutation right)
            => left.AccountId == right.AccountId && left.FolderId == right.FolderId && left.MessageId == right.MessageId &&
               EffectiveKind(left) == EffectiveKind(right);

        public static bool IsCurrentIntent(PendingMailMutation current, PendingMailMutation attempted)
            => IsSame(current, attempted) && current.Value == attempted.Value && current.CreatedUtcTicks == attempted.CreatedUtcTicks;

        public static bool MigrateLegacyAndDeduplicate(List<PendingMailMutation> queue)
        {
            bool changed = false;
            foreach (var mutation in queue)
            {
                if (mutation.Kind != MailMutationKind.Unknown) continue;
                NormalizeLegacy(mutation);
                changed = true;
            }

            var retained = queue
                .GroupBy(MutationKey, StringComparer.Ordinal)
                .Select(group => group.OrderByDescending(mutation => mutation.CreatedUtcTicks).First())
                .ToList();
            if (retained.Count != queue.Count)
            {
                queue.Clear();
                queue.AddRange(retained);
                changed = true;
            }
            return changed;
        }

        private static MailMutationKind EffectiveKind(PendingMailMutation mutation)
            => mutation.Kind == MailMutationKind.Unknown ? MailMutationKind.SetReadState : mutation.Kind;

        private static void NormalizeLegacy(PendingMailMutation mutation)
        {
            if (mutation.Kind != MailMutationKind.Unknown) return;
            mutation.Kind = MailMutationKind.SetReadState;
            mutation.Value = true;
        }

        private static string MutationKey(PendingMailMutation mutation)
            => $"{mutation.AccountId}\u001f{mutation.FolderId}\u001f{mutation.MessageId}\u001f{(int)EffectiveKind(mutation)}";

        public static PendingMailMutation Clone(PendingMailMutation mutation)
            => new()
            {
                AccountId = mutation.AccountId,
                FolderId = mutation.FolderId,
                MessageId = mutation.MessageId,
                ProviderKind = mutation.ProviderKind,
                ImapUidValidity = mutation.ImapUidValidity,
                Kind = EffectiveKind(mutation),
                Value = mutation.Kind == MailMutationKind.Unknown || mutation.Value,
                FailureCount = mutation.FailureCount,
                CreatedUtcTicks = mutation.CreatedUtcTicks,
                NextAttemptUtcTicks = mutation.NextAttemptUtcTicks
            };
    }
}
