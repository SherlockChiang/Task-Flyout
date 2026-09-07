using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public sealed class LatestSearchWorkTests
{
    [Fact]
    public void Close_and_reopen_reject_prior_session_and_default_request()
    {
        var work = new LatestSearchWork();
        Assert.False(work.IsCurrent(default));
        var first = work.Begin();
        Assert.True(work.IsCurrent(first));
        work.Cancel();
        Assert.False(work.IsCurrent(first));
        var reopened = work.Begin();
        Assert.False(work.IsCurrent(first));
        Assert.True(work.IsCurrent(reopened));
        work.Cancel();
    }

    [Fact]
    public async Task Superseded_waiter_does_not_start_and_uncancellable_reader_cannot_publish()
    {
        var work = new LatestSearchWork();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var first = work.Begin();
        var firstResult = work.RunAsync(first, _ =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            return "stale";
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var second = work.Begin();
            bool secondRan = false;
            var secondResult = work.RunAsync(second, _ => { secondRan = true; return "second"; });
            var newest = work.Begin();
            var newestResult = work.RunAsync(newest, _ => "current");
            Assert.False(newestResult.IsCompleted);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => secondResult);
            Assert.False(secondRan);
            release.Set();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstResult);
            Assert.False(work.IsCurrent(first));
            Assert.Equal("current", await newestResult.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(work.IsCurrent(newest));
        }
        finally { release.Set(); work.Cancel(); }
    }

    [Fact]
    public async Task Reader_failure_releases_worker_for_the_next_search()
    {
        var work = new LatestSearchWork();
        var failed = work.Begin();
        await Assert.ThrowsAsync<IOException>(() => work.RunAsync<int>(failed, _ => throw new IOException()));
        var next = work.Begin();
        Assert.Equal(42, await work.RunAsync(next, _ => 42));
        work.Cancel();
    }

    [Fact]
    public async Task Closed_request_never_calls_the_cache_reader()
    {
        var work = new LatestSearchWork();
        var request = work.Begin();
        work.Cancel();
        bool called = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work.RunAsync(request, _ => { called = true; return 1; }));
        Assert.False(called);
    }

    [Theory]
    [InlineData("")]
    [InlineData("match")]
    public void Matching_checks_cancellation_before_enumerating(string query)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => GlobalSearchPolicy.Search(
            Array.Empty<GlobalSearchCandidate>(), query, cancellation.Token));
    }

    [Fact]
    public void Matching_stops_during_a_large_snapshot()
    {
        using var cancellation = new CancellationTokenSource();
        int visited = 0;
        IEnumerable<GlobalSearchCandidate> Candidates()
        {
            for (int i = 0; i < 1000; i++)
            {
                visited++;
                if (i == 2) cancellation.Cancel();
                yield return new(i.ToString(), GlobalSearchGroupKind.Calendar, "match", "", "match");
            }
        }
        Assert.Throws<OperationCanceledException>(() => GlobalSearchPolicy.Search(Candidates(), "match", cancellation.Token));
        Assert.Equal(3, visited);
    }
}
