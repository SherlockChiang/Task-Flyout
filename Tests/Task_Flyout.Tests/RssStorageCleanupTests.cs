using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class RssStorageCleanupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "TaskFlyoutRssCleanupTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Delete_all_removes_database_sidecars_legacy_cache_and_images()
    {
        var paths = CreateStorageFiles();

        RssStorageCleanup.DeleteAll(paths.Database, paths.Legacy, paths.Images);

        Assert.False(File.Exists(paths.Database));
        Assert.False(File.Exists(paths.Database + "-wal"));
        Assert.False(File.Exists(paths.Database + "-shm"));
        Assert.False(File.Exists(paths.Legacy));
        Assert.False(Directory.Exists(paths.Images));
    }

    [Fact]
    public void Delete_all_attempts_every_path_before_reporting_failures()
    {
        var paths = CreateStorageFiles();

        var exception = Assert.Throws<IOException>(() => RssStorageCleanup.DeleteAll(
            paths.Database,
            paths.Legacy,
            paths.Images,
            path =>
            {
                if (path == paths.Database)
                    throw new UnauthorizedAccessException("injected failure");
                File.Delete(path);
            }));

        Assert.Contains("could not be removed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(paths.Database));
        Assert.False(File.Exists(paths.Database + "-wal"));
        Assert.False(File.Exists(paths.Database + "-shm"));
        Assert.False(File.Exists(paths.Legacy));
        Assert.False(Directory.Exists(paths.Images));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private (string Database, string Legacy, string Images) CreateStorageFiles()
    {
        Directory.CreateDirectory(_root);
        var database = Path.Combine(_root, "rss.db");
        var legacy = Path.Combine(_root, "rss.json");
        var images = Path.Combine(_root, "images");
        Directory.CreateDirectory(images);
        File.WriteAllText(database, "database");
        File.WriteAllText(database + "-wal", "wal");
        File.WriteAllText(database + "-shm", "shm");
        File.WriteAllText(legacy, "legacy");
        File.WriteAllText(Path.Combine(images, "image.bin"), "image");
        return (database, legacy, images);
    }
}
