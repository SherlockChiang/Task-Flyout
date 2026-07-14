using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class OnboardingPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void Shows_when_current_version_was_not_completed(object? completedVersion)
    {
        Assert.True(OnboardingPolicy.ShouldShow(completedVersion, null));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Hides_when_current_or_newer_version_was_completed(int completedVersion)
    {
        Assert.False(OnboardingPolicy.ShouldShow(completedVersion, null));
    }

    [Fact]
    public void Honors_legacy_completed_state_for_existing_users()
    {
        Assert.False(OnboardingPolicy.ShouldShow(null, true));
    }

    [Fact]
    public void Does_not_treat_legacy_incomplete_state_as_completed()
    {
        Assert.True(OnboardingPolicy.ShouldShow(null, false));
    }
}
