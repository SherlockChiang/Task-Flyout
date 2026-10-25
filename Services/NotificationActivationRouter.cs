namespace Task_Flyout.Services
{
    internal enum NotificationActivationKind { Ignore, OpenMain, OpenMail, AgendaAction, MailAction, CopyCode }

    internal sealed record NotificationActivationRoute(
        NotificationActivationKind Kind,
        string Action = "", string Token = "",
        string AccountId = "", string FolderId = "", string MessageId = "");

    internal static class NotificationActivationRouter
    {
        public static NotificationActivationRoute Parse(string? argument, bool sdkEncoded = false)
        {
            if (string.IsNullOrWhiteSpace(argument)) return new(NotificationActivationKind.OpenMain);
            var args = NotificationActivationParser.ParseArguments(argument, sdkEncoded);
            if (!args.TryGetValue("action", out var action)) return new(NotificationActivationKind.Ignore);

            if (action == "openMail" && args.Count == 4 &&
                args.TryGetValue("accountId", out var accountId) &&
                args.TryGetValue("folderId", out var folderId) &&
                args.TryGetValue("messageId", out var messageId) &&
                NotificationActivationParser.IsSafeIdToken(accountId) &&
                NotificationActivationParser.IsSafeIdToken(folderId) &&
                NotificationActivationParser.IsSafeIdToken(messageId))
                return new(NotificationActivationKind.OpenMail, AccountId: accountId, FolderId: folderId, MessageId: messageId);

            if (action is "openAgenda" or "snoozeAgenda" or "completeTask" && args.Count == 2 &&
                args.TryGetValue("token", out var token) && NotificationActivationParser.IsOpaqueToken(token))
                return new(NotificationActivationKind.AgendaAction, action, token);

            if (action == "copyCode" && args.Count == 2 && args.TryGetValue("codeToken", out var codeToken) &&
                NotificationActivationParser.IsOpaqueToken(codeToken))
                return new(NotificationActivationKind.CopyCode, Token: codeToken);

            if (action == "markMailRead" && args.Count == 2 && args.TryGetValue("token", out var mailToken) &&
                NotificationActivationParser.IsOpaqueToken(mailToken))
                return new(NotificationActivationKind.MailAction, action, mailToken);

            // Only the explicit test entry and notifications with no arguments open
            // the default page. Invalid mail actions must not look like success.
            return new(action == "smoke" && args.Count == 1
                ? NotificationActivationKind.OpenMain : NotificationActivationKind.Ignore);
        }
    }
}
