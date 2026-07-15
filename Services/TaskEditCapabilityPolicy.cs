namespace Task_Flyout.Services
{
    internal sealed record TaskEditCapabilities(
        bool SupportsTitle,
        bool SupportsDueDate,
        bool SupportsDueTime,
        bool SupportsNotes,
        bool SupportsRecurrence,
        bool SupportsProviderMove,
        bool SupportsListMove,
        bool SupportsCompletion,
        bool SupportsDeletion);

    internal static class TaskEditCapabilityPolicy
    {
        public static TaskEditCapabilities ForProvider(string? providerName)
            => providerName?.Trim().ToLowerInvariant() switch
            {
                "google" or "microsoft" => new(true, true, false, true, false, false, false, true, true),
                _ => new(false, false, false, false, false, false, false, false, false)
            };
    }
}
