using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Task_Flyout.Services;

internal sealed record TodayAgendaEntry(
    string Key, string Title, string Subtitle, string DateKey,
    DateTime? Start, DateTime? End, bool IsTask, bool IsCompleted);

internal static class TodayDashboardPolicy
{
    internal static IReadOnlyList<TodayAgendaEntry> Upcoming(IEnumerable<TodayAgendaEntry> items, DateTime now)
        => items.Where(item => !item.IsTask && IsUpcoming(item, now))
            .DistinctBy(item => item.Key)
            .OrderBy(item => item.Start ?? ParseDate(item.DateKey))
            .ThenBy(item => item.Title, StringComparer.CurrentCulture)
            .Take(5).ToArray();

    internal static IReadOnlyList<TodayAgendaEntry> DueTasks(IEnumerable<TodayAgendaEntry> items, DateTime today)
        => items.Where(item => item.IsTask && !item.IsCompleted
                && ParseDate(item.DateKey) is DateTime due && due.Date <= today.Date)
            .DistinctBy(item => item.Key)
            .OrderBy(item => ParseDate(item.DateKey))
            .ThenBy(item => item.Title, StringComparer.CurrentCulture).ToArray();

    // A missing folder/count is unknown, not zero. Labels and All Mail overlap
    // with Inbox on Gmail, so never sum every folder's unread count.
    internal static long? InboxUnread(IEnumerable<(string Id, string Name, int? Unread)> folders)
    {
        var inbox = folders.FirstOrDefault(folder => IsInbox(folder.Id) || IsInbox(folder.Name));
        return inbox.Unread.HasValue ? Math.Max(0, inbox.Unread.Value) : null;
    }

    private static bool IsInbox(string value)
        => string.Equals(value, "INBOX", StringComparison.OrdinalIgnoreCase)
            || value is "收件箱" or "收件匣";

    private static bool IsUpcoming(TodayAgendaEntry item, DateTime now)
    {
        // Google all-day events carry only DateKey and an exclusive End.
        if (item.End is DateTime end && (item.Start ?? ParseDate(item.DateKey)) is DateTime start && end > start)
            return end > now;
        return item.Start.HasValue ? item.Start.Value >= now
            : ParseDate(item.DateKey) is DateTime date && date >= now.Date;
    }

    private static DateTime? ParseDate(string value)
        => DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date) ? date : null;
}
