using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public sealed class PerformanceOperationTests
{
    [Theory]
    [InlineData(-3600)]
    [InlineData(3600)]
    public void Elapsed_time_ignores_wall_clock_jumps(int jumpSeconds)
    {
        var samples = new List<PerformanceSample>();
        var clock = new ManualClock();
        using var operation = Create(samples, clock);
        clock.Ticks += TimeSpan.FromMilliseconds(37.5).Ticks;
        clock.Utc += TimeSpan.FromSeconds(jumpSeconds);
        operation.Complete();

        Assert.Equal(2, samples.Count);
        Assert.Equal("started", samples[0].Outcome);
        Assert.Equal(37.5, samples[1].Duration);
        Assert.Equal("monotonic", samples[1].TimingBasis);
        Assert.Equal(jumpSeconds, (samples[1].End - samples[1].Start).TotalSeconds);
    }

    [Fact]
    public void Checkpoints_share_identity_and_elapsed_origin_and_are_written_once()
    {
        var samples = new List<PerformanceSample>();
        var clock = new ManualClock();
        using var operation = Create(samples, clock);
        clock.Ticks += TimeSpan.FromMilliseconds(10).Ticks;
        operation.Checkpoint("cached_content_applied");
        operation.Checkpoint("cached_content_applied");
        clock.Ticks += TimeSpan.FromMilliseconds(15).Ticks;
        operation.Complete();
        operation.Checkpoint("late_rendering");

        Assert.Equal(new[] { 0d, 10d, 25d }, samples.Select(sample => sample.Duration));
        Assert.Single(samples.Select(sample => sample.OperationId).Distinct());
        Assert.Single(samples.Select(sample => sample.Start).Distinct());
        Assert.All(samples, sample => Assert.Equal("cold_attempt", sample.SampleKind));
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("superseded")]
    [InlineData("failure")]
    [InlineData("ignored")]
    [InlineData("not_opening")]
    public void Terminal_outcome_rejects_late_success_and_checkpoints(string outcome)
    {
        var samples = new List<PerformanceSample>();
        using var operation = Create(samples);
        operation.Complete(outcome);
        operation.Complete();
        operation.Checkpoint("late_rendering");
        Assert.Equal(new[] { "started", outcome }, samples.Select(sample => sample.Outcome));
    }

    [Fact]
    public void Dispose_records_cancellation()
    {
        var samples = new List<PerformanceSample>();
        Create(samples).Dispose();
        Assert.Equal("cancelled", samples[1].Outcome);
    }

    [Fact]
    public void Concurrent_completion_writes_one_terminal_record()
    {
        var samples = new List<PerformanceSample>();
        using var operation = Create(samples);
        Parallel.For(0, 1000, i => operation.Complete(i % 2 == 0 ? "cancelled" : "success"));
        Assert.Equal(2, samples.Count);
    }

    [Fact]
    public void Separate_operations_have_different_ids()
    {
        var samples = new List<PerformanceSample>();
        using var first = Create(samples);
        using var second = Create(samples);
        Assert.NotEqual(samples[0].OperationId, samples[1].OperationId);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("unavailable")]
    [InlineData("not_observed")]
    [InlineData("cancelled")]
    public void Unobserved_checkpoint_is_not_overwritten_by_late_success(string outcome)
    {
        var samples = new List<PerformanceSample>();
        using var operation = Create(samples);
        operation.Checkpoint("host_visible", outcome);
        operation.Checkpoint("host_visible");
        operation.Complete();
        Assert.Equal(outcome, samples.Single(sample => sample.Metric == "host_visible").Outcome);
    }

    private static PerformanceOperation Create(List<PerformanceSample> samples, TimeProvider? clock = null)
        => new("flyout_open", "command_to_rendering", "tray", "cold_attempt", samples.Add, clock);

    private sealed class ManualClock : TimeProvider
    {
        public long Ticks;
        public DateTimeOffset Utc = new(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Ticks;
        public override DateTimeOffset GetUtcNow() => Utc;
    }
}
