using System;
using System.Collections.Generic;
using System.Linq;

namespace Task_Flyout.Services
{
    internal static class MailPersistentCachePolicy
    {
        public static bool Normalize(
            MailPersistentCache cache,
            DateTimeOffset now,
            int maximumMessages,
            bool removeExpiredMutations = true)
        {
            maximumMessages = Math.Max(0, maximumMessages);
            bool dirty = false;
            dirty |= EnsureCollections(cache);
            dirty |= MailPendingMutationPolicy.MigrateLegacyAndDeduplicate(cache.PendingMutations);
            if (removeExpiredMutations)
                dirty |= MailPendingMutationPolicy.RemoveExpired(cache.PendingMutations, now) > 0;

            foreach (var key in cache.Messages.Keys.ToList())
            {
                if (!MailCacheKeyPolicy.TryCanonicalizeLegacy(key, out var canonicalKey)) continue;
                var combined = cache.Messages.TryGetValue(canonicalKey, out var existing) ? existing.Concat(cache.Messages[key]) : cache.Messages[key];
                cache.Messages[canonicalKey] = combined.GroupBy(item => item.Id, StringComparer.Ordinal)
                    .Select(group => group.OrderByDescending(item => item.RawReceivedTime).First()).ToList();
                if (cache.MessageFetchedUtcTicks.Remove(key, out var legacyFetchedTicks))
                {
                    cache.MessageFetchedUtcTicks[canonicalKey] = cache.MessageFetchedUtcTicks.TryGetValue(canonicalKey, out var currentFetchedTicks)
                        ? Math.Max(currentFetchedTicks, legacyFetchedTicks)
                        : legacyFetchedTicks;
                }
                cache.Messages.Remove(key);
                dirty = true;
            }

            foreach (var key in cache.Messages.Keys.ToList())
            {
                var source = cache.Messages[key];
                if (source.Count > maximumMessages || source.Any(item => !string.IsNullOrEmpty(item.BodyText) || !string.IsNullOrEmpty(item.HtmlBody))) dirty = true;
                cache.Messages[key] = source.OrderByDescending(item => item.RawReceivedTime).Take(maximumMessages).Select(WithoutBody).ToList();
            }

            dirty |= RemoveOrphanTimestamps(cache.FolderFetchedUtcTicks, cache.Folders.Keys);
            dirty |= RemoveOrphanTimestamps(cache.MessageFetchedUtcTicks, cache.Messages.Keys);
            return dirty;
        }

        public static bool CanUseWindow(MailAccountKind kind, IEnumerable<MailItem> items)
            => kind != MailAccountKind.Imap || items.All(item => item.ImapUidValidity.HasValue);

        public static bool IsFresh(long? fetchedUtcTicks, DateTimeOffset now, TimeSpan lifetime)
        {
            if (!fetchedUtcTicks.HasValue || fetchedUtcTicks.Value <= 0 || lifetime <= TimeSpan.Zero)
                return false;

            long nowTicks = now.UtcTicks;
            return fetchedUtcTicks.Value <= nowTicks && nowTicks - fetchedUtcTicks.Value < lifetime.Ticks;
        }

        private static bool EnsureCollections(MailPersistentCache cache)
        {
            bool dirty = cache.Folders == null || cache.FolderFetchedUtcTicks == null || cache.Messages == null || cache.MessageFetchedUtcTicks == null || cache.MessageCursors == null || cache.MessageHasMore == null || cache.PendingMutations == null || cache.LastSeenInboxTicks == null || cache.AccountOrder == null || cache.FolderOrder == null;
            cache.Folders ??= new(); cache.FolderFetchedUtcTicks ??= new(); cache.Messages ??= new(); cache.MessageFetchedUtcTicks ??= new(); cache.MessageCursors ??= new(); cache.MessageHasMore ??= new();
            cache.PendingMutations ??= new(); cache.LastSeenInboxTicks ??= new(); cache.AccountOrder ??= new(); cache.FolderOrder ??= new();
            return dirty;
        }

        private static bool RemoveOrphanTimestamps(Dictionary<string, long> timestamps, IEnumerable<string> retainedKeys)
        {
            var retained = retainedKeys.ToHashSet(StringComparer.Ordinal);
            var orphanKeys = timestamps.Keys.Where(key => !retained.Contains(key)).ToList();
            if (orphanKeys.Count == 0) return false;
            foreach (var key in orphanKeys)
                timestamps.Remove(key);
            return true;
        }

        private static MailItem WithoutBody(MailItem item) => new()
        {
            AccountId = item.AccountId, FolderId = item.FolderId, Id = item.Id, ImapUidValidity = item.ImapUidValidity,
            Subject = item.Subject, Sender = item.Sender, SenderAddress = item.SenderAddress, Recipient = item.Recipient,
            Preview = item.Preview, ReceivedTime = item.ReceivedTime, RawReceivedTime = item.RawReceivedTime, IsRead = item.IsRead, IsFlagged = item.IsFlagged,
            HasAttachments = item.HasAttachments, Importance = item.Importance, WebLink = item.WebLink
        };
    }
}
