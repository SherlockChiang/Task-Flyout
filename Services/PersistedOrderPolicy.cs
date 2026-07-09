using System;
using System.Collections.Generic;
using System.Linq;

namespace Task_Flyout.Services
{
    internal static class PersistedOrderPolicy
    {
        public static List<T> Apply<T>(IEnumerable<T> items, IEnumerable<string>? order, Func<T, string> keySelector)
        {
            var list = items.ToList();
            if (order == null)
                return list;

            var orderIndex = order
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select((id, index) => new { id, index })
                .GroupBy(item => item.id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().index, StringComparer.Ordinal);

            if (orderIndex.Count == 0)
                return list;

            return list
                .Select((item, originalIndex) => new { item, originalIndex })
                .OrderBy(entry => orderIndex.TryGetValue(keySelector(entry.item), out var index) ? index : int.MaxValue)
                .ThenBy(entry => entry.originalIndex)
                .Select(entry => entry.item)
                .ToList();
        }
    }
}
