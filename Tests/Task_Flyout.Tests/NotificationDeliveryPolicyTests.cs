using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public sealed class NotificationDeliveryPolicyTests
{
    [Theory]
    [InlineData(false, 1320, 480, 1380, false)]
    [InlineData(true, 1320, 480, 1319, false)]
    [InlineData(true, 1320, 480, 1320, true)]
    [InlineData(true, 1320, 480, 0, true)]
    [InlineData(true, 1320, 480, 479, true)]
    [InlineData(true, 1320, 480, 480, false)]
    [InlineData(true, 540, 1020, 539, false)]
    [InlineData(true, 540, 1020, 540, true)]
    [InlineData(true, 540, 1020, 1020, false)]
    [InlineData(true, 540, 540, 0, true)]
    [InlineData(true, -1, 2000, 0, true)]
    public void Quiet_hours_respect_boundaries_and_midnight(bool enabled, int start, int end, int now, bool expected)
        => Assert.Equal(expected, NotificationDeliveryPolicy.IsQuiet(enabled, start, end, TimeSpan.FromMinutes(now)));

    [Theory]
    [InlineData(5, 5)]
    [InlineData(10, 10)]
    [InlineData(15, 15)]
    [InlineData(30, 30)]
    [InlineData(60, 60)]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(int.MaxValue, 10)]
    public void Snooze_is_bounded(int value, int expected)
        => Assert.Equal(expected, NotificationDeliveryPolicy.SnoozeMinutes(value));

    [Fact]
    public void Priority_filter_preserves_events_and_only_allows_important_tasks_or_mail()
    {
        Assert.True(NotificationDeliveryPolicy.AllowsAgenda(true, false, false));
        Assert.False(NotificationDeliveryPolicy.AllowsAgenda(true, true, false));
        Assert.True(NotificationDeliveryPolicy.AllowsAgenda(true, true, true));
        Assert.True(NotificationDeliveryPolicy.AllowsAgenda(false, true, false));
        Assert.True(NotificationDeliveryPolicy.AllowsMail(true, true, ""));
        Assert.True(NotificationDeliveryPolicy.AllowsMail(true, false, "High"));
        Assert.False(NotificationDeliveryPolicy.AllowsMail(true, false, null));
        Assert.False(NotificationDeliveryPolicy.AllowsMail(true, false, "normal"));
        Assert.True(NotificationDeliveryPolicy.AllowsMail(false, false, "normal"));
    }

    [Fact]
    public void Tags_are_stable_and_isolate_accounts_lists_items_dates_and_minutes()
    {
        var start = new DateTime(2026, 10, 25, 9, 0, 0);
        string Tag(string account = "Google:a", string list = "list", string id = "task", string date = "2026-10-25", DateTime? time = null)
            => NotificationDeliveryPolicy.AgendaTag(account, list, id, date, time ?? start);
        var tag = Tag();
        Assert.Equal(16, tag.Length);
        Assert.Equal(tag, Tag(account: "google:A", time: DateTime.SpecifyKind(start.AddSeconds(30), DateTimeKind.Utc)));
        Assert.NotEqual(tag, Tag(account: "Google:b"));
        Assert.NotEqual(tag, Tag(list: "other-list"));
        Assert.NotEqual(tag, Tag(id: "other-task"));
        Assert.NotEqual(tag, Tag(date: "2026-10-26"));
        Assert.NotEqual(tag, Tag(time: start.AddMinutes(1)));
        Assert.NotEqual(Tag(list: "a|b", id: "c"), Tag(list: "a", id: "b|c"));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("9223372036854775807")]
    [InlineData("99999999999999999999999")]
    [InlineData("corrupt")]
    public void Invalid_persisted_timestamps_are_rejected(string value)
        => Assert.False(NotificationDeliveryPolicy.TryReadUtcTicks(value, out _));

    [Fact]
    public void Valid_timestamp_round_trips()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.True(NotificationDeliveryPolicy.TryReadUtcTicks(now.UtcTicks.ToString(), out var result));
        Assert.Equal(now, result);
    }
}
