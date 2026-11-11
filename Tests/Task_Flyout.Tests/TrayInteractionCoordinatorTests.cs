using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public sealed class TrayInteractionCoordinatorTests
{
    [Fact]
    public async Task Single_click_waits_for_hydration_then_toggles_only_flyout()
    {
        var ready = new TaskCompletionSource();
        var calls = new List<string>();
        var coordinator = Create(calls, prepare: () => ready.Task);

        Task<bool> click = coordinator.ToggleFlyoutAsync();
        Assert.Empty(calls);
        ready.SetResult();

        Assert.True(await click);
        Assert.Equal(new[] { "toggle" }, calls);
    }

    [Fact]
    public async Task Double_click_dismisses_flyout_before_opening_main_window()
    {
        var closed = new TaskCompletionSource();
        var calls = new List<string>();
        var coordinator = Create(calls, dismiss: () => closed.Task);

        Task click = coordinator.OpenMainWindowAsync();
        Assert.Equal(new[] { "dismiss" }, calls);
        Assert.False(click.IsCompleted);
        closed.SetResult();
        await click;

        Assert.Equal(new[] { "dismiss", "main" }, calls);
    }

    [Fact]
    public async Task Double_click_invalidates_single_click_still_waiting_for_hydration()
    {
        var ready = new TaskCompletionSource();
        var calls = new List<string>();
        var coordinator = Create(calls, prepare: () => ready.Task);

        Task<bool> single = coordinator.ToggleFlyoutAsync();
        await coordinator.OpenMainWindowAsync();
        ready.SetResult();

        Assert.False(await single);
        Assert.Equal(new[] { "dismiss", "main" }, calls);
    }

    [Fact]
    public async Task Single_click_during_main_window_handoff_cannot_reopen_flyout()
    {
        var closed = new TaskCompletionSource();
        var calls = new List<string>();
        var coordinator = Create(calls, dismiss: () => closed.Task);

        Task main = coordinator.OpenMainWindowAsync();
        Assert.False(await coordinator.ToggleFlyoutAsync());
        closed.SetResult();
        await main;

        Assert.Equal(new[] { "dismiss", "main" }, calls);
    }

    [Fact]
    public async Task Double_click_after_single_click_closes_that_flyout_and_opens_main()
    {
        var calls = new List<string>();
        var coordinator = Create(calls);

        Assert.True(await coordinator.ToggleFlyoutAsync());
        await coordinator.OpenMainWindowAsync();

        Assert.Equal(new[] { "toggle", "dismiss", "main" }, calls);
    }

    [Fact]
    public async Task Later_single_click_can_toggle_after_main_window_handoff()
    {
        var calls = new List<string>();
        var coordinator = Create(calls);

        await coordinator.OpenMainWindowAsync();
        Assert.True(await coordinator.ToggleFlyoutAsync());
        Assert.True(await coordinator.ToggleFlyoutAsync());

        Assert.Equal(new[] { "dismiss", "main", "toggle", "toggle" }, calls);
    }

    [Fact]
    public async Task Repeated_double_clicks_share_closure_and_only_latest_opens_main()
    {
        var closed = new TaskCompletionSource();
        var calls = new List<string>();
        var coordinator = Create(calls, dismiss: () => closed.Task);

        Task first = coordinator.OpenMainWindowAsync();
        Task second = coordinator.OpenMainWindowAsync();
        closed.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(new[] { "dismiss", "dismiss", "main" }, calls);
    }

    [Fact]
    public async Task Canceled_dismissal_does_not_open_main_and_releases_handoff_guard()
    {
        var calls = new List<string>();
        var coordinator = Create(calls,
            dismiss: () => Task.FromCanceled(new CancellationToken(true)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(coordinator.OpenMainWindowAsync);
        Assert.True(await coordinator.ToggleFlyoutAsync());

        Assert.Equal(new[] { "dismiss", "toggle" }, calls);
    }

    [Fact]
    public async Task Hydration_failure_does_not_toggle_or_block_a_later_double_click()
    {
        var calls = new List<string>();
        var coordinator = Create(calls,
            prepare: () => Task.FromException(new InvalidOperationException()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ToggleFlyoutAsync());
        await coordinator.OpenMainWindowAsync();

        Assert.Equal(new[] { "dismiss", "main" }, calls);
    }

    private static TrayInteractionCoordinator Create(
        List<string> calls,
        Func<Task>? prepare = null,
        Func<Task>? dismiss = null)
        => new(
            prepare ?? (() => Task.CompletedTask),
            () => calls.Add("toggle"),
            () =>
            {
                calls.Add("dismiss");
                return dismiss?.Invoke() ?? Task.CompletedTask;
            },
            () => calls.Add("main"));
}
