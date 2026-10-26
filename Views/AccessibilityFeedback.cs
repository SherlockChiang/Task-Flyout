using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;

namespace Task_Flyout.Views;

internal static class AccessibilityFeedback
{
    internal static void Announce(FrameworkElement element)
    {
        AutomationProperties.SetLiveSetting(element, AutomationLiveSetting.Polite);
        if (!element.IsLoaded) return;
        var peer = FrameworkElementAutomationPeer.FromElement(element) ?? FrameworkElementAutomationPeer.CreatePeerForElement(element);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
