using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class TodayDashboardPolicyTests
{
    private static readonly DateTime Now = new(2026, 10, 25, 12, 0, 0);

    [Fact]
    public void Due_tasks_include_old_overdue_items_but_exclude_completed_future_and_undated()
    {
        var entries = new[]
        {
            Entry("old", "2020-01-01", task: true),
            Entry("today", "2026-10-25", task: true),
            Entry("future", "2026-10-26", task: true),
            Entry("done", "2026-10-24", task: true) with { IsCompleted = true },
            Entry("undated", "", task: true),
            Entry("event", "2026-10-24")
        };
        Assert.Equal(new[] { "old", "today" }, TodayDashboardPolicy.DueTasks(entries, Now).Select(item => item.Key));
    }

    [Fact]
    public void Upcoming_keeps_ongoing_and_all_day_events_and_excludes_finished_events()
    {
        var entries = new[]
        {
            Entry("ended", "2026-10-25") with { Start = Now.AddHours(-2), End = Now },
            Entry("ongoing", "2026-10-25") with { Start = Now.AddHours(-1), End = Now.AddHours(1) },
            Entry("all-day", "2026-10-25"),
            Entry("yesterday", "2026-10-24"),
            Entry("unknown", "invalid"),
            Entry("task", "2026-10-25", task: true),
            Entry("no-end-past", "2026-10-25") with { Start = Now.AddMinutes(-1) }
        };
        Assert.Equal(new[] { "all-day", "ongoing" }, TodayDashboardPolicy.Upcoming(entries, Now).Select(item => item.Key));
    }

    [Fact]
    public void Multiday_all_day_event_remains_upcoming_until_its_exclusive_end()
    {
        var current = Entry("multiday", "2026-10-24") with { End = Now.Date.AddDays(1) };
        var ended = Entry("finished", "2026-10-24") with { End = Now.Date };
        Assert.Equal(new[] { current }, TodayDashboardPolicy.Upcoming(new[] { current, ended }, Now));
    }

    [Fact]
    public void Upcoming_orders_limits_and_preserves_distinct_account_identities()
    {
        var entries = Enumerable.Range(1, 8).Reverse().Select(index =>
            Entry("account-" + index, "2026-10-25") with { Start = Now.AddHours(index), Title = "Same item" }).ToList();
        entries.Add(entries[0]);
        Assert.Equal(Enumerable.Range(1, 5).Select(index => "account-" + index),
            TodayDashboardPolicy.Upcoming(entries, Now).Select(item => item.Key));
    }

    [Fact]
    public void Inbox_count_does_not_sum_overlapping_gmail_labels()
    {
        var folders = new (string, string, int?)[]
        {
            ("ALL_MAIL", "All Mail", 10), ("INBOX", "Inbox", 3), ("label-a", "Projects", 3)
        };
        Assert.Equal(3L, TodayDashboardPolicy.InboxUnread(folders));
    }

    [Fact]
    public void Uncached_inbox_is_unknown_instead_of_zero()
    {
        Assert.Null(TodayDashboardPolicy.InboxUnread(Array.Empty<(string, string, int?)>()));
        Assert.Null(TodayDashboardPolicy.InboxUnread(new (string, string, int?)[] { ("INBOX", "Inbox", null) }));
        Assert.Equal(0L, TodayDashboardPolicy.InboxUnread(new (string, string, int?)[] { ("opaque-id", "收件匣", 0) }));
    }

    private static TodayAgendaEntry Entry(string key, string date, bool task = false)
        => new(key, key, "", date, null, null, task, false);
}
