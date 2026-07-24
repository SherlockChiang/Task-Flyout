using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class MailMutationCapabilityPolicyTests
{
    [Theory]
    [InlineData(MailAccountKind.Google)]
    [InlineData(MailAccountKind.Imap)]
    public void Safe_state_mutations_are_supported_but_destructive_operations_are_not(MailAccountKind provider)
    {
        var capabilities = MailMutationCapabilityPolicy.For(provider);

        Assert.True(capabilities.SetReadState);
        Assert.True(capabilities.SetFlagged);
        Assert.False(capabilities.Archive);
        Assert.False(capabilities.Move);
        Assert.False(capabilities.Trash);
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
