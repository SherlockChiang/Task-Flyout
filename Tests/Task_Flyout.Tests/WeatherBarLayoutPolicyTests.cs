using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class WeatherBarLayoutPolicyTests
{
    private static readonly WeatherBarFieldRequest[] AllFields =
    {
        new(WeatherBarOptionalField.Description, true, 170, 56),
        new(WeatherBarOptionalField.Location, true, 120, 48),
        new(WeatherBarOptionalField.FeelsLike, true, 90, 44),
        new(WeatherBarOptionalField.Humidity, true, 90, 44),
        new(WeatherBarOptionalField.Wind, true, 90, 44)
    };

    [Fact]
    public void Available_width_reserves_both_widget_and_obstacle_gaps()
    {
        double width = WeatherBarLayoutPolicy.GetAvailableWidth(
            taskbarWidth: 1000,
            occupiedOffset: 180,
            rightBoundary: 400,
            leftGap: 6,
            rightGap: 6);

        Assert.Equal(208, width);
    }

    [Theory]
    [InlineData(1000, 0, 400, 6, 48, 352)]
    [InlineData(1000, 180, 0, 6, 48, 766)]
    [InlineData(1000, 400, 390, 6, 48, 0)]
    [InlineData(1000, 1000, 1000, 6, 48, 0)]
    public void Available_width_never_crosses_taskbar_boundaries(
        double taskbarWidth,
        double occupiedOffset,
        double rightBoundary,
        double leftGap,
        double rightGap,
        double expected)
        => Assert.Equal(expected, WeatherBarLayoutPolicy.GetAvailableWidth(
            taskbarWidth, occupiedOffset, rightBoundary, leftGap, rightGap));

    [Fact]
    public void Keeps_all_fields_when_they_fit()
    {
        var plan = Compute(700);

        Assert.True(plan.ShouldShow);
        Assert.Equal(170, plan.DescriptionWidth);
        Assert.Equal(120, plan.LocationWidth);
        Assert.Equal(90, plan.FeelsLikeWidth);
        Assert.Equal(90, plan.HumidityWidth);
        Assert.Equal(90, plan.WindWidth);
    }

    [Fact]
    public void Trims_low_priority_fields_before_hiding_them()
    {
        var plan = Compute(620);

        Assert.True(plan.ShouldShow);
        Assert.Equal(170, plan.DescriptionWidth);
        Assert.Equal(120, plan.LocationWidth);
        Assert.Equal(90, plan.FeelsLikeWidth);
        Assert.InRange(plan.HumidityWidth, 44, 89.99);
        Assert.Equal(44, plan.WindWidth);
        Assert.True(plan.Width <= 620);
    }

    [Fact]
    public void Removes_optional_fields_in_priority_order()
    {
        var plan = Compute(250);

        Assert.True(plan.ShouldShow);
        Assert.True(plan.DescriptionWidth > 0);
        Assert.True(plan.LocationWidth > 0);
        Assert.Equal(0, plan.FeelsLikeWidth);
        Assert.Equal(0, plan.HumidityWidth);
        Assert.Equal(0, plan.WindWidth);
        Assert.True(plan.Width <= 250);
    }

    [Fact]
    public void Keeps_only_icon_and_temperature_at_essential_width()
    {
        var plan = Compute(92);

        Assert.True(plan.ShouldShow);
        Assert.Equal(92, plan.Width);
        Assert.Equal(0, plan.DescriptionWidth);
        Assert.Equal(0, plan.LocationWidth);
        Assert.Equal(0, plan.FeelsLikeWidth);
        Assert.Equal(0, plan.HumidityWidth);
        Assert.Equal(0, plan.WindWidth);
    }

    [Fact]
    public void Hides_bar_when_essentials_do_not_fit()
        => Assert.False(Compute(91).ShouldShow);

    [Fact]
    public void Disabling_and_reenabling_icon_updates_required_width()
    {
        var fields = Array.Empty<WeatherBarFieldRequest>();
        var withIcon = WeatherBarLayoutPolicy.Compute(200, 20, 8, 24, 40, fields);
        var withoutIcon = WeatherBarLayoutPolicy.Compute(200, 20, 8, 0, 40, fields);
        var restoredIcon = WeatherBarLayoutPolicy.Compute(200, 20, 8, 24, 40, fields);

        Assert.Equal(92, withIcon.Width);
        Assert.Equal(60, withoutIcon.Width);
        Assert.Equal(withIcon, restoredIcon);
    }

    [Fact]
    public void Never_exceeds_available_width()
    {
        for (int width = 0; width <= 700; width++)
        {
            var plan = Compute(width);
            if (plan.ShouldShow)
                Assert.True(plan.Width <= width, $"Plan width {plan.Width} exceeded {width}.");
        }
    }

    private static WeatherBarLayoutPlan Compute(double maximumWidth)
        => WeatherBarLayoutPolicy.Compute(
            maximumWidth,
            horizontalMargins: 20,
            itemSpacing: 8,
            iconWidth: 24,
            temperatureWidth: 40,
            AllFields);
}
