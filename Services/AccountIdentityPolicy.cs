using System;

namespace Task_Flyout.Services
{
    internal static class AccountIdentityPolicy
    {
        private const string RouteSeparator = "|";

        public static string CreateLegacyAccountId(string? providerName)
        {
            var provider = ProviderAuthorizationLifecycle.NormalizeProviderName(providerName);
            if (string.IsNullOrWhiteSpace(provider)) provider = "Local";
            return $"legacy-{provider.ToLowerInvariant()}";
        }

        public static string CreateAccountId()
            => Guid.NewGuid().ToString("N");

        public static string ResolveAccountId(string? providerName, string? accountId)
            => string.IsNullOrWhiteSpace(accountId)
                ? CreateLegacyAccountId(providerName)
                : accountId.Trim();

        public static string CreateProviderKey(string? providerName, string? accountId)
        {
            var provider = ProviderAuthorizationLifecycle.NormalizeProviderName(providerName);
            return $"{provider.ToLowerInvariant()}{RouteSeparator}{ResolveAccountId(provider, accountId)}";
        }

        public static bool Matches(
            string? leftProvider,
            string? leftAccountId,
            string? rightProvider,
            string? rightAccountId)
            => string.Equals(
                CreateProviderKey(leftProvider, leftAccountId),
                CreateProviderKey(rightProvider, rightAccountId),
                StringComparison.OrdinalIgnoreCase);
    }
}
