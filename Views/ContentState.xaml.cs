using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using System;
using System.Threading.Tasks;
using Task_Flyout.Services;

namespace Task_Flyout.Views;

// Each owner supplies a scoped action. This control never replays a provider mutation.
public sealed partial class ContentState : UserControl
{
    private Func<Task>? _action;
    private readonly ContentStateActionGate _actions = new();
    internal bool IsLoading { get; private set; }
    internal bool IsError => Error.IsOpen;

    public ContentState()
    {
        InitializeComponent();
        Clear();
        Unloaded += (_, _) => Clear();
    }

    internal static string Text(string key, string fallback)
        => new ResourceLoader().GetStringOrDefault(key) ?? fallback;
    internal static string RetryLabel => Text("ContentState_Retry", "Retry");
    internal static string RetryHint => Text("ContentState_RetryHint", "Check your connection and retry. You can keep using any content already shown.");
    internal static string FilterHint => Text("ContentState_FilterHint", "Try another search or change the selected filters.");

    internal void Clear() => Show("", "", null, null, false, false, false);

    internal void SetLoading(string message, bool hasContent = false)
        => Show(message, "", null, null, true, false, !hasContent);

    internal void SetEmpty(string reason, string nextStep, string? actionLabel = null, Func<Task>? action = null)
        => Show(reason, nextStep, actionLabel, action, false, false, false);

    internal void SetError(string reason, string nextStep, string? actionLabel = null, Func<Task>? action = null)
        => Show(reason, nextStep, actionLabel, action, false, true, false);

    private void Show(string reason, string nextStep, string? actionLabel, Func<Task>? action, bool loading, bool error, bool skeleton)
    {
        _actions.Replace();
        _action = action;
        IsLoading = loading;
        Progress.IsIndeterminate = loading;
        Progress.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(Progress, reason);
        Skeleton.Visibility = skeleton ? Visibility.Visible : Visibility.Collapsed;
        Reason.Text = error ? "" : reason;
        Reason.Visibility = !error && reason.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Error.Message = error ? reason : "";
        Error.IsOpen = error;
        NextStep.Text = nextStep;
        NextStep.Visibility = nextStep.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ActionLabel.Text = actionLabel ?? "";
        ActionButton.Visibility = action != null ? Visibility.Visible : Visibility.Collapsed;
        ActionButton.IsEnabled = !_actions.IsRunning;
        AutomationProperties.SetName(ActionButton, actionLabel ?? "");
        Visibility = reason.Length > 0 || loading ? Visibility.Visible : Visibility.Collapsed;
        if (IsLoaded && !error)
        {
            var peer = FrameworkElementAutomationPeer.FromElement(Reason) ?? FrameworkElementAutomationPeer.CreatePeerForElement(Reason);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    private async void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        var action = _action;
        if (action == null || !_actions.TryBegin(out int version)) return;
        ActionButton.IsEnabled = false;
        try { await action(); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Content state action failed: {ex.GetType().Name}");
            if (_actions.IsCurrent(version) && IsLoaded)
                SetError(Text("ContentState_ActionFailed", "The action could not finish."), RetryHint, RetryLabel, action);
        }
        finally
        {
            _actions.End();
            ActionButton.IsEnabled = true;
        }
    }
}
