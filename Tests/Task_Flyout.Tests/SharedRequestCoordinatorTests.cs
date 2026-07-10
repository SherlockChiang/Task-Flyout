using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class SharedRequestCoordinatorTests
{
    [Fact]
    public async Task Cancelling_one_waiter_does_not_cancel_shared_request()
    {
        var coordinator = new SharedRequestCoordinator<string>();
        var resultSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryCalls = 0;
        var providerCancelled = false;
        Task<string> Factory(CancellationToken token)
        {
            factoryCalls++;
            token.Register(() => providerCancelled = true);
            return resultSource.Task;
        }

        using var firstCancellation = new CancellationTokenSource();
        var first = coordinator.RunAsync("same", Factory, firstCancellation.Token);
        var second = coordinator.RunAsync("same", Factory, CancellationToken.None);
        firstCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.False(providerCancelled);
        resultSource.SetResult("ok");
        Assert.Equal("ok", await second);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task Cancelling_final_waiter_cancels_provider_request()
    {
        var coordinator = new SharedRequestCoordinator<string>();
        var providerCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var callerCancellation = new CancellationTokenSource();

        var request = coordinator.RunAsync(
            "only",
            async token =>
            {
                token.Register(() => providerCancelled.TrySetResult());
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return "unreachable";
            },
            callerCancellation.Token);

        callerCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        await providerCancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Old_request_completion_does_not_remove_new_request_with_same_key()
    {
        var coordinator = new SharedRequestCoordinator<string>();
        var oldSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryCalls = 0;
        Task<string> Factory(CancellationToken _)
            => ++factoryCalls == 1 ? oldSource.Task : newSource.Task;

        using var oldCallerCancellation = new CancellationTokenSource();
        var oldRequest = coordinator.RunAsync("key", Factory, oldCallerCancellation.Token);
        oldCallerCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => oldRequest);

        var newRequest = coordinator.RunAsync("key", Factory, CancellationToken.None);
        oldSource.SetResult("old");
        await Task.Yield();
        var sharedNewRequest = coordinator.RunAsync("key", Factory, CancellationToken.None);

        newSource.SetResult("new");
        Assert.Equal("new", await newRequest);
        Assert.Equal("new", await sharedNewRequest);
        Assert.Equal(2, factoryCalls);
    }

    [Fact]
    public async Task Synchronous_factory_failure_does_not_poison_request_key()
    {
        var coordinator = new SharedRequestCoordinator<string>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RunAsync(
            "retryable",
            _ => throw new InvalidOperationException("failed before returning a task"),
            CancellationToken.None));

        Assert.Equal("ok", await coordinator.RunAsync(
            "retryable",
            _ => Task.FromResult("ok"),
            CancellationToken.None));
    }
}
