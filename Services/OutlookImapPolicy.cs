using System;

namespace Task_Flyout.Services
{
    public enum MailAuthenticationMode
    {
        Password = 0,
        MicrosoftOAuth2 = 1
    }

    public enum OutlookMailboxType
    {
        Personal = 0,
        WorkOrSchool = 1
    }

    internal static class OutlookImapPolicy
    {
        public const string ImapHost = "outlook.office365.com";
        public const int ImapPort = 993;
        public const int SmtpPort = 587;

        public static string GetSmtpHost(OutlookMailboxType type) => type switch
        {
            OutlookMailboxType.Personal => "smtp-mail.outlook.com",
            OutlookMailboxType.WorkOrSchool => "smtp.office365.com",
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

        public static string GetAuthorityTenant(OutlookMailboxType type) => type switch
        {
            OutlookMailboxType.Personal => "consumers",
            OutlookMailboxType.WorkOrSchool => "organizations",
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

        // These are Exchange delegated scopes, not Microsoft Graph scopes.
        // Request SMTP only when the user explicitly sends a message.
        public static string[] GetScopes(bool sending) => sending
            ? new[] { "https://outlook.office.com/SMTP.Send" }
            : new[] { "https://outlook.office.com/IMAP.AccessAsUser.All" };

        public static string GetCacheFileName(string accountId)
        {
            if (!Guid.TryParseExact(accountId, "N", out var id))
                throw new ArgumentException("A canonical mail account ID is required.", nameof(accountId));
            return $"outlook-imap-{id:N}.msalcache";
        }

        public static bool MatchesAddress(string? expected, string? actual)
            => !string.IsNullOrWhiteSpace(expected) &&
               string.Equals(expected.Trim(), actual?.Trim(), StringComparison.OrdinalIgnoreCase);

        public static bool MatchesIdentity(string? expectedHomeAccountId, string? actualHomeAccountId,
            string expectedAddress, string? actualAddress)
            => !string.IsNullOrWhiteSpace(actualHomeAccountId) &&
               (string.IsNullOrWhiteSpace(expectedHomeAccountId)
                   ? MatchesAddress(expectedAddress, actualAddress)
                   : string.Equals(expectedHomeAccountId, actualHomeAccountId, StringComparison.OrdinalIgnoreCase));

        public static void ValidateEndpoints(OutlookMailboxType type, string imapHost, int imapPort,
            bool imapSsl, string smtpHost, int smtpPort, bool smtpSsl)
        {
            // Never release an Exchange access token to user-configurable servers
            // or a downgraded transport, even if stored account data is changed.
            if (!string.Equals(imapHost, ImapHost, StringComparison.OrdinalIgnoreCase) ||
                imapPort != ImapPort || !imapSsl ||
                !string.Equals(smtpHost, GetSmtpHost(type), StringComparison.OrdinalIgnoreCase) ||
                smtpPort != SmtpPort || smtpSsl)
                throw new InvalidOperationException("Outlook OAuth requires its standard IMAP TLS and SMTP STARTTLS endpoints.");
        }
    }
}
