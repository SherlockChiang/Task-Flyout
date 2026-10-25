using System.Xml.Linq;
using Microsoft.Windows.AppNotifications.Builder;
using Task_Flyout.Services;

// Builds synthetic payloads only. Never registers, shows a toast, opens a page,
// reads application data, contacts a provider, or mutates the real mailbox.
int checks = 0;
void Require(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
    checks++;
}

foreach (var target in new[]
{
    (Account: "test-google", Folder: "INBOX", Message: "18abc123"),
    (Account: "test-outlook", Folder: "A+B/C=", Message: "D+E/F==")
})
{
    var notification = new AppNotificationBuilder().AddText("Synthetic contract fixture")
        .AddArgument("action", "openMail").AddArgument("accountId", target.Account)
        .AddArgument("folderId", target.Folder).AddArgument("messageId", target.Message)
        .BuildNotification();
    string launch = (string)XDocument.Parse(notification.Payload).Root!.Attribute("launch")!;
    Require(launch.Contains(';'), "Unexpected SDK wire format; update the transport contract.");
    foreach (bool sdkEvent in new[] { false, true })
    {
        var route = NotificationActivationRouter.Parse(launch, sdkEncoded: sdkEvent);
        Require(route.Kind == NotificationActivationKind.OpenMail && route.AccountId == target.Account &&
            route.FolderId == target.Folder && route.MessageId == target.Message,
            "The actual SDK builder payload lost its mail target.");
    }
}

const string token = "0123456789abcdef0123456789abcdef";
var withButtons = new AppNotificationBuilder().AddText("Synthetic action fixture")
    .AddArgument("action", "openAgenda").AddArgument("token", token)
    .AddButton(new AppNotificationButton("Snooze").AddArgument("action", "snoozeAgenda").AddArgument("token", token))
    .AddButton(new AppNotificationButton("Complete").AddArgument("action", "completeTask").AddArgument("token", token))
    .AddButton(new AppNotificationButton("Copy").AddArgument("action", "copyCode").AddArgument("codeToken", token))
    .AddButton(new AppNotificationButton("Read").AddArgument("action", "markMailRead").AddArgument("token", token))
    .MuteAudio()
    .BuildNotification();
var xml = XDocument.Parse(withButtons.Payload);
Require(NotificationActivationRouter.Parse((string)xml.Root!.Attribute("launch")!, true).Kind == NotificationActivationKind.AgendaAction,
    "SDK agenda launch payload was rejected.");
foreach (var action in xml.Descendants("action"))
{
    var route = NotificationActivationRouter.Parse((string)action.Attribute("arguments")!, true);
    Require(route.Kind is NotificationActivationKind.AgendaAction or NotificationActivationKind.CopyCode or NotificationActivationKind.MailAction,
        "SDK button payload was rejected.");
    Require(route.Token == token, "SDK button token changed.");
}

Require((string?)xml.Descendants("audio").Single().Attribute("silent") == "true", "Quiet delivery must mute audio.");
withButtons.SuppressDisplay = true;
withButtons.Group = "tasks";
withButtons.Tag = "0123456789abcdef";
Require(withButtons.SuppressDisplay && withButtons.Group == "tasks" && withButtons.Tag == "0123456789abcdef",
    "Quiet delivery and notification identity did not round-trip through the SDK.");

Console.WriteLine($"SDK notification contract passed: {checks} checks; no notifications shown or application data accessed.");
