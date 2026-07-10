using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class RssDataLifecycleTests
{
    [Fact]
    public void Begin_clear_cancels_and_invalidates_existing_lease()
    {
        var lifecycle = new RssDataLifecycle();
        var lease = lifecycle.Capture();

        lifecycle.BeginClear();

        Assert.True(lifecycle.IsClearInProgress);
        Assert.True(lease.CancellationToken.IsCancellationRequested);
        Assert.False(lifecycle.IsCurrent(lease));
    }

    [Fact]
    public void End_clear_allows_only_new_generation_to_commit()
    {
        var lifecycle = new RssDataLifecycle();
        var oldLease = lifecycle.Capture();
        lifecycle.BeginClear();
        lifecycle.EndClear();
        var currentLease = lifecycle.Capture();

        Assert.False(lifecycle.IsCurrent(oldLease));
        Assert.True(lifecycle.IsCurrent(currentLease));
    }

    [Fact]
    public void Lease_cannot_be_captured_during_clear()
    {
        var lifecycle = new RssDataLifecycle();
        lifecycle.BeginClear();

        Assert.Throws<InvalidOperationException>(() => lifecycle.Capture());
        lifecycle.EndClear();
        Assert.True(lifecycle.IsCurrent(lifecycle.Capture()));
    }

    [Fact]
    public async Task Concurrent_clear_invalidates_in_flight_work()
    {
        var lifecycle = new RssDataLifecycle();
        var lease = lifecycle.Capture();
        var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lease.CancellationToken.Register(() => cancellationObserved.SetResult());

        await Task.Run(lifecycle.BeginClear);

        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(lifecycle.IsCurrent(lease));
    }

    [Fact]
    public void Throwing_cancellation_callback_does_not_break_clear_barrier()
    {
        var lifecycle = new RssDataLifecycle();
        var lease = lifecycle.Capture();
        using var registration = lease.CancellationToken.Register(() => throw new InvalidOperationException("injected callback failure"));

        lifecycle.BeginClear();

        Assert.True(lifecycle.IsClearInProgress);
        Assert.False(lifecycle.IsCurrent(lease));
        lifecycle.EndClear();
        Assert.True(lifecycle.IsCurrent(lifecycle.Capture()));
    }
}
