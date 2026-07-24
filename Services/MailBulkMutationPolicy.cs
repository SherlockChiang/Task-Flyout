using System;
using System.Collections.Generic;
using System.Linq;

namespace Task_Flyout.Services
{
    internal static class MailBulkMutationPolicy
    {
        public const int MaximumItems = 100;
        public const int MaximumConcurrency = 4;

        public static IReadOnlyList<T> SelectBounded<T>(IEnumerable<T> items)
            => items.Take(MaximumItems).ToList();

        public static bool ShouldRollback(long attemptedIntent, long currentIntent)
            => attemptedIntent == currentIntent;
    }
}
