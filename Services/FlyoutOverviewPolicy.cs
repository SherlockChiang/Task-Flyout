using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Task_Flyout.Services;

internal sealed record FlyoutOverviewEntry(string Key, string DateKey, DateTime? Start, bool CompletedTask);

internal static class FlyoutOverviewPolicy
{
    internal static IReadOnlyList<FlyoutOverviewEntry> Select(IEnumerable<FlyoutOverviewEntry> items, DateTime today)
        => items.Where(item => !item.CompletedTask && DateTime.TryParseExact(item.DateKey, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && day >= today.Date)
            .DistinctBy(item => item.Key)
            .OrderBy(item => item.DateKey, StringComparer.Ordinal)
            .ThenBy(item => item.Start ?? DateTime.MinValue)
            .Take(5).ToArray();

    // Reserve the footer and at least 100 DIPs for agenda/editor content.
    // The date picker scrolls independently when scaling leaves little space.
    internal static double HeaderMaximum(double availableHeight, double footerHeight)
        => Math.Max(0, Math.Min(availableHeight * 0.6, availableHeight - footerHeight - 100));
}
