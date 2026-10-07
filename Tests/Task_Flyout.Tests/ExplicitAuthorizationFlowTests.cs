using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class ExplicitAuthorizationFlowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cached_authorization_never_opens_interactive_flow(bool userAction)
    {
        int interactiveCalls = 0;
        int result = await ExplicitAuthorizationFlow.AcquireAsync(
            _ => Task.FromResult(7), userAction ? Connect : null);

        Assert.Equal(7, result);
        Assert.Equal(0, interactiveCalls);

        Task<int> Connect(CancellationToken _) { interactiveCalls++; return Task.FromResult(9); }
    }

    [Fact]
    public async Task Background_missing_or_expired_consent_requires_reconnect()
    {
        var required = new AuthorizationInteractionRequiredException("Microsoft", "Reconnect");
        var thrown = await Assert.ThrowsAsync<AuthorizationInteractionRequiredException>(() =>
            ExplicitAuthorizationFlow.AcquireAsync<int>(_ => Task.FromException<int>(required)));

        Assert.Same(required, thrown);
    }

    [Fact]
    public async Task Explicit_action_can_request_consent_after_silent_restore_fails()
    {
        var calls = new List<string>();
        int result = await ExplicitAuthorizationFlow.AcquireAsync<int>(
            _ => { calls.Add("silent"); return Task.FromException<int>(new AuthorizationInteractionRequiredException("Microsoft", "Reconnect")); },
            _ => { calls.Add("interactive"); return Task.FromResult(9); });

        Assert.Equal(9, result);
        Assert.Equal(new[] { "silent", "interactive" }, calls);
    }

    [Fact]
    public async Task Network_error_does_not_trigger_consent()
    {
        var failure = new HttpRequestException("Offline");
        int interactiveCalls = 0;
        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() => ExplicitAuthorizationFlow.AcquireAsync<int>(
            _ => Task.FromException<int>(failure),
            _ => { interactiveCalls++; return Task.FromResult(9); }));

        Assert.Same(failure, thrown);
        Assert.Equal(0, interactiveCalls);
    }

    [Fact]
    public async Task Pre_cancelled_action_performs_no_authorization_work()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        int calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExplicitAuthorizationFlow.AcquireAsync<int>(
            _ => { calls++; return Task.FromResult(7); },
            _ => { calls++; return Task.FromResult(9); }, cts.Token));

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Cancellation_during_restore_prevents_browser_fallback()
    {
        using var cts = new CancellationTokenSource();
        int interactiveCalls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExplicitAuthorizationFlow.AcquireAsync<int>(
            token =>
            {
                Assert.Equal(cts.Token, token);
                cts.Cancel();
                return Task.FromException<int>(new AuthorizationInteractionRequiredException("Microsoft", "Reconnect"));
            },
            _ => { interactiveCalls++; return Task.FromResult(9); }, cts.Token));

        Assert.Equal(0, interactiveCalls);
    }

    [Fact]
    public async Task Denied_interactive_consent_is_propagated_without_retry()
    {
        var denied = new InvalidOperationException("Admin approval required");
        int interactiveCalls = 0;
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => ExplicitAuthorizationFlow.AcquireAsync<int>(
            _ => Task.FromException<int>(new AuthorizationInteractionRequiredException("Microsoft", "Reconnect")),
            _ => { interactiveCalls++; return Task.FromException<int>(denied); }));

        Assert.Same(denied, thrown);
        Assert.Equal(1, interactiveCalls);
    }

    [Fact]
    public async Task Browser_cancellation_propagates_without_retry()
    {
        using var cts = new CancellationTokenSource();
        int interactiveCalls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExplicitAuthorizationFlow.AcquireAsync<int>(
            _ => Task.FromException<int>(new AuthorizationInteractionRequiredException("Microsoft", "Reconnect")),
            token => { interactiveCalls++; cts.Cancel(); return Task.FromCanceled<int>(token); }, cts.Token));

        Assert.Equal(1, interactiveCalls);
    }
}
