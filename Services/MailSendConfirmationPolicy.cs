namespace Task_Flyout.Services
{
    internal static class MailSendConfirmationPolicy
    {
        public static string? NormalizeMessageId(string? messageId)
        {
            var value = messageId?.Trim().Trim('<', '>').Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        public static string? BuildGmailQuery(string? messageId)
        {
            var normalized = NormalizeMessageId(messageId);
            return normalized == null ? null : $"rfc822msgid:{normalized}";
        }
    }
}
