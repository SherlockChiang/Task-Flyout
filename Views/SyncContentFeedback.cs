using System;
using System.Linq;
using System.Threading.Tasks;
using Task_Flyout.Services;

namespace Task_Flyout.Views;

internal static class SyncContentFeedback
{
    // GetAllDataAsync may return cached items when a provider fails. Do not present
    // that as an empty successful refresh, or lose the account's reconnect action.
    internal static bool ShowResult(ContentState state, SyncManager manager, Func<Task> retry, bool tasksOnly = false)
    {
        var failures = manager.AccountManager.Accounts
            .Where(account => !tasksOnly || SyncProviderCapabilityPolicy.ForProvider(account.ProviderName).SupportsTasks)
            .Select(account => manager.GetProviderHealth(account.ProviderName, account.AccountId))
            .Where(health => health.Kind is ProviderHealthKind.Cached or ProviderHealthKind.Failed or ProviderHealthKind.ReconnectRequired)
            .ToList();
        if (failures.Count == 0) { state.Clear(); return true; }
        bool reconnect = failures.Any(health => health.Kind == ProviderHealthKind.ReconnectRequired);
        state.SetError(ContentState.Text("ContentState_SyncIncomplete", "Some accounts could not refresh. Any previously loaded items remain available."),
            reconnect ? ContentState.Text("ContentState_ReconnectHint", "Reconnect the affected account in Accounts and sync.") : ContentState.RetryHint,
            reconnect ? ContentState.Text("ContentState_Accounts", "Open accounts and sync") : ContentState.RetryLabel,
            reconnect ? () => { App.MyMainWindow?.NavigateToAddAccount(); return Task.CompletedTask; }
        : retry);
        return false;
    }
}
