using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public sealed class NotificationActivationRouteTests
{
    [Theory]
    [InlineData("accountId=google-account;action=openMail;folderId=INBOX;messageId=18abc123")]
    [InlineData("----AppNotificationActivated:accountId=google-account;action=openMail;folderId=INBOX;messageId=18abc123")]
    public void Sdk_mail_arguments_reach_the_exact_mail_target(string argument)
    {
        var route = NotificationActivationRouter.Parse(argument, sdkEncoded: true);
        Assert.Equal(NotificationActivationKind.OpenMail, route.Kind);
        Assert.Equal("google-account", route.AccountId);
        Assert.Equal("INBOX", route.FolderId);
        Assert.Equal("18abc123", route.MessageId);
    }

    [Fact]
    public void Sdk_outlook_base64_ids_preserve_plus_slash_and_padding()
    {
        var route = NotificationActivationRouter.Parse(
            "action=openMail;accountId=outlook;folderId=A+B/C%3D;messageId=D+E/F%3D%3D", sdkEncoded: true);
        Assert.Equal(NotificationActivationKind.OpenMail, route.Kind);
        Assert.Equal("A+B/C=", route.FolderId);
        Assert.Equal("D+E/F==", route.MessageId);
    }

    [Theory]
    [InlineData("action=openMail&accountId=google-account&folderId=INBOX&messageId=18abc123")]
    [InlineData("action=openMail&amp;accountId=google-account&amp;folderId=INBOX&amp;messageId=18abc123")]
    public void Legacy_mail_query_arguments_remain_compatible(string argument)
    {
        var route = NotificationActivationRouter.Parse(argument);
        Assert.Equal(NotificationActivationKind.OpenMail, route.Kind);
        Assert.Equal("google-account", route.AccountId);
        Assert.Equal("18abc123", route.MessageId);
    }

    [Theory]
    [InlineData("action=openMail;accountId=a;folderId=INBOX")]
    [InlineData("action=openMail;accountId=a;folderId=INBOX;messageId=")]
    [InlineData("action=openMail;accountId=a;folderId=INBOX;messageId=m;action=smoke")]
    [InlineData("action=openMail;accountId=a;folderId=INBOX;messageId=m;extra=x")]
    [InlineData("action=openMail;accountId=a;folderId=INBOX;messageId=%GG")]
    [InlineData("action=openMail;accountId=a;folderId=INBOX;messageId=m&&action=smoke")]
    [InlineData("action=openAgenda;token=invalid")]
    [InlineData("action=copyCode;codeToken=invalid")]
    [InlineData("action=unknown")]
    [InlineData("----AppNotificationActivated:")]
    public void Rejected_activations_never_fall_back_to_calendar(string argument)
        => Assert.Equal(NotificationActivationKind.Ignore, NotificationActivationRouter.Parse(argument).Kind);

    [Theory]
    [InlineData("openAgenda")]
    [InlineData("snoozeAgenda")]
    [InlineData("completeTask")]
    public void Sdk_agenda_buttons_keep_capability_validation(string action)
    {
        const string token = "0123456789abcdef0123456789abcdef";
        var route = NotificationActivationRouter.Parse($"action={action};token={token}", sdkEncoded: true);
        Assert.Equal(NotificationActivationKind.AgendaAction, route.Kind);
        Assert.Equal(action, route.Action);
        Assert.Equal(token, route.Token);
    }

    [Theory]
    [InlineData("")]
    [InlineData("action=smoke")]
    public void Explicit_generic_and_smoke_activation_still_open_main(string argument)
        => Assert.Equal(NotificationActivationKind.OpenMain, NotificationActivationRouter.Parse(argument).Kind);

    [Fact]
    public void Sdk_copy_button_uses_only_the_opaque_token()
    {
        var route = NotificationActivationRouter.Parse("action=copyCode;codeToken=0123456789abcdef0123456789abcdef", sdkEncoded: true);
        Assert.Equal(NotificationActivationKind.CopyCode, route.Kind);
    }

    [Fact]
    public void Mail_ids_containing_agenda_action_words_are_not_misrouted()
        => Assert.Equal(NotificationActivationKind.OpenMail, NotificationActivationRouter.Parse(
            "action=openMail;accountId=a;folderId=INBOX;messageId=openAgenda-completeTask-snoozeAgenda", sdkEncoded: true).Kind);
}
