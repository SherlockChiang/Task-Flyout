using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class OutlookImapPolicyTests
{
    [Fact]
    public void Existing_records_default_to_password_authentication()
    {
        Assert.Equal(MailAuthenticationMode.Password, default(MailAuthenticationMode));
        Assert.Equal(0, (int)MailAuthenticationMode.Password);
    }

    [Theory]
    [InlineData(OutlookMailboxType.Personal, "consumers", "smtp-mail.outlook.com")]
    [InlineData(OutlookMailboxType.WorkOrSchool, "organizations", "smtp.office365.com")]
    public void Account_type_selects_the_authority_and_smtp_host(
        OutlookMailboxType type, string tenant, string smtp)
    {
        Assert.Equal(tenant, OutlookImapPolicy.GetAuthorityTenant(type));
        Assert.Equal(smtp, OutlookImapPolicy.GetSmtpHost(type));
        OutlookImapPolicy.ValidateEndpoints(type, "outlook.office365.com", 993, true, smtp, 587, false);
    }

    [Fact]
    public void Unknown_account_type_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OutlookImapPolicy.GetAuthorityTenant((OutlookMailboxType)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => OutlookImapPolicy.GetSmtpHost((OutlookMailboxType)42));
    }

    [Theory]
    [InlineData(false, "https://outlook.office.com/IMAP.AccessAsUser.All")]
    [InlineData(true, "https://outlook.office.com/SMTP.Send")]
    public void Each_operation_requests_only_its_exchange_scope(bool sending, string expected)
        => Assert.Equal(new[] { expected }, OutlookImapPolicy.GetScopes(sending));

    [Fact]
    public void Caller_cannot_mutate_future_scope_requests()
    {
        var scopes = OutlookImapPolicy.GetScopes(false);
        scopes[0] = "Mail.Send";
        Assert.Equal("https://outlook.office.com/IMAP.AccessAsUser.All", OutlookImapPolicy.GetScopes(false)[0]);
    }

    [Theory]
    [InlineData("../other")]
    [InlineData("..\\other")]
    [InlineData("C:\\tokens")]
    [InlineData("00000000-0000-0000-0000-000000000001")]
    [InlineData("not-an-id")]
    [InlineData("")]
    public void Cache_path_rejects_noncanonical_ids(string id)
        => Assert.Throws<ArgumentException>(() => OutlookImapPolicy.GetCacheFileName(id));

    [Fact]
    public void Cache_names_are_account_scoped_and_case_normalized()
    {
        var one = Guid.NewGuid().ToString("N");
        var two = Guid.NewGuid().ToString("N");
        Assert.NotEqual(OutlookImapPolicy.GetCacheFileName(one), OutlookImapPolicy.GetCacheFileName(two));
        Assert.Equal(OutlookImapPolicy.GetCacheFileName(one), OutlookImapPolicy.GetCacheFileName(one.ToUpperInvariant()));
        Assert.DoesNotContain("TaskFlyout_MSAL_Cache", OutlookImapPolicy.GetCacheFileName(one));
        Assert.Equal($"outlook-imap-{one}.msalcache", OutlookImapPolicy.GetCacheFileName(one));
    }

    [Theory]
    [InlineData("USER@outlook.com", " user@outlook.com ", true)]
    [InlineData("user@outlook.com", "other@outlook.com", false)]
    [InlineData("user@outlook.com", null, false)]
    [InlineData("", "", false)]
    public void Mailbox_address_matching_does_not_accept_an_empty_identity(string expected, string? actual, bool matches)
        => Assert.Equal(matches, OutlookImapPolicy.MatchesAddress(expected, actual));

    [Theory]
    [InlineData("", "home-1", "user@outlook.com", true)]
    [InlineData("", "home-1", "other@outlook.com", false)]
    [InlineData("home-1", "home-1", "renamed@outlook.com", true)]
    [InlineData("home-1", "home-2", "user@outlook.com", false)]
    [InlineData("home-1", "", "user@outlook.com", false)]
    public void Known_account_is_bound_to_home_identity_not_display_address(
        string expectedHome, string actualHome, string actualAddress, bool matches)
        => Assert.Equal(matches, OutlookImapPolicy.MatchesIdentity(expectedHome, actualHome, "user@outlook.com", actualAddress));

    [Theory]
    [InlineData("attacker.example", 993, true, "smtp-mail.outlook.com", 587, false)]
    [InlineData("outlook.office365.com", 143, true, "smtp-mail.outlook.com", 587, false)]
    [InlineData("outlook.office365.com", 993, false, "smtp-mail.outlook.com", 587, false)]
    [InlineData("outlook.office365.com", 993, true, "attacker.example", 587, false)]
    [InlineData("outlook.office365.com", 993, true, "smtp-mail.outlook.com", 25, false)]
    [InlineData("outlook.office365.com", 993, true, "smtp-mail.outlook.com", 587, true)]
    [InlineData("outlook.office365.com.attacker.example", 993, true, "smtp-mail.outlook.com", 587, false)]
    [InlineData("outlook.office365.com", 993, true, "smtp.office365.com", 587, false)]
    public void OAuth_tokens_cannot_be_sent_to_custom_or_downgraded_endpoints(
        string imap, int imapPort, bool imapSsl, string smtp, int smtpPort, bool smtpSsl)
        => Assert.Throws<InvalidOperationException>(() => OutlookImapPolicy.ValidateEndpoints(
            OutlookMailboxType.Personal, imap, imapPort, imapSsl, smtp, smtpPort, smtpSsl));
}
