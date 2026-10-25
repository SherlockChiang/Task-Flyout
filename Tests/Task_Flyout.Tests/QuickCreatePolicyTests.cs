using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class QuickCreatePolicyTests
{
    private static readonly DateTime Today = new(2026, 10, 25);

    [Theory]
    [InlineData("明天 09:30 评审方案", 1, 9, 30, "评审方案")]
    [InlineData("後天9點15分開會", 2, 9, 15, "開會")]
    [InlineData("今天9点开会", 0, 9, 0, "开会")]
    [InlineData("明天 9 点和客户开会", 1, 9, 0, "和客户开会")]
    [InlineData("Tomorrow 9 am review", 1, 9, 0, "review")]
    [InlineData("tomorrow 12:05 pm lunch", 1, 12, 5, "lunch")]
    [InlineData("today 12 am deploy", 0, 0, 0, "deploy")]
    [InlineData("2026-10-25 23:59 release", 0, 23, 59, "release")]
    public void Recognizes_explicit_date_and_time(string input, int days, int hour, int minute, string title)
    {
        var parsed = Assert.IsType<QuickCreateParsed>(QuickCreatePolicy.Parse(input, Today));
        Assert.Equal(Today.AddDays(days), parsed.Date);
        Assert.Equal(new TimeSpan(hour, minute, 0), parsed.Time);
        Assert.Equal(title, parsed.Title);
    }

    [Theory]
    [InlineData("明天 买牛奶", 1, "买牛奶")]
    [InlineData("today read notes", 0, "read notes")]
    [InlineData("后天交报告", 2, "交报告")]
    public void Recognizes_date_only_without_inventing_a_time(string input, int days, string title)
    {
        var parsed = Assert.IsType<QuickCreateParsed>(QuickCreatePolicy.Parse(input, Today));
        Assert.Equal(Today.AddDays(days), parsed.Date);
        Assert.Null(parsed.Time);
        Assert.Equal(title, parsed.Title);
    }

    [Theory]
    [InlineData("meet tomorrow")]
    [InlineData("tomorrow")]
    [InlineData("tomorrow 09:00")]
    [InlineData("tomorrow 24:00 meeting")]
    [InlineData("tomorrow 09:61 meeting")]
    [InlineData("tomorrow 13 pm meeting")]
    [InlineData("tomorrow 00 am meeting")]
    [InlineData("tomorrow 09:300 meeting")]
    [InlineData("tomorrow 09:30:45 meeting")]
    [InlineData("明天9点123分开会")]
    [InlineData("明天9点99分开会")]
    [InlineData("2026-02-29 meeting")]
    [InlineData("tomorrowness meeting")]
    [InlineData("明天 09:00   ")]
    [InlineData("明天 ٩:٣٠ 开会")]
    [InlineData("")]
    public void Rejects_partial_or_invalid_phrases(string input)
        => Assert.Null(QuickCreatePolicy.Parse(input, Today));

    [Fact]
    public void Parsing_bounds_input_and_date_arithmetic()
    {
        Assert.Null(QuickCreatePolicy.Parse(new string('a', 513), Today));
        Assert.Null(QuickCreatePolicy.Parse("tomorrow meeting", DateTime.MaxValue));
        Assert.Null(QuickCreatePolicy.Parse("后天开会", DateTime.MaxValue.AddDays(-1)));
    }

    [Fact]
    public void Duration_wraps_midnight_and_rejects_unknown_defaults()
    {
        Assert.Equal(60, QuickCreatePolicy.Duration(-1));
        Assert.Equal(30, QuickCreatePolicy.Duration(30));
        var end = QuickCreatePolicy.EndTime(new TimeSpan(23, 30, 0), 90);
        var window = SyncEventTimePolicy.Create(Today, new TimeSpan(23, 30, 0), end);
        Assert.Equal(Today.AddDays(1).AddHours(1), window.End);
        Assert.Equal(TimeSpan.FromMinutes(90), window.End - window.Start);
    }

    [Theory]
    [InlineData("Google", true, true, true, true, true)]
    [InlineData("Google", false, true, false, false, false)]
    [InlineData("Microsoft", true, true, true, true, true)]
    [InlineData("Microsoft", false, true, false, true, true)]
    [InlineData("iCloud", true, true, true, true, true)]
    [InlineData("iCloud", false, false, false, false, false)]
    [InlineData("Unknown", true, false, false, false, false)]
    public void Options_match_provider_capabilities(string provider, bool isEvent, bool supported, bool time, bool reminder, bool recurrence)
        => Assert.Equal(new QuickCreateCapabilities(supported, time, reminder, recurrence), QuickCreatePolicy.Capabilities(provider, isEvent));

    [Fact]
    public void Validates_title_notes_reminders_and_recurrence()
    {
        QuickCreatePolicy.Validate("Google", false, "Task", new QuickCreateOptions("Notes"));
        QuickCreatePolicy.Validate("Microsoft", false, "Task", new QuickCreateOptions("Notes", 15, EventRecurrenceKind.Weekly));
        Assert.Throws<ArgumentException>(() => QuickCreatePolicy.Validate("Google", true, " ", new()));
        Assert.Throws<ArgumentException>(() => QuickCreatePolicy.Validate("Google", true, new string('a', 513), new()));
        Assert.Throws<ArgumentException>(() => QuickCreatePolicy.Validate("Google", true, "Event", new(new string('a', 8001))));
        Assert.Throws<ArgumentOutOfRangeException>(() => QuickCreatePolicy.Validate("Google", true, "Event", new(ReminderMinutes: -2)));
        Assert.Throws<NotSupportedException>(() => QuickCreatePolicy.Validate("Google", false, "Task", new(ReminderMinutes: 15)));
        Assert.Throws<NotSupportedException>(() => QuickCreatePolicy.Validate("Google", false, "Task", new(Recurrence: EventRecurrenceKind.Weekly)));
        Assert.Throws<NotSupportedException>(() => QuickCreatePolicy.Validate("iCloud", false, "Task", new()));
        Assert.Throws<NotSupportedException>(() => QuickCreatePolicy.Validate("Microsoft", true, "Event", new(Recurrence: (EventRecurrenceKind)99)));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/secret.txt")]
    [InlineData("https://user:password@example.com")]
    [InlineData("/relative/path")]
    public void Source_drafts_exclude_non_web_links_and_credentials(string link)
        => Assert.Equal("Source", QuickCreatePolicy.FromSource("Title", "Source", link).Notes);

    [Fact]
    public void Source_drafts_preserve_context_account_and_length_limits()
    {
        var seed = QuickCreatePolicy.FromSource("Title", "Sender", "https://example.com/article", "Microsoft|account");
        Assert.Equal("Sender" + Environment.NewLine + "https://example.com/article", seed.Notes);
        Assert.Equal("Microsoft|account", seed.PreferredProviderKey);
        var longSeed = QuickCreatePolicy.FromSource(new string('t', 600), new string('n', 9000), null);
        Assert.Equal(512, longSeed.Title.Length);
        Assert.Equal(8000, longSeed.Notes.Length);
    }

    [Fact]
    public void Recurrence_uses_the_start_weekday_day_and_month()
    {
        Assert.Equal(new QuickCreateRecurrence(null, null, null), QuickCreatePolicy.RecurrenceFields(EventRecurrenceKind.Daily, Today));
        Assert.Equal(new QuickCreateRecurrence(DayOfWeek.Sunday, null, null), QuickCreatePolicy.RecurrenceFields(EventRecurrenceKind.Weekly, Today));
        Assert.Equal(new QuickCreateRecurrence(null, 25, null), QuickCreatePolicy.RecurrenceFields(EventRecurrenceKind.Monthly, Today));
        Assert.Equal(new QuickCreateRecurrence(null, 25, 10), QuickCreatePolicy.RecurrenceFields(EventRecurrenceKind.Yearly, Today));
        Assert.Throws<ArgumentOutOfRangeException>(() => QuickCreatePolicy.RecurrenceFields(EventRecurrenceKind.None, Today));
    }

    [Theory]
    [InlineData("Pacific Standard Time", "America/Los_Angeles")]
    [InlineData("Asia/Shanghai", "Asia/Shanghai")]
    [InlineData("UTC", "Etc/UTC")]
    public void Calendar_time_zone_preserves_daylight_saving_rules(string input, string expected)
        => Assert.Equal(expected, QuickCreatePolicy.CalendarTimeZoneId(input));
}
