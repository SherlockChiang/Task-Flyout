using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class StandaloneTaskbarLaunchPolicyTests
{
    [Theory]
    [InlineData("----AppNotificationActivated:action=smoke", null, true)]
    [InlineData("action=smoke", null, true)]
    [InlineData("action=openWeather", null, false)]
    [InlineData("action=smoke;invalid=true", null, false)]
    [InlineData(null, "1", true)]
    [InlineData("action=openWeather", " 1 ", true)]
    [InlineData("action=smoke", "0", true)]
    [InlineData(null, null, false)]
    public void Suppression_is_limited_to_smoke_or_the_explicit_kill_switch(
        string? activationArgument,
        string? environmentValue,
        bool expected)
        => Assert.Equal(
            expected,
            StandaloneTaskbarLaunchPolicy.IsSuppressed(
                activationArgument,
                environmentValue));
}
