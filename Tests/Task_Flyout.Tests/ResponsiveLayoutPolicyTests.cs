using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class ResponsiveLayoutPolicyTests
{
    [Theory]
    [InlineData(0, ResponsiveLayoutMode.Narrow)]
    [InlineData(719, ResponsiveLayoutMode.Narrow)]
    [InlineData(720, ResponsiveLayoutMode.Medium)]
    [InlineData(1099, ResponsiveLayoutMode.Medium)]
    [InlineData(1100, ResponsiveLayoutMode.Wide)]
    [InlineData(1920, ResponsiveLayoutMode.Wide)]
    public void Selects_layout_mode_at_defined_breakpoints(double width, ResponsiveLayoutMode expected)
    {
        Assert.Equal(expected, ResponsiveLayoutPolicy.GetMode(width));
    }
}
