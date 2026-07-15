using System;
using System.Threading.Tasks;

namespace Task_Flyout.Services
{
    internal enum ProviderRemovalMode
    {
        FeatureOnly,
        CompleteDisconnect
    }

    internal static class ProviderAuthorizationLifecycle
    {
        public static bool HasSharedAuthorization(string? providerName)
            => string.Equals(providerName, "Google", StringComparison.OrdinalIgnoreCase)
               || string.Equals(providerName, "Microsoft", StringComparison.OrdinalIgnoreCase)
               || string.Equals(providerName, "Gmail", StringComparison.OrdinalIgnoreCase)
               || string.Equals(providerName, "Outlook", StringComparison.OrdinalIgnoreCase);

        public static string NormalizeProviderName(string? providerName)
            => providerName?.Trim().ToLowerInvariant() switch
            {
                "google" or "gmail" => "Google",
                "microsoft" or "outlook" => "Microsoft",
                _ => providerName?.Trim() ?? ""
            };

        public static async Task DisconnectCompletelyAsync(
            Func<Task> clearAuthorization,
            Func<Task> removeAgendaData,
            Func<Task> removeMailData,
            Func<Task> clearBrowserData)
        {
            await clearAuthorization();
            await removeAgendaData();
            await removeMailData();
            await clearBrowserData();
        }
    }
}
