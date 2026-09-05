using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public sealed class FlyoutForegroundProbeTests
{
    [Theory]
    [InlineData("DesktopFlyoutHostClass", true)]
    [InlineData("DesktopFlyoutHostClass.0123456789abcdef", true)]
    [InlineData("DesktopFlyoutHostClassExtra", false)]
    [InlineData("Windows.UI.Core.CoreWindow", false)]
    public void IsFlyoutHostClassName_accepts_dynamic_desktop_flyout_host_classes(
        string className,
        bool expected)
    {
        Assert.Equal(expected, FlyoutForegroundProbe.IsFlyoutHostClassName(className));
    }
}
