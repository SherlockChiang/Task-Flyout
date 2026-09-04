using System;

namespace Task_Flyout.Services
{
    internal static class MailProviderAccountLinkPolicy
    {
        public static string GetProviderName(MailAccountKind kind)
            => kind switch
            {
                MailAccountKind.Google => "Google",
                MailAccountKind.Outlook => "Microsoft",
                _ => string.Empty
            };

        public static string ResolveProviderAccountId(
            MailAccountKind kind,
            string? providerAccountId)
        {
            if (!string.IsNullOrWhiteSpace(providerAccountId))
                return providerAccountId.Trim();

            return kind switch
            {
                MailAccountKind.Google => AccountIdentityPolicy.CreateLegacyAccountId("Google"),
                MailAccountKind.Outlook => AccountIdentityPolicy.CreateLegacyAccountId("Microsoft"),
                _ => string.Empty
            };
        }

        public static string GetPreferredAgendaDisplayName(
            MailAccountKind kind,
            string? displayName,
            string? address)
        {
            string normalizedDisplayName = displayName?.Trim() ?? string.Empty;
            string normalizedAddress = address?.Trim() ?? string.Empty;
            return kind switch
            {
                MailAccountKind.Google => !string.IsNullOrWhiteSpace(normalizedAddress)
                    ? normalizedAddress
                    : normalizedDisplayName,
                MailAccountKind.Outlook => !string.IsNullOrWhiteSpace(normalizedDisplayName)
                    ? normalizedDisplayName
                    : normalizedAddress,
                _ => string.Empty
            };
        }

        public static bool ShouldHydrateAgendaDisplayName(
            string? currentDisplayName,
            string? providerName)
            => string.IsNullOrWhiteSpace(currentDisplayName)
               || string.Equals(
                   currentDisplayName.Trim(),
                   ProviderAuthorizationLifecycle.NormalizeProviderName(providerName),
                   StringComparison.OrdinalIgnoreCase);

        public static bool MatchesExpectedMailboxAddress(
            string? expectedAddress,
            string? actualAddress)
            => string.IsNullOrWhiteSpace(expectedAddress)
               || string.Equals(
                   expectedAddress.Trim(),
                   actualAddress?.Trim(),
                   StringComparison.OrdinalIgnoreCase);

        public static bool MatchesProviderAccount(
            MailAccountKind kind,
            string? providerAccountId,
            string? providerName,
            string? accountId)
        {
            string expectedProvider = GetProviderName(kind);
            if (string.IsNullOrEmpty(expectedProvider)) return false;

            return AccountIdentityPolicy.Matches(
                expectedProvider,
                ResolveProviderAccountId(kind, providerAccountId),
                providerName,
                accountId);
        }
    }
}
