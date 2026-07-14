using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class DiagnosticLogRetentionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "TaskFlyoutLogRetentionTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Rotates_log_at_size_limit_and_replaces_backup()
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, "diagnostics.csv");
        File.WriteAllText(path + ".1", "old backup");
        File.WriteAllText(path, "1234567890");

        DiagnosticLogRetention.RotateIfNeeded(path, 10);

        Assert.False(File.Exists(path));
        Assert.Equal("1234567890", File.ReadAllText(path + ".1"));
    }

    [Fact]
    public void Keeps_log_below_size_limit()
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, "diagnostics.csv");
        File.WriteAllText(path, "small");

        DiagnosticLogRetention.RotateIfNeeded(path, 10);

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".1"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }
}
