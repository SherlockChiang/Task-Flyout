namespace Task_Flyout.Services
{
    internal readonly record struct SyncProviderCapabilities(bool SupportsEvents, bool SupportsTasks);

    internal static class SyncProviderCapabilityPolicy
    {
        public static SyncProviderCapabilities ForProvider(string? providerName)
            => ProviderAuthorizationLifecycle.NormalizeProviderName(providerName) switch
            {
                "Google" or "Microsoft" => new(true, true),
                "iCloud" => new(true, false),
                _ => new(false, false)
            };
    }
}
