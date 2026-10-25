using System.Net;
using System.Net.Http;
using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class AccountSyncHealthTests
{
    [Fact]
    public void Account_retry_never_falls_back_to_another_provider_or_disconnected_account()
    {
        var accounts = new[] { ("google|a", true), ("google|ab", true), ("microsoft|a", true), ("google|gone", false) };
        Assert.Equal(new[] { accounts[0] }, AccountSyncSelection.Select(accounts, item => item.Item1, item => item.Item2, "Google|A"));
        Assert.Empty(AccountSyncSelection.Select(accounts, item => item.Item1, item => item.Item2, "google|missing"));
        Assert.Empty(AccountSyncSelection.Select(accounts, item => item.Item1, item => item.Item2, "google|gone"));
        Assert.Equal(3, AccountSyncSelection.Select(accounts, item => item.Item1, item => item.Item2, null).Count);
    }

    [Theory]
    [InlineData("a@example.com", "A@example.com", null, true)]
    [InlineData("a@example.com", null, "a@example.com", true)]
    [InlineData("a@example.com", "ab@example.com", "b@example.com", false)]
    [InlineData("", "", null, false)]
    [InlineData("a@example.com", null, null, false)]
    public void Mailbox_verification_rejects_a_different_signed_in_account(string expected, string? mail, string? principal, bool matches)
        => Assert.Equal(matches, AccountSyncSelection.MatchesMailbox(expected, mail, principal));

    [Fact]
    public async Task Accounts_with_similar_keys_keep_independent_status_and_timestamps()
    {
        var tracker = new AccountSyncHealthTracker();
        await tracker.RunAsync("google|a", () => Task.FromResult(1));
        await Assert.ThrowsAsync<HttpRequestException>(() => tracker.RunAsync<int>("google|ab",
            () => Task.FromException<int>(new HttpRequestException())));

        Assert.Equal(AccountSyncState.Ready, tracker.Get("google|a").State);
        Assert.NotNull(tracker.Get("google|a").LastSuccessUtc);
        Assert.Equal(SyncFailureKind.Network, tracker.Get("google|ab").Failure);
        Assert.Null(tracker.Get("google|ab").LastSuccessUtc);
        Assert.Equal(AccountSyncState.Unknown, tracker.Get("microsoft|a").State);
    }

    [Fact]
    public async Task Older_failure_cannot_replace_newer_success()
    {
        var tracker = new AccountSyncHealthTracker();
        var older = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = tracker.RunAsync("account", () => older.Task);
        await tracker.RunAsync("account", () => Task.FromResult(2));
        var latest = tracker.Get("account");
        older.SetException(new HttpRequestException());
        await Assert.ThrowsAsync<HttpRequestException>(() => pending);

        Assert.Equal(latest, tracker.Get("account"));
    }

    [Fact]
    public async Task Older_success_cannot_hide_newer_failure()
    {
        var tracker = new AccountSyncHealthTracker();
        var older = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = tracker.RunAsync("account", () => older.Task);
        await Assert.ThrowsAsync<TimeoutException>(() => tracker.RunAsync<int>("account",
            () => Task.FromException<int>(new TimeoutException())));
        older.SetResult(1);
        await pending;

        Assert.Equal(SyncFailureKind.Timeout, tracker.Get("account").Failure);
        Assert.Null(tracker.Get("account").LastSuccessUtc);
    }

    [Fact]
    public async Task Failure_retains_last_success_without_storing_exception_text()
    {
        var tracker = new AccountSyncHealthTracker();
        await tracker.RunAsync("account", () => Task.FromResult(1));
        var success = tracker.Get("account").LastSuccessUtc;
        await Assert.ThrowsAsync<IOException>(() => tracker.RunAsync<int>("account",
            () => Task.FromException<int>(new IOException("private token and path"))));

        var failed = tracker.Get("account");
        Assert.Equal(success, failed.LastSuccessUtc);
        Assert.Equal(SyncFailureKind.Storage, failed.Failure);
        Assert.DoesNotContain("private", failed.ToString());
    }

    [Fact]
    public async Task Caller_cancellation_and_transport_timeout_are_different()
    {
        var tracker = new AccountSyncHealthTracker();
        using var cts = new CancellationTokenSource();
        var pending = tracker.RunAsync("cancel", async () => { await Task.Delay(Timeout.Infinite, cts.Token); return 1; }, cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tracker.RunAsync<int>("timeout",
            () => Task.FromException<int>(new OperationCanceledException())));

        Assert.Equal(AccountSyncState.Canceled, tracker.Get("cancel").State);
        Assert.Equal(SyncFailureKind.None, tracker.Get("cancel").Failure);
        Assert.Equal(SyncFailureKind.Timeout, tracker.Get("timeout").Failure);
    }

    [Fact]
    public async Task Removed_account_cannot_be_resurrected_by_inflight_request()
    {
        var tracker = new AccountSyncHealthTracker();
        var source = new TaskCompletionSource<int>();
        var pending = tracker.RunAsync("account", () => source.Task);
        tracker.Remove("account");
        source.SetResult(1);
        await pending;

        Assert.Equal(AccountSyncState.Unknown, tracker.Get("account").State);
    }

    [Fact]
    public async Task Observation_and_subscriber_failures_cannot_change_sync_outcome()
    {
        var tracker = new AccountSyncHealthTracker();
        tracker.Changed += (_, _) => throw new InvalidOperationException();
        Assert.Null(tracker.Get("account").LastAttemptUtc);
        Assert.Equal(42, await tracker.RunAsync("account", () => Task.FromResult(42)));
        Assert.Equal(AccountSyncState.Ready, tracker.Get("account").State);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, SyncFailureKind.Authorization)]
    [InlineData(HttpStatusCode.Forbidden, SyncFailureKind.Authorization)]
    [InlineData(HttpStatusCode.ServiceUnavailable, SyncFailureKind.Network)]
    public void Http_failures_have_safe_actionable_categories(HttpStatusCode status, SyncFailureKind expected)
        => Assert.Equal(expected, SyncFailurePolicy.Classify(new HttpRequestException("secret", null, status)));
}
