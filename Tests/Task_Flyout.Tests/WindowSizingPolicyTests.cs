using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class WindowSizingPolicyTests
{
    [Theory]
    [InlineData(1.5, 1920, 1040)]
    [InlineData(2.0, 1920, 1040)]
    public void Main_window_is_clamped_inside_scaled_work_area(double scale, int workWidth, int workHeight)
    {
        var size = WindowSizingPolicy.Calculate(1200, 800, scale, workWidth, workHeight, 0);
        Assert.InRange(size.Width, 1, workWidth - size.Margin * 2);
        Assert.InRange(size.Height, 1, workHeight - size.Margin * 2);
    }

    [Fact]
    public void Desired_logical_size_is_scaled_when_space_allows()
    {
        var size = WindowSizingPolicy.Calculate(1200, 800, 1.5, 3000, 2000, 0);
        Assert.Equal(1800, size.Width);
        Assert.Equal(1200, size.Height);
    }

    [Fact]
    public void Main_window_can_fill_the_work_area_without_padding()
    {
        var size = WindowSizingPolicy.Calculate(1920, 1040, 1, 1920, 1040, 0);
        Assert.Equal(1920, size.Width);
        Assert.Equal(1040, size.Height);
        Assert.Equal(0, size.Margin);
    }

    [Fact]
    public void Initial_main_window_is_comfortable_not_full_work_area()
    {
        var size = WindowSizingPolicy.CalculateInitialMainWindow(1920, 1040);

        Assert.Equal(1480, size.Width);
        Assert.Equal(874, size.Height);
        Assert.Equal(0, size.Margin);
    }
}
