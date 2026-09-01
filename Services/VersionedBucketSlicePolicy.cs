using System;
using System.Collections.Generic;

namespace Task_Flyout.Services
{
    internal sealed record VersionedBucketSlice<T>(
        long Version,
        Dictionary<string, List<T>> Buckets);

    internal static class VersionedBucketSlicePolicy
    {
        public static VersionedBucketSlice<T>? CreateIfChanged<T>(
            long knownVersion,
            long publishedVersion,
            IReadOnlyDictionary<string, List<T>> source,
            IEnumerable<string> keys,
            Func<T, T> clone)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(keys);
            ArgumentNullException.ThrowIfNull(clone);

            if (knownVersion == publishedVersion) return null;

            var buckets = new Dictionary<string, List<T>>(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in keys)
            {
                if (string.IsNullOrWhiteSpace(key) || !seen.Add(key)) continue;
                if (!source.TryGetValue(key, out var items)) continue;

                var clonedItems = new List<T>(items.Count);
                foreach (var item in items)
                    clonedItems.Add(clone(item));
                buckets[key] = clonedItems;
            }

            return new VersionedBucketSlice<T>(publishedVersion, buckets);
        }
    }
}
