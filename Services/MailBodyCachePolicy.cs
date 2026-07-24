using System;
using System.Collections.Generic;
using System.Linq;

namespace Task_Flyout.Services
{
    public readonly record struct MailBodyCacheEntry(
        string Key,
        string AccountId,
        long RetainedBytes,
        long AccessSequence);

    public static class MailBodyCachePolicy
    {
        public static long GetRetainedUtf16Bytes(string? bodyText, string? htmlBody)
            => checked(((long)(bodyText?.Length ?? 0) + (htmlBody?.Length ?? 0)) * sizeof(char));

        public static IReadOnlyList<string> SelectEvictions(
            IEnumerable<MailBodyCacheEntry> entries,
            long perAccountMaxBytes,
            long perAccountTargetBytes,
            long globalMaxBytes,
            long globalTargetBytes,
            string? activeAccountId,
            string? protectedKey = null)
        {
            ArgumentNullException.ThrowIfNull(entries);
            ValidateBudget(perAccountMaxBytes, perAccountTargetBytes, nameof(perAccountMaxBytes));
            ValidateBudget(globalMaxBytes, globalTargetBytes, nameof(globalMaxBytes));

            var retained = entries
                .Where(entry => entry.RetainedBytes > 0)
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .ToDictionary(entry => entry.Key, StringComparer.Ordinal);
            var evictions = new List<string>();

            foreach (var account in retained.Values
                         .GroupBy(entry => entry.AccountId, StringComparer.Ordinal)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                long accountBytes = account.Sum(entry => entry.RetainedBytes);
                if (accountBytes <= perAccountMaxBytes) continue;

                foreach (var entry in OrderLru(account).Where(entry => entry.Key != protectedKey))
                {
                    retained.Remove(entry.Key);
                    evictions.Add(entry.Key);
                    accountBytes -= entry.RetainedBytes;
                    if (accountBytes <= perAccountTargetBytes) break;
                }
            }

            long globalBytes = retained.Values.Sum(entry => entry.RetainedBytes);
            if (globalBytes <= globalMaxBytes) return evictions;

            var globalCandidates = retained.Values
                .Where(entry => entry.Key != protectedKey)
                .OrderBy(entry => string.Equals(entry.AccountId, activeAccountId, StringComparison.Ordinal) ? 1 : 0)
                .ThenBy(entry => entry.AccessSequence)
                .ThenBy(entry => entry.AccountId, StringComparer.Ordinal)
                .ThenBy(entry => entry.Key, StringComparer.Ordinal);

            foreach (var entry in globalCandidates)
            {
                evictions.Add(entry.Key);
                globalBytes -= entry.RetainedBytes;
                if (globalBytes <= globalTargetBytes) break;
            }

            return evictions;
        }

        private static IOrderedEnumerable<MailBodyCacheEntry> OrderLru(IEnumerable<MailBodyCacheEntry> entries)
            => entries.OrderBy(entry => entry.AccessSequence).ThenBy(entry => entry.Key, StringComparer.Ordinal);

        private static void ValidateBudget(long maxBytes, long targetBytes, string parameterName)
        {
            if (targetBytes < 0 || maxBytes < targetBytes)
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
