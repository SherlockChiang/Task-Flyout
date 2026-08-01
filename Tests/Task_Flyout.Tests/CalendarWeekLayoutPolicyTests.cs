using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class CalendarWeekLayoutPolicyTests
{
    [Fact]
    public void Time_axis_labels_every_three_hours_and_includes_day_end()
    {
        var labels = Enumerable.Range(0, 25)
            .Where(CalendarWeekLayoutPolicy.ShouldShowTimeLabel)
            .ToArray();

        Assert.Equal(new[] { 0, 3, 6, 9, 12, 15, 18, 21, 24 }, labels);
    }

    [Fact]
    public void Event_time_contains_start_and_end()
    {
        var start = new DateTime(2026, 7, 30, 9, 5, 0);
        var end = new DateTime(2026, 7, 30, 10, 45, 0);

        Assert.Equal(
            "09:05\u201310:45",
            CalendarWeekLayoutPolicy.FormatEventTime(start, end, isAllDay: false, "All Day"));
    }

    [Fact]
    public void Event_time_handles_all_day_and_missing_end()
    {
        var start = new DateTime(2026, 7, 30, 9, 5, 0);

        Assert.Equal("All Day", CalendarWeekLayoutPolicy.FormatEventTime(start, null, true, "All Day"));
        Assert.Equal("09:05", CalendarWeekLayoutPolicy.FormatEventTime(start, null, false, "All Day"));
    }

    [Fact]
    public void Compact_event_text_keeps_time_before_title()
    {
        Assert.Equal(
            "09:05\u201310:45  Thesis review",
            CalendarWeekLayoutPolicy.FormatCompactEventText("Thesis review", "09:05\u201310:45"));
    }
}
