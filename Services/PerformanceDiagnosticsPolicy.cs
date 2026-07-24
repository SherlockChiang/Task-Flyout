using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Task_Flyout.Services
{
    internal sealed class PerformanceOncePolicy
    {
        private readonly ConcurrentDictionary<string, byte> _keys = new(StringComparer.Ordinal);

        public bool TryClaim(string key) => _keys.TryAdd(key, 0);
        public bool IsClaimed(string key) => _keys.ContainsKey(key);
    }

    internal static class PerformancePercentilePolicy
    {
        public static double? NearestRank(IEnumerable<double> values, int percentile, int minimumSamples = 1)
        {
            if (percentile is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(percentile));
            var ordered = values.OrderBy(value => value).ToArray();
            if (ordered.Length < minimumSamples) return null;
            int index = (int)Math.Ceiling(percentile / 100d * ordered.Length) - 1;
            return ordered[index];
        }
    }
}
