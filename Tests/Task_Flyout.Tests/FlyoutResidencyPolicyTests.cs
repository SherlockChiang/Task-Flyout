using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class FlyoutResidencyPolicyTests
{
    [Fact]
    public void Explicit_prewarm_runs_when_memory_has_headroom()
        => Assert.True(FlyoutResidencyPolicy.ShouldPrewarm(
            configured: true,
            underMemoryPressure: false,
            currentUsageBytes: 320L * 1024 * 1024,
            usageLimitBytes: 4L * 1024 * 1024 * 1024));

    [Theory]
    [InlineData(false, false, 320L, 4096L)]
    [InlineData(null, false, 320L, 4096L)]
    [InlineData(null, true, 320L, 4096L)]
    [InlineData(null, false, 700L, 4096L)]
    [InlineData(true, false, 500L, 1000L)]
    public void Avoids_prewarm_when_disabled_or_memory_is_constrained(
        bool? configured,
        bool underMemoryPressure,
        long usageMb,
        long limitMb)
        => Assert.False(FlyoutResidencyPolicy.ShouldPrewarm(
            configured,
            underMemoryPressure,
            usageMb * 1024 * 1024,
            limitMb * 1024 * 1024));

    [Theory]
    [InlineData(-1L, 4096L)]
    [InlineData(320L, 0L)]
    [InlineData(1200L, 1000L)]
    public void Invalid_memory_readings_fail_closed(long usageMb, long limitMb)
        => Assert.False(FlyoutResidencyPolicy.ShouldPrewarm(
            configured: true,
            underMemoryPressure: false,
            usageMb * 1024 * 1024,
            limitMb * 1024 * 1024));
}
