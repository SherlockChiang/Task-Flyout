using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Task_Flyout.Services
{
    public static class ProtectedLocalStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TaskFlyout.LocalCache.v1");
        private static readonly byte[] FormatMagic = Encoding.ASCII.GetBytes("TFPS");
        private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        private const byte CurrentFormatVersion = 1;
        private const int HeaderLength = 5;
        private const string LegacyMicrosoftAuthFileName = "ms_auth_record.bin";

        public static string? ReadText(string path)
        {
            if (!File.Exists(path)) return null;

            var bytes = File.ReadAllBytes(path);
            if (HasMagic(bytes))
            {
                if (bytes.Length <= HeaderLength || bytes[FormatMagic.Length] != CurrentFormatVersion)
                    return null;

                return TryUnprotectText(bytes.AsSpan(HeaderLength));
            }

            // Files written before the versioned envelope were still DPAPI protected.
            // Read them once, then rewrite them in the current format.
            var decrypted = TryUnprotectText(bytes);
            if (decrypted != null)
            {
                WriteText(path, decrypted);
                return decrypted;
            }

            // The only supported legacy plaintext is the old Microsoft MSAL account
            // record. Corrupt ciphertext must not be mistaken for arbitrary text.
            if (!TryReadRecognizedLegacyPlaintext(path, bytes, out var legacyText))
                return null;

            WriteText(path, legacyText);
            return legacyText;
        }

        public static Task WriteTextAsync(string path, string text)
            => WriteBytesAtomicallyAsync(path, CreateEnvelope(text ?? string.Empty));

        public static void WriteText(string path, string text)
            => WriteBytesAtomically(path, CreateEnvelope(text ?? string.Empty));

        private static byte[] CreateEnvelope(string text)
        {
            var plaintext = StrictUtf8.GetBytes(text);
            var encrypted = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
            var envelope = new byte[HeaderLength + encrypted.Length];
            Buffer.BlockCopy(FormatMagic, 0, envelope, 0, FormatMagic.Length);
            envelope[FormatMagic.Length] = CurrentFormatVersion;
            Buffer.BlockCopy(encrypted, 0, envelope, HeaderLength, encrypted.Length);
            return envelope;
        }

        private static string? TryUnprotectText(ReadOnlySpan<byte> encrypted)
        {
            if (encrypted.Length == 0) return null;

            try
            {
                var decrypted = ProtectedData.Unprotect(encrypted.ToArray(), Entropy, DataProtectionScope.CurrentUser);
                return StrictUtf8.GetString(decrypted);
            }
            catch (CryptographicException)
            {
                return null;
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
        }

        private static bool TryReadRecognizedLegacyPlaintext(
            string path,
            ReadOnlySpan<byte> bytes,
            out string text)
        {
            text = string.Empty;
            if (!string.Equals(Path.GetFileName(path), LegacyMicrosoftAuthFileName, StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                text = StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return false;
            }

            if (text.Length == 0) return true;
            if (text.Length > 512 || text.Trim() != text || text.Any(char.IsControl))
                return false;

            var separator = text.IndexOf('.');
            return separator > 0 && separator < text.Length - 1 &&
                   text.All(static character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_');
        }

        private static bool HasMagic(ReadOnlySpan<byte> bytes)
            => bytes.Length >= FormatMagic.Length && bytes[..FormatMagic.Length].SequenceEqual(FormatMagic);

        private static void WriteBytesAtomically(string path, byte[] bytes)
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath)!;
            Directory.CreateDirectory(directory);

            var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    options: FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(flushToDisk: true);
                }

                ReplaceAtomically(tempPath, fullPath);
            }
            finally
            {
                TryDelete(tempPath);
            }
        }

        private static async Task WriteBytesAtomicallyAsync(string path, byte[] bytes)
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath)!;
            Directory.CreateDirectory(directory);

            var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var stream = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    options: FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await stream.WriteAsync(bytes).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }

                ReplaceAtomically(tempPath, fullPath);
            }
            finally
            {
                TryDelete(tempPath);
            }
        }

        private static void ReplaceAtomically(string tempPath, string targetPath)
        {
            if (File.Exists(targetPath))
                File.Replace(tempPath, targetPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(tempPath, targetPath);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // Preserve the original write/replace exception.
            }
        }
    }
}
