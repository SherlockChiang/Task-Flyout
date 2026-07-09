using System;
using System.Collections.Generic;
using System.Linq;

namespace Task_Flyout.Services
{
    internal readonly record struct CachePruneEntry(string Key, long Length, DateTime LastWriteUtc);

    internal static class CachePrunePolicy
    {
        public static IReadOnlyList<string> SelectOldestUntilTarget(
            IEnumerable<CachePruneEntry> entries,
            long maxBytes,
            long targetBytes)
        {
            var live = entries
                .Where(entry => entry.Length > 0)
                .ToList();
            var total = live.Sum(entry => entry.Length);
            if (total <= maxBytes)
                return Array.Empty<string>();

            var selected = new List<string>();
            foreach (var entry in live.OrderBy(entry => entry.LastWriteUtc).ThenBy(entry => entry.Key, StringComparer.Ordinal))
            {
                selected.Add(entry.Key);
                total -= entry.Length;
                if (total <= targetBytes)
                    break;
            }

            return selected;
        }
    }
}
