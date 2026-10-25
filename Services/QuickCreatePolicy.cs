using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Task_Flyout.Services;

public sealed record QuickCreateOptions(string Description = "", int? ReminderMinutes = null,
    EventRecurrenceKind Recurrence = EventRecurrenceKind.None, string? RequestId = null);

internal sealed record QuickCreateSeed(string Title, string Notes, string? PreferredProviderKey = null);
internal sealed record QuickCreateParsed(string Title, DateTime Date, TimeSpan? Time);
internal readonly record struct QuickCreateCapabilities(bool Supported, bool Time, bool Reminder, bool Recurrence);
internal readonly record struct QuickCreateRecurrence(DayOfWeek? DayOfWeek, int? DayOfMonth, int? Month);

internal static class QuickCreatePolicy
{
    public static QuickCreateCapabilities Capabilities(string? provider, bool isEvent) =>
        ProviderAuthorizationLifecycle.NormalizeProviderName(provider) switch
        {
            "Google" => isEvent ? new(true, true, true, true) : new(true, false, false, false),
            "Microsoft" => new(true, isEvent, true, true),
            "iCloud" => isEvent ? new(true, true, true, true) : default,
            _ => default
        };

    public static int Duration(int minutes) => minutes is 15 or 30 or 60 or 90 or 120 ? minutes : 60;
    public static TimeSpan EndTime(TimeSpan start, int minutes) => TimeSpan.FromMinutes((start.TotalMinutes + Duration(minutes)) % 1440);

    public static QuickCreateRecurrence RecurrenceFields(EventRecurrenceKind recurrence, DateTime date) => recurrence switch
    {
        EventRecurrenceKind.Daily => default,
        EventRecurrenceKind.Weekly => new(date.DayOfWeek, null, null),
        EventRecurrenceKind.Monthly => new(null, date.Day, null),
        EventRecurrenceKind.Yearly => new(null, date.Day, date.Month),
        _ => throw new ArgumentOutOfRangeException(nameof(recurrence))
    };

    public static string CalendarTimeZoneId(string localId)
        => TimeZoneInfo.TryConvertWindowsIdToIanaId(localId, out string? ianaId) ? ianaId : localId;

    public static void Validate(string provider, bool isEvent, string title, QuickCreateOptions options)
    {
        var capabilities = Capabilities(provider, isEvent);
        if (!capabilities.Supported) throw new NotSupportedException("This account cannot create the selected item type.");
        if (string.IsNullOrWhiteSpace(title) || title.Length > 512) throw new ArgumentException("A title of 1 to 512 characters is required.");
        if (options.Description.Length > 8000) throw new ArgumentException("Notes are too long.");
        if (options.ReminderMinutes is < -1 or > 10080) throw new ArgumentOutOfRangeException(nameof(options));
        if (options.ReminderMinutes.HasValue && !capabilities.Reminder) throw new NotSupportedException("This account does not support reminders.");
        if (!Enum.IsDefined(options.Recurrence) || (!capabilities.Recurrence && options.Recurrence != EventRecurrenceKind.None))
            throw new NotSupportedException("This account does not support the selected recurrence.");
    }

    public static QuickCreateSeed FromSource(string title, string source, string? link, string? preferredProviderKey = null)
    {
        string notes = source;
        if (Uri.TryCreate(link, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http") && string.IsNullOrEmpty(uri.UserInfo))
            notes += Environment.NewLine + uri.AbsoluteUri;
        return new(title.Length > 512 ? title[..512] : title, notes.Length > 8000 ? notes[..8000] : notes, preferredProviderKey);
    }

    // Only an explicit leading date phrase is consumed. Unsupported text stays intact.
    public static QuickCreateParsed? Parse(string input, DateTime today)
    {
        if (input.Length > 512) return null;
        var match = Regex.Match(input.Trim(), @"^(?<day>今天|明天|后天|後天|today\b|tomorrow\b|[0-9]{4}-[0-9]{2}-[0-9]{2}(?!\d))\s*(?<rest>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
        if (!match.Success) return null;
        string day = match.Groups["day"].Value.ToLowerInvariant();
        DateTime date;
        if (day is "今天" or "today") date = today.Date;
        else if (day is "明天" or "tomorrow")
        {
            if (today.Date > DateTime.MaxValue.Date.AddDays(-1)) return null;
            date = today.Date.AddDays(1);
        }
        else if (day is "后天" or "後天")
        {
            if (today.Date > DateTime.MaxValue.Date.AddDays(-2)) return null;
            date = today.Date.AddDays(2);
        }
        else if (!DateTime.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return null;
        TimeSpan? time = null;
        string title = match.Groups["rest"].Value.Trim();
        var clock = Regex.Match(title, @"^(?<hour>[0-9]{1,2})\s*(?::(?<minute>[0-9]{2})(?!\d)(?:\s*(?<period>am\b|pm\b))?|[点點](?:(?<minute>[0-9]{1,2})(?!\d)分?)?|(?<period>am\b|pm\b))\s*(?<title>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
        if (clock.Success)
        {
            int hour = int.Parse(clock.Groups["hour"].Value, CultureInfo.InvariantCulture);
            int minute = clock.Groups["minute"].Success ? int.Parse(clock.Groups["minute"].Value, CultureInfo.InvariantCulture) : 0;
            string period = clock.Groups["period"].Value.ToLowerInvariant();
            if (minute > 59 || hour > 23 || (period.Length > 0 && hour is < 1 or > 12)) return null;
            if (period.Length > 0) hour = hour % 12 + (period == "pm" ? 12 : 0);
            time = new TimeSpan(hour, minute, 0);
            title = clock.Groups["title"].Value.Trim();
        }
        // A malformed clock must not silently become part of a date-only title.
        if (title.Length == 0 || char.IsDigit(title[0]) || title[0] == ':' || title[0] == '分') return null;
        return new(title, date.Date, time);
    }
}
