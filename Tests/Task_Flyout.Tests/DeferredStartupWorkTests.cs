using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class DeferredStartupWorkTests
{
    [Fact]
    public async Task Concurrent_callers_share_one_background_task()
    {
        var coordinator = new DeferredStartupWork();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        Task Work()
        {
            Interlocked.Increment(ref calls);
            return release.Task;
        }

        var first = coordinator.RunAsync(Work);
        var second = coordinator.RunAsync(Work);
        release.SetResult();
        await Task.WhenAll(first, second);

        Assert.Same(first, second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Completed_work_is_not_repeated()
    {
        var coordinator = new DeferredStartupWork();
        var calls = 0;

        await coordinator.RunAsync(() =>
        {
            Interlocked.Increment(ref calls);
            return Task.CompletedTask;
        });
        await coordinator.RunAsync(() =>
        {
            Interlocked.Increment(ref calls);
            return Task.CompletedTask;
        });

        Assert.Equal(1, calls);
    }
}
