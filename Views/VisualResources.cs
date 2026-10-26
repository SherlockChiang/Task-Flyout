using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Task_Flyout.Views;

// Code-created controls use the same resources as XAML.
internal static class VisualResources
{
    public static FontFamily IconFont => (FontFamily)Application.Current.Resources["TaskFlyoutIconFont"];
    public static double Number(string name) => (double)Application.Current.Resources["TaskFlyout" + name];
    public static CornerRadius Corner(string name) => (CornerRadius)Application.Current.Resources["TaskFlyout" + name];
}
