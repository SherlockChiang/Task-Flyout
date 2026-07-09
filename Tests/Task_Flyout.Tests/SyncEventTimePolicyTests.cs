using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class SyncEventTimePolicyTests
{
    [Fact]
    public void Create_without_start_time_returns_all_day_window()
    {
        var window = SyncEventTimePolicy.Create(new DateTime(2026, 7, 9, 14, 30, 0), null, null);

        Assert.True(window.IsAllDay);
        Assert.Equal(new DateTime(2026, 7, 9), window.Start);
        Assert.Equal(new DateTime(2026, 7, 10), window.End);
    }

    [Fact]
    public void Create_with_start_and_end_time_returns_timed_window()
    {
        var window = SyncEventTimePolicy.Create(
            new DateTime(2026, 7, 9, 14, 30, 0),
            new TimeSpan(9, 15, 0),
            new TimeSpan(10, 45, 0));

        Assert.False(window.IsAllDay);
        Assert.Equal(new DateTime(2026, 7, 9, 9, 15, 0), window.Start);
        Assert.Equal(new DateTime(2026, 7, 9, 10, 45, 0), window.End);
    }

    [Fact]
    public void Create_without_end_time_defaults_to_one_hour()
    {
        var window = SyncEventTimePolicy.Create(
            new DateTime(2026, 7, 9),
            new TimeSpan(9, 15, 0),
            null);

        Assert.False(window.IsAllDay);
        Assert.Equal(new DateTime(2026, 7, 9, 9, 15, 0), window.Start);
        Assert.Equal(new DateTime(2026, 7, 9, 10, 15, 0), window.End);
    }

    [Fact]
    public void Create_moves_end_to_next_day_when_timed_window_crosses_midnight()
    {
        var window = SyncEventTimePolicy.Create(
            new DateTime(2026, 7, 9),
            new TimeSpan(23, 30, 0),
            new TimeSpan(0, 15, 0));

        Assert.False(window.IsAllDay);
        Assert.Equal(new DateTime(2026, 7, 9, 23, 30, 0), window.Start);
        Assert.Equal(new DateTime(2026, 7, 10, 0, 15, 0), window.End);
    }
}
