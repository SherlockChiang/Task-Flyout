using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class NotificationActivationParserTests
{
    [Fact]
    public void ParseArguments_accepts_packaged_activation_prefix_and_decodes_values()
    {
        var result = NotificationActivationParser.ParseArguments("----AppNotificationActivated:action=openMail&accountId=a%40b.com&folderId=Inbox&messageId=abc%2F123");

        Assert.Equal("openMail", result["action"]);
        Assert.Equal("a@b.com", result["accountId"]);
        Assert.Equal("Inbox", result["folderId"]);
        Assert.Equal("abc/123", result["messageId"]);
    }

    [Fact]
    public void ParseArguments_accepts_html_encoded_separator()
    {
        var result = NotificationActivationParser.ParseArguments("action=copyCode&amp;codeToken=token-123");

        Assert.Equal("copyCode", result["action"]);
        Assert.Equal("token-123", result["codeToken"]);
    }

    [Fact]
    public void ParseArguments_rejects_oversized_payloads()
    {
        var result = NotificationActivationParser.ParseArguments(new string('a', 4097));

        Assert.Empty(result);
    }

    [Theory]
    [InlineData("abc-DEF_123.@/+=", true)]
    [InlineData("abc:123", false)]
    [InlineData("abc&action=openMail", false)]
    [InlineData("", false)]
    public void IsSafeIdToken_allows_only_expected_id_characters(string value, bool expected)
    {
        Assert.Equal(expected, NotificationActivationParser.IsSafeIdToken(value));
    }

    [Theory]
    [InlineData("1234", true)]
    [InlineData("12345678", true)]
    [InlineData("123", false)]
    [InlineData("123456789", false)]
    [InlineData("12a4", false)]
    public void IsVerificationCode_accepts_only_short_digit_codes(string value, bool expected)
    {
        Assert.Equal(expected, NotificationActivationParser.IsVerificationCode(value));
    }

    [Theory]
    [InlineData("action=openAgenda&token=0123456789abcdef0123456789abcdef", true)]
    [InlineData("action=snoozeAgenda&token=0123456789abcdef0123456789abcdef", true)]
    [InlineData("action=completeTask&token=0123456789abcdef0123456789abcdef", true)]
    [InlineData("action=completeTask&token=ABCDEF0123456789abcdef0123456789", false)]
    [InlineData("action=openAgenda&token=invalid", false)]
    [InlineData("action=completeTask&token=0123456789abcdef0123456789abcdef&provider=Google", false)]
    [InlineData("action=completeTask&action=openAgenda&token=0123456789abcdef0123456789abcdef", false)]
    [InlineData("prefix----AppNotificationActivated:action=openAgenda&token=0123456789abcdef0123456789abcdef", false)]
    [InlineData("action=openAgenda;token=0123456789abcdef0123456789abcdef", false)]
    [InlineData("action=openAgenda&token=%GG", false)]
    public void Agenda_actions_require_exact_strict_schema(string value, bool expected)
    {
        Assert.Equal(expected, NotificationActivationParser.TryParseAgendaAction(value, out _, out _));
    }
}
