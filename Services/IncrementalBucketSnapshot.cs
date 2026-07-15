using System;
using System.Collections.Generic;

namespace Task_Flyout.Services
{
    internal static class IncrementalBucketSnapshot
    {
        public static Dictionary<string, List<T>> Create<T>(
            IReadOnlyDictionary<string, List<T>> source,
            IReadOnlyDictionary<string, List<T>>? previous,
            Func<T, T, bool> areEqual,
            Func<T, T> clone)
        {
            var snapshot = new Dictionary<string, List<T>>(source.Count, StringComparer.Ordinal);
            foreach (var (key, sourceItems) in source)
            {
                if (previous != null && previous.TryGetValue(key, out var previousItems)
                    && BucketsEqual(sourceItems, previousItems, areEqual))
                {
                    snapshot[key] = previousItems;
                    continue;
                }

                var clonedItems = new List<T>(sourceItems.Count);
                foreach (var item in sourceItems)
                    clonedItems.Add(clone(item));
                snapshot[key] = clonedItems;
            }
            return snapshot;
        }

        private static bool BucketsEqual<T>(IReadOnlyList<T> left, IReadOnlyList<T> right, Func<T, T, bool> areEqual)
        {
            if (left.Count != right.Count) return false;
            for (var index = 0; index < left.Count; index++)
            {
                if (!areEqual(left[index], right[index])) return false;
            }
            return true;
        }
    }
}
