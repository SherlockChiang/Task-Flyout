using System.Text;
using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class ProtectedLocalStoreTests
{
    [Fact]
    public async Task Versioned_store_round_trips_with_envelope_and_atomic_async_write()
    {
        var (root, path) = CreateStorePath();
        try
        {
            await ProtectedLocalStore.WriteTextAsync(path, "user.tenant");

            var bytes = File.ReadAllBytes(path);
            Assert.Equal("TFPS", Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.Equal(1, bytes[4]);
            Assert.DoesNotContain("user.tenant", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
            Assert.Equal("user.tenant", ProtectedLocalStore.ReadText(path));
        }
        finally
        {
            DeleteStore(root);
        }
    }

    [Fact]
    public void Read_migrates_unversioned_dpapi_ciphertext_to_current_format()
    {
        var (root, path) = CreateStorePath();
        try
        {
            ProtectedLocalStore.WriteText(path, "user.tenant");
            var current = File.ReadAllBytes(path);
            File.WriteAllBytes(path, current[5..]);

            Assert.Equal("user.tenant", ProtectedLocalStore.ReadText(path));
            Assert.Equal("TFPS", Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 4));
        }
        finally
        {
            DeleteStore(root);
        }
    }

    [Fact]
    public void Read_migrates_only_recognized_legacy_msal_plaintext()
    {
        var (root, path) = CreateStorePath();
        try
        {
            File.WriteAllText(path, "user.tenant");

            Assert.Equal("user.tenant", ProtectedLocalStore.ReadText(path));
            Assert.Equal("TFPS", Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 4));
        }
        finally
        {
            DeleteStore(root);
        }
    }

    [Fact]
    public void Read_leaves_unrecognized_plaintext_unchanged()
    {
        var (root, path) = CreateStorePath();
        try
        {
            var original = Encoding.UTF8.GetBytes("legacy account");
            File.WriteAllBytes(path, original);

            Assert.Null(ProtectedLocalStore.ReadText(path));
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally
        {
            DeleteStore(root);
        }
    }

    [Fact]
    public void Read_leaves_corrupt_ciphertext_unchanged()
    {
        var (root, path) = CreateStorePath();
        try
        {
            var original = new byte[] { 0xff, 0x00, 0x81, 0x19, 0x02 };
            File.WriteAllBytes(path, original);

            Assert.Null(ProtectedLocalStore.ReadText(path));
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally
        {
            DeleteStore(root);
        }
    }

    [Fact]
    public void Read_leaves_unknown_envelope_version_unchanged()
    {
        var (root, path) = CreateStorePath();
        try
        {
            var original = new byte[] { (byte)'T', (byte)'F', (byte)'P', (byte)'S', 0x63, 0x63, 0x6f, 0x72, 0x72, 0x75, 0x70, 0x74 };
            File.WriteAllBytes(path, original);

            Assert.Null(ProtectedLocalStore.ReadText(path));
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally
        {
            DeleteStore(root);
        }
    }

    private static (string Root, string Path) CreateStorePath()
    {
        var root = System.IO.Path.Combine(Path.GetTempPath(), "taskflyout-protected-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return (root, System.IO.Path.Combine(root, "ms_auth_record.bin"));
    }

    private static void DeleteStore(string root)
        => Directory.Delete(root, recursive: true);
}
