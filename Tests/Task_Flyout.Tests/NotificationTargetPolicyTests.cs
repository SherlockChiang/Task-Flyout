using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public sealed class NotificationTargetPolicyTests
{
    [Fact]
    public void Agenda_target_requires_exact_list_account_day_and_type()
    {
        var target = new NotificationActionTarget(3, "Microsoft", "id", "2026-10-25",
            NotificationActionMask.Complete, DateTimeOffset.UtcNow.AddDays(1), AccountId: "a", ContainerId: "list", IsTask: true);
        Assert.True(NotificationTargetPolicy.MatchesAgenda(target, "Microsoft", "a", "list", "id", "2026-10-25", true));
        Assert.False(NotificationTargetPolicy.MatchesAgenda(target, "Google", "a", "list", "id", "2026-10-25", true));
        Assert.False(NotificationTargetPolicy.MatchesAgenda(target, "Microsoft", "b", "list", "id", "2026-10-25", true));
        Assert.False(NotificationTargetPolicy.MatchesAgenda(target, "Microsoft", "a", "other-list", "id", "2026-10-25", true));
        Assert.False(NotificationTargetPolicy.MatchesAgenda(target, "Microsoft", "a", "list", "other-id", "2026-10-25", true));
        Assert.False(NotificationTargetPolicy.MatchesAgenda(target, "Microsoft", "a", "list", "id", "2026-10-26", true));
        Assert.False(NotificationTargetPolicy.MatchesAgenda(target, "Microsoft", "a", "list", "id", "2026-10-25", false));
    }

    [Theory]
    [InlineData(MailAccountKind.Google)]
    [InlineData(MailAccountKind.Outlook)]
    [InlineData(MailAccountKind.Imap)]
    public void Mail_capability_cannot_cross_account_folder_message_or_mailbox(MailAccountKind kind)
    {
        var target = new NotificationActionTarget(3, kind.ToString(), "42", "", NotificationActionMask.MarkRead,
            DateTimeOffset.UtcNow.AddDays(1), AccountId: "a", ContainerId: "inbox", AccountAddress: "a@example.test", ImapUidValidity: 4);
        var item = new MailItem { AccountId = "a", Id = "42", FolderId = "inbox", ImapUidValidity = 4 };
        bool Matches(NotificationActionTarget value, string id = "a", string address = "a@example.test", bool setup = true)
            => NotificationTargetPolicy.MatchesMail(value, id, address, kind, setup, item);
        Assert.True(Matches(target));
        Assert.False(Matches(target, id: "b"));
        Assert.False(Matches(target, address: "b@example.test"));
        Assert.False(Matches(target, setup: false));
        Assert.False(Matches(target with { AccountId = "b" }));
        Assert.False(Matches(target with { ContainerId = "archive" }));
        Assert.False(Matches(target with { ItemId = "43" }));
        Assert.False(Matches(target with { AllowedActions = NotificationActionMask.Complete }));
        item.AccountId = "b";
        Assert.False(Matches(target));
    }

    [Theory]
    [InlineData(null)]
    [InlineData((uint)0)]
    [InlineData((uint)5)]
    public void Imap_changed_or_missing_uidvalidity_is_rejected(uint? uidValidity)
    {
        var target = new NotificationActionTarget(3, "Imap", "42", "", NotificationActionMask.MarkRead,
            DateTimeOffset.UtcNow.AddDays(1), AccountId: "a", ContainerId: "inbox", AccountAddress: "a@example.test", ImapUidValidity: uidValidity);
        var item = new MailItem { AccountId = "a", Id = "42", FolderId = "inbox", ImapUidValidity = 4 };
        Assert.False(NotificationTargetPolicy.MatchesMail(target, "a", "a@example.test", MailAccountKind.Imap, true, item));
    }
}
