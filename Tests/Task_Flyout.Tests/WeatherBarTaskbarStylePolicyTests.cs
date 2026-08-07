using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class WeatherBarTaskbarStylePolicyTests
{
    [Theory]
    [InlineData(false, WeatherBarTaskbarStylePolicy.LuminosityDockTheme)]
    [InlineData(true, "DockLike")]
    [InlineData(true, null)]
    public void Non_matching_configuration_preserves_system_layout(bool enabled, string? theme)
    {
        var profile = WeatherBarTaskbarStylePolicy.Resolve(enabled, theme, null);

        Assert.Equal(WeatherBarTaskbarStyleProfile.SystemDefault, profile);
    }

    [Fact]
    public void Resolves_luminosity_dock_defaults()
    {
        var profile = WeatherBarTaskbarStylePolicy.Resolve(
            true,
            WeatherBarTaskbarStylePolicy.LuminosityDockTheme,
            new[] { "" });

        Assert.True(profile.MatchesTaskbarSurface);
        Assert.Equal(250, profile.LeftInset);
        Assert.Equal(750, profile.RightInset);
        Assert.Equal(58, profile.DockHeight);
        Assert.Equal(5, profile.TopInset);
        Assert.Equal(5, profile.BottomInset);
        Assert.Equal(10, profile.CornerRadius);
    }

    [Fact]
    public void Applies_valid_style_constant_overrides()
    {
        var profile = WeatherBarTaskbarStylePolicy.Resolve(
            true,
            WeatherBarTaskbarStylePolicy.LuminosityDockTheme,
            new[] { "DockMargin = 180", "DockMarginFix=420", "DockHeight=64", "DockTopGap=7", "bcr=12.5" });

        Assert.Equal(180, profile.LeftInset);
        Assert.Equal(600, profile.RightInset);
        Assert.Equal(64, profile.DockHeight);
        Assert.Equal(7, profile.TopInset);
        Assert.Equal(12.5, profile.CornerRadius);
    }

    [Fact]
    public void Invalid_overrides_fall_back_independently()
    {
        var profile = WeatherBarTaskbarStylePolicy.Resolve(
            true,
            WeatherBarTaskbarStylePolicy.LuminosityDockTheme,
            new[] { "DockMargin=-1", "DockHeight=oops", "DockTopGap=200", "bcr=NaN" });

        Assert.Equal(250, profile.LeftInset);
        Assert.Equal(750, profile.RightInset);
        Assert.Equal(58, profile.DockHeight);
        Assert.Equal(5, profile.TopInset);
        Assert.Equal(10, profile.CornerRadius);
    }

    [Fact]
    public void Matches_windhawk_constant_names_and_termination_rules()
    {
        var profile = WeatherBarTaskbarStylePolicy.Resolve(
            true,
            WeatherBarTaskbarStylePolicy.LuminosityDockTheme,
            new[] { "dockmargin=100", "$DockHeight=80", "", "DockMargin=120" });

        Assert.Equal(250, profile.LeftInset);
        Assert.Equal(58, profile.DockHeight);
    }

    [Fact]
    public void Maps_current_primary_taskbar_to_luminosity_left_reserve()
    {
        var profile = WeatherBarTaskbarStylePolicy.Resolve(
            true,
            WeatherBarTaskbarStylePolicy.LuminosityDockTheme,
            null);

        var slot = WeatherBarTaskbarStylePolicy.GetSlot(profile, 1920, 58, 1);

        Assert.True(slot.IsThemed);
        Assert.Equal(0, slot.Left);
        Assert.Equal(250, slot.Right);
        Assert.Equal(250, slot.Width);
        Assert.Equal(5, slot.Top);
        Assert.Equal(48, slot.Height);
    }

    [Fact]
    public void Custom_dock_margin_sets_the_left_reserve_width()
    {
        var profile = WeatherBarTaskbarStylePolicy.Resolve(
            true,
            WeatherBarTaskbarStylePolicy.LuminosityDockTheme,
            new[] { "DockMargin=180", "DockMarginFix=360" });

        var slot = WeatherBarTaskbarStylePolicy.GetSlot(profile, 1920, 58, 1);

        Assert.True(slot.IsThemed);
        Assert.Equal(0, slot.Left);
        Assert.Equal(180, slot.Right);
        Assert.Equal(180, slot.Width);
    }

    [Fact]
    public void Full_width_dock_without_a_left_reserve_uses_native_fallback()
    {
        var profile = WeatherBarTaskbarStylePolicy.Resolve(
            true,
            WeatherBarTaskbarStylePolicy.LuminosityDockTheme,
            new[] { "DockMargin=0", "DockMarginFix=0" });

        var slot = WeatherBarTaskbarStylePolicy.GetSlot(profile, 1920, 58, 1);

        Assert.False(slot.IsThemed);
        Assert.Equal(0, slot.Left);
        Assert.Equal(1920, slot.Right);
    }

    [Fact]
    public void Rejects_vertical_overrides_that_collapse_the_visible_slot()
    {
        var profile = WeatherBarTaskbarStylePolicy.Resolve(
            true,
            WeatherBarTaskbarStylePolicy.LuminosityDockTheme,
            new[] { "DockHeight=32", "DockTopGap=20" });

        Assert.Equal(58, profile.DockHeight);
        Assert.Equal(5, profile.TopInset);
    }

    [Fact]
    public void Maps_current_200_percent_taskbar_to_visible_left_reserve()
    {
        var profile = WeatherBarTaskbarStylePolicy.Resolve(
            true,
            WeatherBarTaskbarStylePolicy.LuminosityDockTheme,
            null);

        var slot = WeatherBarTaskbarStylePolicy.GetSlot(profile, 3840, 116, 2);

        Assert.True(slot.IsThemed);
        Assert.Equal(0, slot.Left);
        Assert.Equal(500, slot.Right);
        Assert.Equal(500, slot.Width);
        Assert.Equal(10, slot.Top);
        Assert.Equal(96, slot.Height);
    }

    [Fact]
    public void Narrow_scaled_reserve_uses_native_fallback()
    {
        var profile = WeatherBarTaskbarStylePolicy.Resolve(
            true,
            WeatherBarTaskbarStylePolicy.LuminosityDockTheme,
            null);

        var slot = WeatherBarTaskbarStylePolicy.GetSlot(profile, 500, 116, 2);

        Assert.False(slot.IsThemed);
        Assert.Equal(0, slot.Left);
        Assert.Equal(500, slot.Right);
        Assert.Equal(500, slot.Width);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void Invalid_scale_uses_unmodified_taskbar_bounds(double scale)
    {
        var profile = WeatherBarTaskbarStylePolicy.Resolve(
            true,
            WeatherBarTaskbarStylePolicy.LuminosityDockTheme,
            null);

        var slot = WeatherBarTaskbarStylePolicy.GetSlot(profile, 1920, 58, scale);

        Assert.False(slot.IsThemed);
        Assert.Equal(0, slot.Left);
        Assert.Equal(1920, slot.Right);
        Assert.Equal(58, slot.Height);
    }
}
