using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class WeatherBarRecoveryPolicyTests
{
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);

    [Fact]
    public void Disabled_bar_stops_recovery()
        => Assert.Equal(WeatherBarRecoveryAction.Stop, Decide(enabled: false));

    [Fact]
    public void Missing_taskbar_waits_without_constructing_a_window()
        => Assert.Equal(
            WeatherBarRecoveryAction.WaitForTaskbar,
            Decide(taskbarAvailable: false, barExists: false, barAlive: false));

    [Fact]
    public void Dead_bar_is_discarded_while_waiting_for_taskbar()
        => Assert.Equal(
            WeatherBarRecoveryAction.DiscardAndWaitForTaskbar,
            Decide(taskbarAvailable: false, barExists: true, barAlive: false));

    [Fact]
    public void Attached_live_bar_is_healthy()
        => Assert.Equal(
            WeatherBarRecoveryAction.Healthy,
            Decide(barExists: true, barAlive: true, attached: true));

    [Fact]
    public void Detached_live_bar_gets_one_reattach_attempt()
        => Assert.Equal(
            WeatherBarRecoveryAction.Reattach,
            Decide(barExists: true, barAlive: true, attached: false, reattachAttempted: false));

    [Fact]
    public void Failed_reattach_recreates_after_cooldown()
        => Assert.Equal(
            WeatherBarRecoveryAction.Recreate,
            Decide(
                barExists: true,
                barAlive: true,
                attached: false,
                reattachAttempted: true,
                elapsed: Cooldown));

    [Fact]
    public void Recent_recreation_throttles_a_failed_reattach()
        => Assert.Equal(
            WeatherBarRecoveryAction.Cooldown,
            Decide(
                barExists: true,
                barAlive: true,
                attached: false,
                reattachAttempted: true,
                elapsed: TimeSpan.FromSeconds(2)));

    [Fact]
    public void Dead_bar_recreates_immediately_when_not_throttled()
        => Assert.Equal(
            WeatherBarRecoveryAction.Recreate,
            Decide(barExists: true, barAlive: false, elapsed: TimeSpan.MaxValue));

    [Fact]
    public void Missing_bar_respects_recreation_cooldown()
        => Assert.Equal(
            WeatherBarRecoveryAction.Cooldown,
            Decide(barExists: false, barAlive: false, elapsed: TimeSpan.FromSeconds(2)));

    [Fact]
    public void Negative_elapsed_time_cannot_bypass_cooldown()
        => Assert.Equal(
            WeatherBarRecoveryAction.Cooldown,
            Decide(barExists: false, barAlive: false, elapsed: TimeSpan.FromSeconds(-1)));

    private static WeatherBarRecoveryAction Decide(
        bool enabled = true,
        bool taskbarAvailable = true,
        bool barExists = true,
        bool barAlive = true,
        bool attached = false,
        bool reattachAttempted = false,
        TimeSpan? elapsed = null)
        => WeatherBarRecoveryPolicy.Decide(
            enabled,
            taskbarAvailable,
            barExists,
            barAlive,
            attached,
            reattachAttempted,
            elapsed ?? TimeSpan.MaxValue,
            Cooldown);
}
