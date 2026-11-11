using System;

namespace Task_Flyout.Services
{
    internal static class GoogleAuthorizationNamespacePolicy
    {
        public static bool OwnsLegacyAuthorization(string? accountId)
            => string.Equals(
                AccountIdentityPolicy.ResolveAccountId("Google", accountId),
                AccountIdentityPolicy.CreateLegacyAccountId("Google"),
                StringComparison.OrdinalIgnoreCase);
    }
}
