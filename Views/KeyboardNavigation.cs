using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Threading.Tasks;
using Task_Flyout.Services;

namespace Task_Flyout.Views;

internal interface IPageKeyboardCommands
{
    bool IsKeyboardCommandBlocked => false;
    Task<bool> ExecuteKeyboardCommandAsync(PageShortcut command, DependencyObject? focused);
}

internal static class KeyboardNavigation
{
    internal static bool IsInput(DependencyObject? focused)
    {
        for (var node = focused; node != null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is TextBox or PasswordBox or RichEditBox or AutoSuggestBox or ComboBox
                or NumberBox or DatePicker or TimePicker or CalendarDatePicker or CalendarView
                or Slider or WebView2 or ContentDialog) return true;
        }
        return false;
    }

    internal static T? FocusedItem<T>(DependencyObject? focused, FrameworkElement owner) where T : class
    {
        T? item = null;
        for (var node = focused; node != null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is FrameworkElement element && element.DataContext is T value) item ??= value;
            if (ReferenceEquals(node, owner)) return item;
        }
        return null;
    }

    internal static bool IsVisible(DependencyObject element)
    {
        for (DependencyObject? node = element; node != null; node = VisualTreeHelper.GetParent(node))
            if (node is UIElement { Visibility: Visibility.Collapsed } || node is Control { IsEnabled: false }) return false;
        return element is FrameworkElement { IsLoaded: true };
    }

    internal static void FocusFirst(FrameworkElement owner)
    {
        if (IsVisible(owner)) (FocusManager.FindFirstFocusableElement(owner) as UIElement)?.Focus(FocusState.Keyboard);
    }

    internal static void RestoreIfLost(FrameworkElement owner, DependencyObject? previous, Func<UIElement?> target)
    {
        if (previous is not Control { FocusState: FocusState.Keyboard }) return;
        owner.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!IsVisible(owner) || owner.XamlRoot == null ||
                VisualTreeHelper.GetOpenPopupsForXamlRoot(owner.XamlRoot).Count > 0) return;
            var current = FocusManager.GetFocusedElement(owner.XamlRoot) as DependencyObject;
            if (current != null && !ReferenceEquals(current, previous) && IsVisible(current)) return;
            var next = target();
            if (next != null && IsVisible(next)) next.Focus(FocusState.Keyboard);
        });
    }
}
