using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public sealed class BoundedDiagnosticWriterTests
{
    [Fact]
    public async Task Slow_sink_is_bounded_preserves_order_and_recovers_capacity()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var written = new List<(long Sequence, int Value)>();
        var writer = new BoundedDiagnosticWriter<int>((sequence, value) =>
        {
            if (value == 1)
            {
                entered.SetResult();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            }
            written.Add((sequence, value));
        }, capacity: 2);
        try
        {
            Assert.True(writer.TryEnqueue(1));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(writer.TryEnqueue(2));
            Assert.False(writer.TryEnqueue(3));
            Assert.Equal(1, writer.Dropped);
            Assert.False(writer.DrainAsync().IsCompleted);
        }
        finally { release.Set(); }
        await writer.DrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(writer.TryEnqueue(4));
        await writer.DrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { (1L, 1), (2L, 2), (4L, 4) }, written);
        Assert.Equal(0, writer.Failures);
    }

    [Fact]
    public async Task Failed_write_does_not_poison_later_writes_or_leak_capacity()
    {
        var written = new List<int>();
        var writer = new BoundedDiagnosticWriter<int>((_, value) =>
        {
            if (value == 1) throw new IOException("Simulated unavailable disk");
            written.Add(value);
        }, capacity: 1);
        Assert.True(writer.TryEnqueue(1));
        await writer.DrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(writer.TryEnqueue(2));
        await writer.DrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { 2 }, written);
        Assert.Equal(1, writer.Failures);
        Assert.Equal(0, writer.Dropped);
    }

    [Fact]
    public async Task Concurrent_producers_are_serialized()
    {
        var sequences = new List<long>();
        var writer = new BoundedDiagnosticWriter<int>((sequence, _) => sequences.Add(sequence), capacity: 1000);
        Parallel.For(0, 1000, value => writer.TryEnqueue(value));
        await writer.DrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(Enumerable.Range(1, 1000).Select(i => (long)i), sequences);
        Assert.Equal(0, writer.Dropped);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Capacity_must_be_positive(int capacity)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedDiagnosticWriter<int>((_, _) => { }, capacity));
}
