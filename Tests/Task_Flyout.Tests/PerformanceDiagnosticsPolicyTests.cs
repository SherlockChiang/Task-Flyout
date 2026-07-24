using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public sealed class PerformanceDiagnosticsPolicyTests
{
    [Fact]
    public void TryClaim_IsAtomicAcrossConcurrentCallers()
    {
        var policy = new PerformanceOncePolicy();
        int winners = 0;

        Parallel.For(0, 1000, _ =>
        {
            if (policy.TryClaim("first")) Interlocked.Increment(ref winners);
        });

        Assert.Equal(1, winners);
        Assert.True(policy.IsClaimed("first"));
    }

    [Fact]
    public void TryClaim_IsIdempotentPerKey()
    {
        var policy = new PerformanceOncePolicy();

        Assert.True(policy.TryClaim("one"));
        Assert.False(policy.TryClaim("one"));
        Assert.True(policy.TryClaim("two"));
    }

    [Fact]
    public void NearestRank_ReturnsP50AndP95()
    {
        var values = Enumerable.Range(1, 20).Select(value => (double)value);

        Assert.Equal(10, PerformancePercentilePolicy.NearestRank(values, 50));
        Assert.Equal(19, PerformancePercentilePolicy.NearestRank(values, 95, minimumSamples: 20));
    }

    [Fact]
    public void P95_IsMissingBelowTwentySamples()
    {
        var values = Enumerable.Range(1, 19).Select(value => (double)value);

        Assert.Null(PerformancePercentilePolicy.NearestRank(values, 95, minimumSamples: 20));
    }
}
