using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public sealed class CalendarEventTimePolicyTests
{
    [Fact]
    public void Editor_uses_model_times_instead_of_stale_subtitle()
    {
        var state = CalendarEventTimePolicy.CreateEditorState(
            new DateTime(2026, 9, 2, 9, 15, 0),
            new DateTime(2026, 9, 2, 11, 45, 0),
            isAllDay: false,
            legacySubtitle: "08:00 - 09:00");

        Assert.False(state.IsAllDay);
        Assert.Equal(new TimeSpan(9, 15, 0), state.StartTime);
        Assert.Equal(new TimeSpan(11, 45, 0), state.EndTime);
    }

    [Fact]
    public void Editor_defaults_missing_end_and_wraps_after_midnight()
    {
        var state = CalendarEventTimePolicy.CreateEditorState(
            new DateTime(2026, 9, 2, 23, 30, 0),
            endDateTime: null,
            isAllDay: false);

        Assert.Equal(new TimeSpan(23, 30, 0), state.StartTime);
        Assert.Equal(new TimeSpan(0, 30, 0), state.EndTime);
    }

    [Fact]
    public void Editor_clears_times_for_all_day_event()
    {
        var state = CalendarEventTimePolicy.CreateEditorState(
            new DateTime(2026, 9, 2, 9, 0, 0),
            new DateTime(2026, 9, 2, 10, 0, 0),
            isAllDay: true);

        Assert.True(state.IsAllDay);
        Assert.Null(state.StartTime);
        Assert.Null(state.EndTime);
    }

    [Fact]
    public void Agenda_range_contains_model_start_and_end()
    {
        string text = CalendarEventTimePolicy.FormatTimeRange(
            new DateTime(2026, 9, 2, 9, 5, 0),
            new DateTime(2026, 9, 2, 10, 45, 0),
            isAllDay: false,
            allDayText: "All Day",
            fallbackText: "stale");

        Assert.Equal("09:05 - 10:45", text);
    }
}
