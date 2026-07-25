using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class MailMutationCapabilityPolicyTests
{
    [Fact]
    public void Imap_safe_state_mutations_are_supported_but_move_operations_are_not()
    {
        var capabilities = MailMutationCapabilityPolicy.For(MailAccountKind.Imap);

        Assert.True(capabilities.SetReadState);
        Assert.True(capabilities.SetFlagged);
        Assert.False(capabilities.Archive);
        Assert.False(capabilities.Move);
        Assert.False(capabilities.Trash);
        Assert.False(capabilities.PermanentDelete);
    }

    [Fact]
    public void Gmail_supports_label_and_trash_actions_but_not_permanent_delete()
    {
        var capabilities = MailMutationCapabilityPolicy.For(MailAccountKind.Google);

        Assert.True(capabilities.SetReadState);
        Assert.True(capabilities.SetFlagged);
        Assert.True(capabilities.Archive);
        Assert.True(capabilities.Move);
        Assert.True(capabilities.Trash);
        Assert.False(capabilities.PermanentDelete);
    }


    [Fact]
    public void Outlook_supports_identity_changing_moves_but_not_permanent_delete()
    {
        var capabilities = MailMutationCapabilityPolicy.For(MailAccountKind.Outlook);

        Assert.True(capabilities.SetReadState);
        Assert.True(capabilities.SetFlagged);
        Assert.True(capabilities.Archive);
        Assert.True(capabilities.Move);
        Assert.True(capabilities.Trash);
        Assert.False(capabilities.PermanentDelete);
    }
}
