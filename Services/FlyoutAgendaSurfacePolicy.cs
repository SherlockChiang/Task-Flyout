using System.Collections.Generic;
using System.Linq;

namespace Task_Flyout.Services
{
    internal enum FlyoutAgendaSurfaceKind
    {
        Content,
        Onboarding,
        Empty,
        Loading,
        Error
    }

    internal readonly record struct FlyoutAgendaSurfacePresentation(
        bool ShowState,
        bool ShowList,
        bool ShowRetry,
        bool ShowAddAccount);

    internal readonly record struct FlyoutProviderSyncAttempt(
        bool Attempted,
        bool Succeeded,
        bool HasCachedData);

    internal enum FlyoutSyncOutcomeKind
    {
        NoProviders,
        Success,
        PartialFailure,
        Failure,
        Indeterminate
    }

    internal readonly record struct FlyoutSyncOutcome(
        FlyoutSyncOutcomeKind Kind,
        bool KeepAgendaItems,
        bool CanRetry);

    internal static class FlyoutAgendaSurfacePolicy
    {
        public static FlyoutAgendaSurfacePresentation GetPresentation(
            FlyoutAgendaSurfaceKind kind,
            bool hasAgendaItems)
            => kind switch
            {
                FlyoutAgendaSurfaceKind.Content => new(false, true, false, false),
                FlyoutAgendaSurfaceKind.Onboarding => new(true, false, false, true),
                FlyoutAgendaSurfaceKind.Empty => new(true, hasAgendaItems, false, false),
                FlyoutAgendaSurfaceKind.Loading => new(true, false, false, false),
                FlyoutAgendaSurfaceKind.Error => new(true, hasAgendaItems, true, false),
                _ => new(false, true, false, false)
            };

        public static bool CanOpenEditor(bool isEvent, bool isTask)
            => isEvent || isTask;

        public static FlyoutSyncOutcome EvaluateSyncOutcome(
            IReadOnlyList<FlyoutProviderSyncAttempt> providers)
        {
            if (providers.Count == 0)
                return new(FlyoutSyncOutcomeKind.NoProviders, false, false);

            if (providers.Any(provider => !provider.Attempted))
                return new(FlyoutSyncOutcomeKind.Indeterminate, true, false);

            int failures = providers.Count(provider => !provider.Succeeded);
            if (failures == 0)
                return new(FlyoutSyncOutcomeKind.Success, true, false);

            bool hasUsableData = providers.Any(provider => provider.Succeeded || provider.HasCachedData);
            return new(
                failures == providers.Count ? FlyoutSyncOutcomeKind.Failure : FlyoutSyncOutcomeKind.PartialFailure,
                hasUsableData,
                true);
        }
    }
}
