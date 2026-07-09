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
        var result = NotificationActivationParser.ParseArguments("action=copyCode&amp;code=123456");

        Assert.Equal("copyCode", result["action"]);
        Assert.Equal("123456", result["code"]);
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
}
