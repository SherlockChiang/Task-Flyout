using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class VersionedAsyncWriterTests
{
    [Fact]
    public async Task Newer_snapshot_remains_persisted_when_older_snapshot_arrives_late()
    {
        var writes = new List<string>();
        var writer = new VersionedAsyncWriter<string>(value =>
        {
            writes.Add(value);
            return Task.CompletedTask;
        });

        await writer.WriteAsync(2, "newer");
        await writer.WriteAsync(1, "older");

        Assert.Equal(new[] { "newer" }, writes);
    }

    [Fact]
    public async Task Concurrent_snapshots_are_written_in_gate_order_and_end_with_newest_version()
    {
        var firstWriteStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = new List<string>();
        var writer = new VersionedAsyncWriter<string>(async value =>
        {
            writes.Add(value);
            if (value == "older")
            {
                firstWriteStarted.SetResult();
                await releaseFirstWrite.Task;
            }
        });

        var olderWrite = writer.WriteAsync(1, "older");
        await firstWriteStarted.Task;
        var newerWrite = writer.WriteAsync(2, "newer");

        releaseFirstWrite.SetResult();
        await Task.WhenAll(olderWrite, newerWrite);

        Assert.Equal(new[] { "older", "newer" }, writes);
    }

    [Fact]
    public async Task Failed_write_does_not_mark_version_as_persisted()
    {
        int attempts = 0;
        var writer = new VersionedAsyncWriter<string>(_ =>
        {
            attempts++;
            return attempts == 1
                ? Task.FromException(new IOException("Disk unavailable."))
                : Task.CompletedTask;
        });

        await Assert.ThrowsAsync<IOException>(() => writer.WriteAsync(1, "snapshot"));
        await writer.WriteAsync(1, "snapshot");

        Assert.Equal(2, attempts);
    }
}
