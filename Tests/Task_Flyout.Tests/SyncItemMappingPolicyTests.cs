using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class SyncItemMappingPolicyTests
{
    [Fact]
    public void MapEvent_normalizes_null_strings_and_formats_timed_event()
    {
        var mapped = SyncItemMappingPolicy.MapEvent(
            id: null,
            title: null,
            location: null,
            description: null,
            provider: "Google",
            calendarId: null,
            calendarName: null,
            date: new DateTime(2026, 7, 9, 8, 5, 0),
            startDateTime: new DateTime(2026, 7, 9, 8, 5, 0),
            endDateTime: new DateTime(2026, 7, 9, 9, 0, 0),
            isAllDay: false,
            allDayText: "All Day",
            recurringEventId: null,
            hasRecurrence: false,
            recurrenceKind: null);

        Assert.Equal("", mapped.Id);
        Assert.Equal("", mapped.Title);
        Assert.Equal("08:05", mapped.Subtitle);
        Assert.Equal("", mapped.Location);
        Assert.Equal("", mapped.Description);
        Assert.Equal("Google", mapped.Provider);
        Assert.Equal("", mapped.CalendarId);
        Assert.Equal("", mapped.CalendarName);
        Assert.Equal("2026-07-09", mapped.DateKey);
        Assert.False(mapped.IsRecurring);
        Assert.Equal("", mapped.RecurringEventId);
        Assert.Equal("None", mapped.RecurrenceKind);
    }

    [Fact]
    public void MapEvent_uses_all_day_subtitle_and_marks_recurrence_from_master_id()
    {
        var mapped = SyncItemMappingPolicy.MapEvent(
            id: "event-1",
            title: "Planning",
            location: "Room 1",
            description: "Discuss roadmap",
            provider: "Microsoft",
            calendarId: "calendar-1",
            calendarName: "Work",
            date: new DateTime(2026, 7, 9),
            startDateTime: null,
            endDateTime: null,
            isAllDay: true,
            allDayText: "All Day",
            recurringEventId: "master-1",
            hasRecurrence: false,
            recurrenceKind: "Weekly");

        Assert.Equal("event-1", mapped.Id);
        Assert.Equal("Planning", mapped.Title);
        Assert.Equal("All Day", mapped.Subtitle);
        Assert.Equal("Room 1", mapped.Location);
        Assert.Equal("Discuss roadmap", mapped.Description);
        Assert.Equal("Microsoft", mapped.Provider);
        Assert.Equal("calendar-1", mapped.CalendarId);
        Assert.Equal("Work", mapped.CalendarName);
        Assert.Equal("2026-07-09", mapped.DateKey);
        Assert.True(mapped.IsRecurring);
        Assert.Equal("master-1", mapped.RecurringEventId);
        Assert.Equal("Weekly", mapped.RecurrenceKind);
    }

    [Fact]
    public void MapEvent_marks_recurrence_from_recurrence_payload()
    {
        var mapped = SyncItemMappingPolicy.MapEvent(
            id: "event-1",
            title: "Planning",
            location: "Room 1",
            description: "Discuss roadmap",
            provider: "Google",
            calendarId: "calendar-1",
            calendarName: "Work",
            date: new DateTime(2026, 7, 9, 11, 30, 0),
            startDateTime: new DateTime(2026, 7, 9, 11, 30, 0),
            endDateTime: new DateTime(2026, 7, 9, 12, 0, 0),
            isAllDay: false,
            allDayText: "All Day",
            recurringEventId: "",
            hasRecurrence: true,
            recurrenceKind: "Daily");

        Assert.True(mapped.IsRecurring);
        Assert.Equal("", mapped.RecurringEventId);
        Assert.Equal("Daily", mapped.RecurrenceKind);
    }
}
