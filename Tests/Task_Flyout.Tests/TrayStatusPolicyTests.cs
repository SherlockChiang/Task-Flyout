using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class TrayStatusPolicyTests
{
    [Theory]
    [InlineData(TrayStatus.Idle, "TrayStatus_Ready")]
    [InlineData(TrayStatus.Syncing, "TrayStatus_Syncing")]
    [InlineData(TrayStatus.Finished, "TrayStatus_Finished")]
    [InlineData(TrayStatus.NeedsAttention, "TrayStatus_NeedsAttention")]
    [InlineData(TrayStatus.NewMail, "TrayStatus_NewMail")]
    public void Describes_generic_status(TrayStatus status, string resourceKey)
    {
        var descriptor = TrayStatusPolicy.Describe(status);
        Assert.Equal(resourceKey, descriptor.ResourceKey);
        Assert.False(string.IsNullOrWhiteSpace(descriptor.Fallback));
    }
}
