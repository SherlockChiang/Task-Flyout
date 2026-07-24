namespace Task_Flyout.Services
{
    internal readonly record struct MailMutationCapabilities(
        bool SetReadState,
        bool SetFlagged,
        bool Archive,
        bool Move,
        bool Trash,
        bool PermanentDelete);

    internal static class MailMutationCapabilityPolicy
    {
        public static MailMutationCapabilities For(MailAccountKind providerKind) => providerKind switch
        {
            MailAccountKind.Outlook => new(true, true, true, true, true, false),
            MailAccountKind.Google => new(true, true, false, false, false, false),
            MailAccountKind.Imap => new(true, true, false, false, false, false),
            _ => new(false, false, false, false, false, false)
        };

        public static bool Supports(MailAccountKind providerKind, MailMutationKind kind)
        {
            var capabilities = For(providerKind);
            return kind switch
            {
                MailMutationKind.SetReadState => capabilities.SetReadState,
                MailMutationKind.SetFlagged => capabilities.SetFlagged,
                _ => false
            };
        }
    }
}
