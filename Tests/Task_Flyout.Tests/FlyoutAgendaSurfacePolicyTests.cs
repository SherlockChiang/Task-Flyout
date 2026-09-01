using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class FlyoutAgendaSurfacePolicyTests
{
    [Theory]
    [InlineData(0, false, false, true, false, false)]
    [InlineData(1, false, true, false, false, true)]
    [InlineData(2, false, true, false, false, false)]
    [InlineData(2, true, true, true, false, false)]
    [InlineData(3, true, true, false, false, false)]
    [InlineData(4, false, true, false, true, false)]
    [InlineData(4, true, true, true, true, false)]
    public void Presentation_separates_status_from_agenda_items(
        int kindValue,
        bool hasAgendaItems,
        bool showState,
        bool showList,
        bool showRetry,
        bool showAddAccount)
    {
        var presentation = FlyoutAgendaSurfacePolicy.GetPresentation(
            (FlyoutAgendaSurfaceKind)kindValue,
            hasAgendaItems);

        Assert.Equal(showState, presentation.ShowState);
        Assert.Equal(showList, presentation.ShowList);
        Assert.Equal(showRetry, presentation.ShowRetry);
        Assert.Equal(showAddAccount, presentation.ShowAddAccount);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void Only_real_agenda_items_open_the_editor(bool isEvent, bool isTask, bool expected)
        => Assert.Equal(expected, FlyoutAgendaSurfacePolicy.CanOpenEditor(isEvent, isTask));

    [Fact]
    public void Successful_provider_attempt_is_a_success()
    {
        var outcome = FlyoutAgendaSurfacePolicy.EvaluateSyncOutcome(
            new[] { new FlyoutProviderSyncAttempt(true, true, false) });

        Assert.Equal(FlyoutSyncOutcomeKind.Success, outcome.Kind);
        Assert.True(outcome.KeepAgendaItems);
        Assert.False(outcome.CanRetry);
    }

    [Fact]
    public void Failed_provider_with_cache_keeps_items_and_offers_retry()
    {
        var outcome = FlyoutAgendaSurfacePolicy.EvaluateSyncOutcome(
            new[] { new FlyoutProviderSyncAttempt(true, false, true) });

        Assert.Equal(FlyoutSyncOutcomeKind.Failure, outcome.Kind);
        Assert.True(outcome.KeepAgendaItems);
        Assert.True(outcome.CanRetry);
    }

    [Fact]
    public void Mixed_provider_results_are_partial_failure()
    {
        var outcome = FlyoutAgendaSurfacePolicy.EvaluateSyncOutcome(
            new[]
            {
                new FlyoutProviderSyncAttempt(true, true, false),
                new FlyoutProviderSyncAttempt(true, false, false)
            });

        Assert.Equal(FlyoutSyncOutcomeKind.PartialFailure, outcome.Kind);
        Assert.True(outcome.KeepAgendaItems);
        Assert.True(outcome.CanRetry);
    }

    [Fact]
    public void Stale_provider_failure_is_not_reported_as_this_refresh_failure()
    {
        var outcome = FlyoutAgendaSurfacePolicy.EvaluateSyncOutcome(
            new[] { new FlyoutProviderSyncAttempt(false, false, true) });

        Assert.Equal(FlyoutSyncOutcomeKind.Indeterminate, outcome.Kind);
        Assert.False(outcome.CanRetry);
    }
}
