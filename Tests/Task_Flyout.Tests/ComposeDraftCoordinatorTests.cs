using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class ComposeDraftCoordinatorTests
{
    [Fact]
    public async Task Rapid_edits_persist_only_latest_draft()
    {
        var repository = new FakeRepository();
        var coordinator = new ComposeDraftCoordinator(repository, TimeSpan.FromMilliseconds(10));
        coordinator.Schedule(Draft("first"));
        coordinator.Schedule(Draft("second"));

        await repository.WaitForSaveAsync();

        Assert.Equal("second", repository.Current?.Subject);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task Discard_prevents_stale_autosave_recreation()
    {
        var repository = new FakeRepository();
        var coordinator = new ComposeDraftCoordinator(repository, TimeSpan.FromMilliseconds(30));
        coordinator.Schedule(Draft("private"));

        await coordinator.DiscardAsync();
        await Task.Delay(60);

        Assert.Null(repository.Current);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task Flush_persists_without_waiting_for_debounce()
    {
        var repository = new FakeRepository();
        var coordinator = new ComposeDraftCoordinator(repository, TimeSpan.FromMinutes(1));
        coordinator.Schedule(Draft("flush"));

        await coordinator.FlushAsync();

        Assert.Equal("flush", repository.Current?.Subject);
    }

    [Fact]
    public async Task Account_removal_discards_only_matching_draft()
    {
        var repository = new FakeRepository { Current = Draft("draft", "account-a") };
        var coordinator = new ComposeDraftCoordinator(repository);

        await coordinator.DiscardForAccountAsync("account-b");
        Assert.NotNull(repository.Current);
        await coordinator.DiscardForAccountAsync("account-a");
        Assert.Null(repository.Current);
    }

    [Fact]
    public async Task Flush_without_session_edits_preserves_existing_draft()
    {
        var existing = Draft("not now");
        var repository = new FakeRepository { Current = existing };
        var coordinator = new ComposeDraftCoordinator(repository);

        await coordinator.FlushAsync();

        Assert.Same(existing, repository.Current);
    }

    [Fact]
    public async Task Empty_draft_securely_deletes_existing_content()
    {
        var repository = new FakeRepository { Current = Draft("existing") };
        var coordinator = new ComposeDraftCoordinator(repository, TimeSpan.FromMinutes(1));
        coordinator.Schedule(new ComposeDraft(1, "account", "", "", "", DateTimeOffset.UtcNow));

        await coordinator.FlushAsync();

        Assert.Null(repository.Current);
    }

    private static ComposeDraft Draft(string subject, string accountId = "account")
        => new(ComposeDraft.CurrentSchemaVersion, accountId, "recipient@example.test", subject, "body", DateTimeOffset.UtcNow);

    private sealed class FakeRepository : IComposeDraftRepository
    {
        public ComposeDraft? Current { get; set; }
        public int SaveCount { get; private set; }
        private readonly TaskCompletionSource _saved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ComposeDraft? Load() => Current;
        public Task SaveAsync(ComposeDraft draft) { Current = draft; SaveCount++; _saved.TrySetResult(); return Task.CompletedTask; }
        public Task DeleteAsync() { Current = null; return Task.CompletedTask; }
        public Task WaitForSaveAsync() => _saved.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
