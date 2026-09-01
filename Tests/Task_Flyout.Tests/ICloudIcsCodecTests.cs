using Ical.Net;
using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class ICloudIcsCodecTests
{
    private static readonly Uri ResourceUri = new("https://p01-caldav.icloud.com/123/calendars/work/event.ics");

    [Fact]
    public void Creates_and_parses_timed_event_without_losing_fields()
    {
        var start = new DateTime(2026, 8, 4, 9, 30, 0);
        string calendar = ICloudIcsCodec.CreateCalendar(
            "uid-1", "Design review", "Room 2", "Bring notes", start, start.AddHours(1), false, "None");

        var parsed = ICloudIcsCodec.ParseEvents(calendar, ResourceUri, start.Date, start.Date.AddDays(1));

        var calendarEvent = Assert.Single(parsed);
        Assert.Equal("Design review", calendarEvent.Title);
        Assert.Equal("Room 2", calendarEvent.Location);
        Assert.Equal("Bring notes", calendarEvent.Description);
        Assert.Equal(start, calendarEvent.Start);
        Assert.False(calendarEvent.IsAllDay);
        Assert.False(calendarEvent.IsRecurring);
        Assert.True(ICloudEventReferenceCodec.TryDecode(calendarEvent.Id, out _));
    }

    [Fact]
    public void Preserves_all_day_event_boundaries()
    {
        var start = new DateTime(2026, 8, 4);
        string calendar = ICloudIcsCodec.CreateCalendar(
            "uid-all-day", "Holiday", "", "", start, start.AddDays(1), true, "None");

        var calendarEvent = Assert.Single(
            ICloudIcsCodec.ParseEvents(calendar, ResourceUri, start.AddDays(-1), start.AddDays(2)));

        Assert.True(calendarEvent.IsAllDay);
        Assert.Equal(start, calendarEvent.Start);
        Assert.Equal(start.AddDays(1), calendarEvent.End);
    }

    [Fact]
    public void Expands_weekly_recurrence_only_inside_requested_range()
    {
        var start = new DateTime(2026, 8, 3, 10, 0, 0);
        string calendar = ICloudIcsCodec.CreateCalendar(
            "uid-weekly", "Weekly", "", "", start, start.AddHours(1), false, "Weekly");

        var parsed = ICloudIcsCodec.ParseEvents(calendar, ResourceUri, start.Date, start.Date.AddDays(15));

        Assert.Equal(3, parsed.Count);
        Assert.All(parsed, calendarEvent => Assert.True(calendarEvent.IsRecurring));
        Assert.Equal(3, parsed.Select(calendarEvent => calendarEvent.Id).Distinct().Count());
    }

    [Fact]
    public void Updates_a_single_non_recurring_resource()
    {
        var start = new DateTime(2026, 8, 4, 9, 0, 0);
        string calendar = ICloudIcsCodec.CreateCalendar("uid-update", "Old", "", "", start, start.AddHours(1), false, "None");
        var original = Assert.Single(ICloudIcsCodec.ParseEvents(calendar, ResourceUri, start.Date, start.Date.AddDays(1)));
        Assert.True(ICloudEventReferenceCodec.TryDecode(original.Id, out var reference));

        string updated = ICloudIcsCodec.UpdateCalendar(
            calendar, reference, "New", "Studio", "Updated", start.AddHours(2), start.AddHours(3), false);
        var result = Assert.Single(ICloudIcsCodec.ParseEvents(updated, ResourceUri, start.Date, start.Date.AddDays(1)));

        Assert.Equal("New", result.Title);
        Assert.Equal("Studio", result.Location);
        Assert.Equal(start.AddHours(2), result.Start);
    }

    [Fact]
    public void Deletes_only_the_selected_recurring_occurrence()
    {
        var start = new DateTime(2026, 8, 3, 10, 0, 0);
        string calendar = ICloudIcsCodec.CreateCalendar(
            "uid-delete", "Weekly", "", "", start, start.AddHours(1), false, "Weekly");
        var before = ICloudIcsCodec.ParseEvents(calendar, ResourceUri, start.Date, start.Date.AddDays(15));
        Assert.True(ICloudEventReferenceCodec.TryDecode(before[1].Id, out var selected));

        string updated = ICloudIcsCodec.DeleteOccurrence(calendar, selected, RecurringDeleteMode.Single);
        var after = ICloudIcsCodec.ParseEvents(updated, ResourceUri, start.Date, start.Date.AddDays(15));

        Assert.Equal(2, after.Count);
        Assert.DoesNotContain(after, calendarEvent => calendarEvent.Start == before[1].Start);
    }

    [Fact]
    public void Shortens_a_recurring_series_before_the_selected_occurrence()
    {
        var start = new DateTime(2026, 8, 3, 10, 0, 0);
        string calendar = ICloudIcsCodec.CreateCalendar(
            "uid-following", "Weekly", "", "", start, start.AddHours(1), false, "Weekly");
        var before = ICloudIcsCodec.ParseEvents(calendar, ResourceUri, start.Date, start.Date.AddDays(29));
        Assert.True(ICloudEventReferenceCodec.TryDecode(before[2].Id, out var selected));

        string updated = ICloudIcsCodec.DeleteOccurrence(calendar, selected, RecurringDeleteMode.ThisAndFollowing);
        var after = ICloudIcsCodec.ParseEvents(updated, ResourceUri, start.Date, start.Date.AddDays(29));

        Assert.Equal(2, after.Count);
        Assert.All(after, calendarEvent => Assert.True(calendarEvent.Start < selected.OccurrenceStart));
    }

    [Fact]
    public void Uses_recurrence_id_as_the_stable_key_for_a_moved_override()
    {
        var before = ICloudIcsCodec.ParseEvents(
            MovedRecurringCalendar,
            ResourceUri,
            new DateTime(2026, 3, 1),
            new DateTime(2026, 3, 20));
        var moved = Assert.Single(before, calendarEvent => calendarEvent.Title == "Moved");

        Assert.True(ICloudEventReferenceCodec.TryDecode(moved.Id, out var reference));
        Assert.Equal(new DateTime(2026, 3, 9, 10, 0, 0), reference.OccurrenceStart);

        string updated = ICloudIcsCodec.DeleteOccurrence(
            MovedRecurringCalendar,
            reference,
            RecurringDeleteMode.Single);
        var after = ICloudIcsCodec.ParseEvents(
            updated,
            ResourceUri,
            new DateTime(2026, 3, 1),
            new DateTime(2026, 3, 20));

        Assert.Equal(2, after.Count);
        Assert.DoesNotContain(after, calendarEvent => calendarEvent.Title == "Moved");
    }

    [Fact]
    public void Keeps_override_sequence_monotonic_across_repeated_edits()
    {
        var moved = Assert.Single(
            ICloudIcsCodec.ParseEvents(
                MovedRecurringCalendar,
                ResourceUri,
                new DateTime(2026, 3, 1),
                new DateTime(2026, 3, 20)),
            calendarEvent => calendarEvent.Title == "Moved");
        Assert.True(ICloudEventReferenceCodec.TryDecode(moved.Id, out var reference));

        string first = ICloudIcsCodec.UpdateCalendar(
            MovedRecurringCalendar,
            reference,
            "First edit",
            "",
            "",
            new DateTime(2026, 3, 10, 13, 0, 0),
            new DateTime(2026, 3, 10, 14, 0, 0),
            false);
        var firstParsed = Assert.Single(
            ICloudIcsCodec.ParseEvents(first, ResourceUri, new DateTime(2026, 3, 1), new DateTime(2026, 3, 20)),
            calendarEvent => calendarEvent.Title == "First edit");
        Assert.True(ICloudEventReferenceCodec.TryDecode(firstParsed.Id, out var firstReference));

        string second = ICloudIcsCodec.UpdateCalendar(
            first,
            firstReference,
            "Second edit",
            "",
            "",
            new DateTime(2026, 3, 10, 14, 0, 0),
            new DateTime(2026, 3, 10, 15, 0, 0),
            false);
        Calendar calendar = Calendar.Load(second)!;
        var calendarOverride = Assert.Single(calendar.Events, value => value.RecurrenceIdentifier != null);

        Assert.Equal(9, calendarOverride.Sequence);
    }

    [Fact]
    public void Shortens_non_UTC_recurrence_without_an_invalid_local_until()
    {
        var start = new DateTime(2026, 3, 2, 9, 0, 0);
        string calendar = ICloudIcsCodec.CreateCalendar(
            "uid-new-york",
            "Weekly",
            "",
            "",
            start,
            start.AddHours(1),
            false,
            "Weekly",
            "America/New_York");
        var before = ICloudIcsCodec.ParseEvents(
            calendar,
            ResourceUri,
            new DateTime(2026, 3, 1),
            new DateTime(2026, 3, 24));
        Assert.True(ICloudEventReferenceCodec.TryDecode(before[2].Id, out var selected));

        string updated = ICloudIcsCodec.DeleteOccurrence(
            calendar,
            selected,
            RecurringDeleteMode.ThisAndFollowing);
        var after = ICloudIcsCodec.ParseEvents(
            updated,
            ResourceUri,
            new DateTime(2026, 3, 1),
            new DateTime(2026, 3, 24));

        Assert.Equal(2, after.Count);
    }

    [Fact]
    public void Timed_recurrence_keeps_wall_clock_time_across_DST()
    {
        var start = new DateTime(2026, 3, 2, 9, 0, 0);
        string calendar = ICloudIcsCodec.CreateCalendar(
            "uid-dst",
            "Weekly",
            "",
            "",
            start,
            start.AddHours(1),
            false,
            "Weekly",
            "America/New_York");

        var parsed = ICloudIcsCodec.ParseEvents(
            calendar,
            ResourceUri,
            new DateTime(2026, 3, 1),
            new DateTime(2026, 3, 17));

        Assert.Contains("DTSTART;TZID=America/New_York:20260302T090000", calendar, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromHours(167), parsed[1].Start.ToUniversalTime() - parsed[0].Start.ToUniversalTime());
    }

    [Fact]
    public void Treats_RDATE_occurrences_as_recurring_and_deletes_only_one()
    {
        const string calendar = """
BEGIN:VCALENDAR
VERSION:2.0
PRODID:-//Task Flyout Tests//EN
BEGIN:VEVENT
UID:rdate-only
DTSTART:20260302T100000Z
DTEND:20260302T110000Z
RDATE:20260309T100000Z,20260316T100000Z
SUMMARY:RDate event
END:VEVENT
END:VCALENDAR
""";
        var before = ICloudIcsCodec.ParseEvents(
            calendar,
            ResourceUri,
            new DateTime(2026, 3, 1),
            new DateTime(2026, 3, 20));

        Assert.Equal(3, before.Count);
        Assert.All(before, calendarEvent => Assert.True(calendarEvent.IsRecurring));
        Assert.True(ICloudEventReferenceCodec.TryDecode(before[1].Id, out var selected));

        string updated = ICloudIcsCodec.DeleteOccurrence(calendar, selected, RecurringDeleteMode.Single);
        var after = ICloudIcsCodec.ParseEvents(
            updated,
            ResourceUri,
            new DateTime(2026, 3, 1),
            new DateTime(2026, 3, 20));

        Assert.Equal(2, after.Count);
    }

    [Fact]
    public void Rejects_multiple_UIDs_in_one_CalDav_resource()
    {
        const string calendar = """
BEGIN:VCALENDAR
VERSION:2.0
BEGIN:VEVENT
UID:first
DTSTART:20260302T100000Z
DTEND:20260302T110000Z
END:VEVENT
BEGIN:VEVENT
UID:second
DTSTART:20260303T100000Z
DTEND:20260303T110000Z
END:VEVENT
END:VCALENDAR
""";

        Assert.Throws<InvalidDataException>(() => ICloudIcsCodec.ParseEvents(
            calendar,
            ResourceUri,
            new DateTime(2026, 3, 1),
            new DateTime(2026, 3, 5)));
    }

    [Fact]
    public void Rejects_UIDs_too_large_for_a_stable_event_reference()
    {
        string calendar = $"""
BEGIN:VCALENDAR
VERSION:2.0
BEGIN:VEVENT
UID:{new string('u', ICloudEventReferenceCodec.MaxUidLength + 1)}
DTSTART:20260302T100000Z
DTEND:20260302T110000Z
END:VEVENT
END:VCALENDAR
""";

        Assert.Throws<InvalidDataException>(() => ICloudIcsCodec.ParseEvents(
            calendar,
            ResourceUri,
            new DateTime(2026, 3, 1),
            new DateTime(2026, 3, 5)));
    }

    [Fact]
    public void Normalizes_same_day_all_day_end_to_one_full_day()
    {
        var start = new DateTime(2026, 8, 4);
        string calendar = ICloudIcsCodec.CreateCalendar(
            "uid-all-day-normalized",
            "Holiday",
            "",
            "",
            start,
            start.AddHours(23),
            true,
            "None");

        var calendarEvent = Assert.Single(
            ICloudIcsCodec.ParseEvents(calendar, ResourceUri, start.AddDays(-1), start.AddDays(2)));

        Assert.Equal(start.AddDays(1), calendarEvent.End);
    }

    private const string MovedRecurringCalendar = """
BEGIN:VCALENDAR
VERSION:2.0
PRODID:-//Task Flyout Tests//EN
BEGIN:VEVENT
UID:moved-series
DTSTART:20260302T100000Z
DTEND:20260302T110000Z
RRULE:FREQ=WEEKLY;COUNT=3
SUMMARY:Master
SEQUENCE:0
END:VEVENT
BEGIN:VEVENT
UID:moved-series
RECURRENCE-ID:20260309T100000Z
DTSTART:20260310T120000Z
DTEND:20260310T130000Z
SUMMARY:Moved
SEQUENCE:7
END:VEVENT
END:VCALENDAR
""";
}
