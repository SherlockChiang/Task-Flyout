using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class ContentStateActionGateTests
{
    [Fact]
    public void DoubleClickCannotStartAnotherAction()
    {
        var gate = new ContentStateActionGate();
        Assert.True(gate.TryBegin(out int version));
        Assert.False(gate.TryBegin(out _));
        Assert.True(gate.IsCurrent(version));
        gate.End();
        Assert.True(gate.TryBegin(out _));
    }

    [Fact]
    public void NavigationOrUnloadInvalidatesLateFailure()
    {
        var gate = new ContentStateActionGate();
        gate.TryBegin(out int previous);
        gate.Replace();
        Assert.False(gate.IsCurrent(previous));
        Assert.False(gate.TryBegin(out _));
        gate.End();
        Assert.True(gate.TryBegin(out int current));
        Assert.True(gate.IsCurrent(current));
    }

    [Fact]
    public void ShowingLoadingOrResultDoesNotReleaseRunningAction()
    {
        var gate = new ContentStateActionGate();
        gate.TryBegin(out int previous);
        gate.Replace();
        gate.Replace();
        Assert.True(gate.IsRunning);
        Assert.False(gate.TryBegin(out _));
        Assert.False(gate.IsCurrent(previous));
        gate.End();
        Assert.False(gate.IsRunning);
    }
}
