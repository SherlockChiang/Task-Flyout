using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class WindowsWidgetsAvailabilityPolicyTests
{
    [Fact]
    public void Unsupported_build_is_rejected_even_when_other_components_exist()
    {
        var result = Resolve(build: false, package: true, taskbar: true, entryPoint: true);

        Assert.False(result.IsAvailable);
        Assert.Equal(WindowsWidgetsAvailabilityReason.UnsupportedWindowsBuild, result.Reason);
    }

    [Fact]
    public void Missing_web_experience_pack_is_not_native_widgets()
    {
        var result = Resolve(build: true, package: false, taskbar: true, entryPoint: false);

        Assert.False(result.IsAvailable);
        Assert.Equal(WindowsWidgetsAvailabilityReason.WebExperiencePackMissing, result.Reason);
    }

    [Fact]
    public void Explorer_must_have_a_taskbar_before_native_mode_owns_the_surface()
    {
        var result = Resolve(build: true, package: true, taskbar: false, entryPoint: false);

        Assert.False(result.IsAvailable);
        Assert.Equal(WindowsWidgetsAvailabilityReason.TaskbarUnavailable, result.Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Registered_pack_and_taskbar_are_available_while_entry_materializes(bool entryPoint)
    {
        var result = Resolve(build: true, package: true, taskbar: true, entryPoint);

        Assert.True(result.IsAvailable);
        Assert.Equal(WindowsWidgetsAvailabilityReason.Available, result.Reason);
        Assert.Equal(entryPoint, result.NativeEntryPointPresent);
    }

    [Fact]
    public void Live_detector_returns_a_coherent_availability_result()
    {
        var result = WindowsWidgetsService.Detect();

        Assert.Equal(
            result.IsAvailable,
            result.Reason == WindowsWidgetsAvailabilityReason.Available);
    }

    private static WindowsWidgetsAvailability Resolve(
        bool build,
        bool package,
        bool taskbar,
        bool entryPoint)
        => WindowsWidgetsAvailabilityPolicy.Resolve(
            new WindowsWidgetsAvailabilitySignals(build, package, taskbar, entryPoint));
}
