using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using Task_Flyout.Services;

namespace Task_Flyout.Views;

internal sealed class CompactPaneFocus
{
    private sealed record FocusTarget(WeakReference<UIElement> Element, object? DataContext);
    private readonly Dictionary<CompactPane, FocusTarget> _targets = new();
    private int _version;

    public void Cancel() => _version++;

    public static bool HasFocus(FrameworkElement root)
        => root.XamlRoot != null && FocusManager.GetFocusedElement(root.XamlRoot) is UIElement element && IsInside(element, root);

    public void Remember(CompactPane pane, FrameworkElement root)
    {
        if (root.XamlRoot != null && FocusManager.GetFocusedElement(root.XamlRoot) is UIElement focused
            && IsInside(focused, root))
            _targets[pane] = new FocusTarget(new WeakReference<UIElement>(focused), (focused as FrameworkElement)?.DataContext);
    }

    public void Restore(CompactPane pane, FrameworkElement root, Func<UIElement?> fallback)
    {
        int version = ++_version;
        var previous = root.XamlRoot == null ? null : FocusManager.GetFocusedElement(root.XamlRoot);
        root.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (version != _version || !root.IsLoaded || root.Visibility != Visibility.Visible || root.XamlRoot == null) return;
            var current = FocusManager.GetFocusedElement(root.XamlRoot);
            // A user or dialog that moved focus after navigation takes precedence.
            if (current != null && !ReferenceEquals(current, previous)) return;
            if (_targets.TryGetValue(pane, out var saved) && saved.Element.TryGetTarget(out var target)
                && ReferenceEquals(saved.DataContext, (target as FrameworkElement)?.DataContext)
                && IsInside(target, root) && target.Focus(FocusState.Programmatic)) return;
            var next = fallback();
            if (next != null && IsInside(next, root) && next.Focus(FocusState.Programmatic)) return;
            (FocusManager.FindFirstFocusableElement(root) as UIElement)?.Focus(FocusState.Programmatic);
        });
    }

    private static bool IsInside(DependencyObject element, DependencyObject root)
    {
        for (DependencyObject? node = element; node != null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is UIElement ui && ui.Visibility != Visibility.Visible) return false;
            if (ReferenceEquals(node, root)) return true;
        }
        return false;
    }
}
