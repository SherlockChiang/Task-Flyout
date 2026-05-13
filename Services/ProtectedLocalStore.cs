using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Task_Flyout.Services
{
    public static class ProtectedLocalStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TaskFlyout.LocalCache.v1");

        public static string? ReadText(string path)
        {
            if (!File.Exists(path)) return null;

            var bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0) return "";

            try
            {
                var decrypted = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch (CryptographicException)
            {
                try
                {
                    return File.ReadAllText(path);
                }
                catch
                {
                    return null;
                }
            }
        }

        public static async Task WriteTextAsync(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var bytes = Encoding.UTF8.GetBytes(text ?? "");
            var encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            await File.WriteAllBytesAsync(path, encrypted);
        }

        public static void WriteText(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var bytes = Encoding.UTF8.GetBytes(text ?? "");
            var encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(path, encrypted);
        }
    }
}
