namespace Task_Flyout.Services
{
    public enum TrayStatus { Idle, Syncing, Finished, NeedsAttention, NewMail }
    internal sealed record TrayStatusDescriptor(string ResourceKey, string Fallback);

    internal static class TrayStatusPolicy
    {
        public static TrayStatusDescriptor Describe(TrayStatus status)
            => status switch
            {
                TrayStatus.Syncing => new("TrayStatus_Syncing", "Syncing..."),
                TrayStatus.Finished => new("TrayStatus_Finished", "Sync finished"),
                TrayStatus.NeedsAttention => new("TrayStatus_NeedsAttention", "Needs attention"),
                TrayStatus.NewMail => new("TrayStatus_NewMail", "New mail"),
                _ => new("TrayStatus_Ready", "Ready")
            };
    }
}
