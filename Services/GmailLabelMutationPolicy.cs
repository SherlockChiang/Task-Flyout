using System;
using System.Collections.Generic;
using System.Linq;

namespace Task_Flyout.Services
{
    internal static class GmailLabelMutationPolicy
    {
        public static IReadOnlyList<GmailLabelDestination> Destinations(
            IEnumerable<GmailLabelDestination> labels,
            string currentLabelId)
            => labels
                .Where(label => label.IsUserLabel && !string.IsNullOrWhiteSpace(label.Id))
                .Where(label => !string.Equals(label.Id, currentLabelId, StringComparison.Ordinal))
                .GroupBy(label => label.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(label => label.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        public static bool CanRemoveSource(string sourceLabelId, bool isUserLabel)
            => isUserLabel || string.Equals(sourceLabelId, "INBOX", StringComparison.Ordinal);

        public static IReadOnlyList<string> ActualAddedLabels(
            IEnumerable<string> beforeLabels,
            IEnumerable<string> requestedLabels)
        {
            var before = beforeLabels.ToHashSet(StringComparer.Ordinal);
            return requestedLabels.Where(label => !before.Contains(label)).Distinct(StringComparer.Ordinal).ToList();
        }

        public static IReadOnlyList<string> ActualRemovedLabels(
            IEnumerable<string> beforeLabels,
            IEnumerable<string> requestedLabels)
        {
            var before = beforeLabels.ToHashSet(StringComparer.Ordinal);
            return requestedLabels.Where(before.Contains).Distinct(StringComparer.Ordinal).ToList();
        }

        public static IReadOnlyList<string> AffectedLabels(
            IEnumerable<string> beforeLabels,
            IEnumerable<string> afterLabels,
            string sourceLabelId)
        {
            var before = beforeLabels.ToHashSet(StringComparer.Ordinal);
            var after = afterLabels.ToHashSet(StringComparer.Ordinal);
            before.SymmetricExceptWith(after);
            before.Add(sourceLabelId);
            return before.ToList();
        }

        public static IReadOnlyList<string> InvalidatedWindowKeys(string accountId, IEnumerable<string> labelIds)
            => labelIds
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Distinct(StringComparer.Ordinal)
                .SelectMany(label => new[]
                {
                    MailCacheKeyPolicy.Build(accountId, label, false),
                    MailCacheKeyPolicy.Build(accountId, label, true)
                })
                .ToList();

        public static int? AdjustUnreadCount(int? count, bool messageIsRead, bool wasMember, bool isMember)
        {
            if (!count.HasValue || messageIsRead || wasMember == isMember) return count;
            return isMember ? count.Value + 1 : Math.Max(0, count.Value - 1);
        }
    }
}
