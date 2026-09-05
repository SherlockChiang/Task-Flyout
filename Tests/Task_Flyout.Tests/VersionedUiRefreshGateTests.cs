using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public sealed class VersionedUiRefreshGateTests
{
    [Fact]
    public void Consecutive_notifications_share_one_dispatch_and_apply_latest_version()
    {
        var gate = new VersionedUiRefreshGate();

        Assert.True(gate.TryQueue(4));
        Assert.False(gate.TryQueue(6));
        Assert.False(gate.TryQueue(5));

        Assert.True(gate.TryBeginApply(out long version));
        Assert.Equal(6, version);
    }

    [Fact]
    public void Applied_or_older_versions_are_no_ops()
    {
        var gate = new VersionedUiRefreshGate();

        Assert.True(gate.TryQueue(8));
        Assert.True(gate.TryBeginApply(out _));
        Assert.False(gate.TryQueue(7));
        Assert.False(gate.TryQueue(8));
    }

    [Fact]
    public void Failed_dispatch_can_be_queued_again()
    {
        var gate = new VersionedUiRefreshGate();

        Assert.True(gate.TryQueue(2));
        gate.CancelQueuedDispatch();

        Assert.True(gate.TryQueue(2));
        Assert.True(gate.TryBeginApply(out long version));
        Assert.Equal(2, version);
    }
}
