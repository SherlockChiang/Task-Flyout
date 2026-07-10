using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class MailPaginationPolicyTests
{
    [Theory]
    [InlineData("https://graph.microsoft.com/v1.0/me/messages?$skiptoken=opaque", true)]
    [InlineData("http://graph.microsoft.com/v1.0/me/messages", false)]
    [InlineData("https://graph.microsoft.com.evil.example/messages", false)]
    [InlineData("https://evil.example/messages", false)]
    [InlineData("not-a-url", false)]
    public void Graph_next_link_requires_https_official_host(string value, bool expected)
    {
        Assert.Equal(expected, MailPaginationPolicy.IsAllowedGraphNextLink(value));
    }

    [Theory]
    [InlineData(42u, 42u, 100u, true)]
    [InlineData(41u, 42u, 100u, false)]
    [InlineData(42u, 42u, 1u, false)]
    public void Imap_cursor_requires_matching_uid_validity_and_older_uid(uint storedValidity, uint currentValidity, uint beforeUid, bool expected)
    {
        Assert.Equal(expected, MailPaginationPolicy.IsValidImapCursor(storedValidity, currentValidity, beforeUid));
    }
}
