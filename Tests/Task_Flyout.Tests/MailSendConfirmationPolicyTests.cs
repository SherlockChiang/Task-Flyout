using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class MailSendConfirmationPolicyTests
{
    [Theory]
    [InlineData("<message@example.com>", "message@example.com")]
    [InlineData("  <message@example.com>  ", "message@example.com")]
    [InlineData("message@example.com", "message@example.com")]
    [InlineData("", null)]
    [InlineData("<>", null)]
    [InlineData(null, null)]
    public void Message_id_is_normalized_for_provider_queries(string? value, string? expected)
    {
        Assert.Equal(expected, MailSendConfirmationPolicy.NormalizeMessageId(value));
    }

    [Fact]
    public void Gmail_query_uses_rfc822_message_id_operator()
    {
        Assert.Equal("rfc822msgid:message@example.com", MailSendConfirmationPolicy.BuildGmailQuery("<message@example.com>"));
    }
}
