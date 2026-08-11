using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class StandaloneTaskbarUiStatusPolicyTests
{
    [Theory]
    [InlineData(false, true, true, "ControllerActiveUnverified", false, null, "Off")]
    [InlineData(true, false, true, "ControllerActiveUnverified", false, null, "WeatherBarDisabled")]
    [InlineData(true, true, false, "ControllerActiveUnverified", false, null, "WeatherProviderDisabled")]
    [InlineData(true, true, true, "Starting", true, null, "Preparing")]
    [InlineData(true, true, true, "ControllerActiveUnverified", true, null, "ActiveUnverified")]
    [InlineData(true, true, true, "BinaryMissing", false, null, "BinaryMissing")]
    [InlineData(true, true, true, "Unsupported", false, null, "Unsupported")]
    [InlineData(true, true, true, "TemporarilyUnavailable", false, null, "TemporarilyUnavailable")]
    [InlineData(true, true, true, "Rejected", false, null, "Failed")]
    [InlineData(true, true, true, "Stopping", false, null, "Stopping")]
    [InlineData(true, true, true, "Disabled", false, null, "Fallback")]
    public void Runtime_states_map_to_localizable_status_kinds(
        bool requested,
        bool weatherBarEnabled,
        bool providerEnabled,
        string runtimeState,
        bool pipeActive,
        string? detail,
        string expected)
        => Assert.Equal(
            Enum.Parse<StandaloneTaskbarUiStatusKind>(expected),
            StandaloneTaskbarUiStatusPolicy.Resolve(
                requested,
                weatherBarEnabled,
                providerEnabled,
                Enum.Parse<StandaloneTaskbarRuntimeState>(runtimeState),
                pipeActive,
                detail));

    [Theory]
    [InlineData("standalone:waiting-for-windows-widgets-removal", "Preparing")]
    [InlineData("standalone:windows-widgets-settings-failure", "Preparing")]
    [InlineData("standalone:windows-widgets-external-change-preserved", "WidgetsConflict")]
    [InlineData("standalone:windows-widgets-invalid-snapshot", "WidgetsConflict")]
    [InlineData("standalone:taskbar-control-suppressed", "ControlSuppressed")]
    public void Suppression_details_take_priority_over_controller_state(
        string detail,
        string expected)
        => Assert.Equal(
            Enum.Parse<StandaloneTaskbarUiStatusKind>(expected),
            StandaloneTaskbarUiStatusPolicy.Resolve(
                standaloneModeRequested: true,
                weatherBarEnabled: true,
                weatherProviderEnabled: true,
                StandaloneTaskbarRuntimeState.Disabled,
                companionPipeActive: false,
                detail));
}
