using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class TaskMutationCoordinatorTests
{
    [Fact]
    public async Task Successful_operation_reports_pending_then_succeeded()
    {
        var coordinator = new TaskMutationCoordinator();
        var phases = new List<TaskMutationPhase>();

        var result = await coordinator.ExecuteAsync("task", () => Task.CompletedTask, state => phases.Add(state.Phase));

        Assert.Equal(TaskMutationPhase.Succeeded, result.Phase);
        Assert.Equal([TaskMutationPhase.Pending, TaskMutationPhase.Succeeded], phases);
    }

    [Fact]
    public async Task Failed_operation_can_be_retried()
    {
        var coordinator = new TaskMutationCoordinator();
        var calls = 0;
        Task Operation()
        {
            if (Interlocked.Increment(ref calls) == 1) throw new InvalidOperationException();
            return Task.CompletedTask;
        }

        var failed = await coordinator.ExecuteAsync("task", Operation);
        var phases = new List<TaskMutationPhase>();
        var retried = await coordinator.RetryAsync("task", state => phases.Add(state.Phase));

        Assert.Equal(TaskMutationPhase.Failed, failed.Phase);
        Assert.Equal(TaskMutationPhase.Succeeded, retried!.Phase);
        Assert.Equal([TaskMutationPhase.Queued, TaskMutationPhase.Pending, TaskMutationPhase.Succeeded], phases);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Duplicate_operation_is_queued_and_runs_after_first()
    {
        var coordinator = new TaskMutationCoordinator();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = coordinator.ExecuteAsync("task", () => release.Task);

        var phases = new List<TaskMutationPhase>();
        var duplicateTask = coordinator.ExecuteAsync("task", () => Task.CompletedTask, state => phases.Add(state.Phase));
        release.SetResult();
        await first;
        var duplicate = await duplicateTask;

        Assert.Equal(TaskMutationPhase.Succeeded, duplicate.Phase);
        Assert.Contains(TaskMutationPhase.Queued, phases);
        Assert.Equal(TaskMutationPhase.Pending, phases[^2]);
        Assert.Equal(TaskMutationPhase.Succeeded, phases[^1]);
    }

    [Fact]
    public async Task New_successful_operation_clears_stale_retry()
    {
        var coordinator = new TaskMutationCoordinator();
        await coordinator.ExecuteAsync("task", () => Task.FromException(new InvalidOperationException()));

        await coordinator.ExecuteAsync("task", () => Task.CompletedTask);

        Assert.Null(await coordinator.RetryAsync("task"));
    }

    [Fact]
    public async Task Counts_reflect_pending_and_retryable_operations()
    {
        var coordinator = new TaskMutationCoordinator();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = coordinator.ExecuteAsync("pending", async () =>
        {
            entered.SetResult();
            await release.Task;
        });

        await entered.Task;
        Assert.Equal(1, coordinator.PendingCount);
        Assert.Equal(0, coordinator.FailedCount);

        await coordinator.ExecuteAsync("failed", () => Task.FromException(new InvalidOperationException()));
        Assert.Equal(1, coordinator.FailedCount);

        release.SetResult();
        await pending;
        Assert.Equal(0, coordinator.PendingCount);
    }
}
