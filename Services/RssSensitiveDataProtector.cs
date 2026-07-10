using System;
using System.Security.Cryptography;
using System.Text;

namespace Task_Flyout.Services
{
    internal static class RssSensitiveDataProtector
    {
        private const string ProtectedValuePrefix = "dpapi:v1:";
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TaskFlyout.RssSensitiveData.v1");

        public static bool IsProtected(string value)
            => value.StartsWith(ProtectedValuePrefix, StringComparison.Ordinal);

        public static string Protect(string? value)
        {
            value ??= "";
            if (IsProtected(value)) return value;

            var bytes = Encoding.UTF8.GetBytes(value);
            var encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return ProtectedValuePrefix + Convert.ToBase64String(encrypted);
        }

        public static string Unprotect(string value)
        {
            if (!IsProtected(value)) return value;

            try
            {
                var encrypted = Convert.FromBase64String(value[ProtectedValuePrefix.Length..]);
                var decrypted = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch
            {
                return "";
            }
        }
    }
}
