using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class BackgroundRefreshSchedulePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void New_enabled_account_is_due()
        => Assert.True(BackgroundRefreshSchedulePolicy.IsMailPollDue(Now, DateTimeOffset.MinValue, 15, true, true));

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Disabled_or_accountless_polling_is_not_due(bool enabled, bool hasAccounts)
        => Assert.False(BackgroundRefreshSchedulePolicy.IsMailPollDue(Now, DateTimeOffset.MinValue, 15, enabled, hasAccounts));

    [Fact]
    public void Poll_is_due_at_interval_boundary_but_not_before()
    {
        Assert.False(BackgroundRefreshSchedulePolicy.IsMailPollDue(Now, Now.AddMinutes(-14), 15, true, true));
        Assert.True(BackgroundRefreshSchedulePolicy.IsMailPollDue(Now, Now.AddMinutes(-15), 15, true, true));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(500, 240)]
    public void Interval_is_clamped(int configured, int effective)
    {
        Assert.False(BackgroundRefreshSchedulePolicy.IsMailPollDue(Now, Now.AddMinutes(-effective).AddSeconds(1), configured, true, true));
        Assert.True(BackgroundRefreshSchedulePolicy.IsMailPollDue(Now, Now.AddMinutes(-effective), configured, true, true));
    }

    [Fact]
    public void Agenda_refresh_is_due_for_a_new_account_but_not_without_accounts()
    {
        Assert.True(BackgroundRefreshSchedulePolicy.IsAgendaRefreshDue(
            Now, DateTimeOffset.MinValue, 15, hasAccounts: true));
        Assert.False(BackgroundRefreshSchedulePolicy.IsAgendaRefreshDue(
            Now, DateTimeOffset.MinValue, 15, hasAccounts: false));
    }

    [Fact]
    public void Agenda_refresh_uses_the_current_interval_at_each_heartbeat()
    {
        var lastStarted = Now.AddMinutes(-10);

        Assert.False(BackgroundRefreshSchedulePolicy.IsAgendaRefreshDue(
            Now, lastStarted, 15, hasAccounts: true));
        Assert.True(BackgroundRefreshSchedulePolicy.IsAgendaRefreshDue(
            Now, lastStarted, 5, hasAccounts: true));
    }

    [Fact]
    public void Agenda_refresh_range_is_normalized_around_the_local_day()
    {
        var anchor = new DateTime(2026, 7, 23, 18, 45, 0, DateTimeKind.Local);

        var (min, max) = BackgroundRefreshSchedulePolicy.GetAgendaRefreshRange(anchor);

        Assert.Equal(new DateTime(2026, 7, 9), min);
        Assert.Equal(new DateTime(2026, 10, 21), max);
    }
}
