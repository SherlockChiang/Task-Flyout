using System;
using System.IO;

namespace Task_Flyout.Services
{
    internal static class ProviderAuthCleanup
    {
        public static string AppDataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskFlyout");

        public static string GoogleLegacyTokenPath => Path.Combine(AppDataRoot, "GoogleToken");

        public static void DeleteGoogleLegacyTokenStore(
            string tokenPath,
            Func<string, bool>? directoryExists = null,
            Action<string, bool>? deleteDirectory = null)
        {
            directoryExists ??= Directory.Exists;
            deleteDirectory ??= Directory.Delete;

            if (directoryExists(tokenPath))
                deleteDirectory(tokenPath, true);
        }

        public static string MicrosoftAuthRecordPath
        {
            get
            {
                Directory.CreateDirectory(AppDataRoot);
                return Path.Combine(AppDataRoot, "ms_auth_record.bin");
            }
        }

        public static string MicrosoftTokenCacheDirectory
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ".IdentityService");

        public static System.Collections.Generic.IReadOnlyList<string> MicrosoftTokenCacheNames { get; } = new[]
        {
            "TaskFlyout_MSAL_Cache.nocae",
            "TaskFlyout_MSAL_Cache.cae"
        };

        public static bool SupportsProvider(string providerName)
            => providerName.Equals("Google", StringComparison.OrdinalIgnoreCase) ||
               providerName.Equals("Microsoft", StringComparison.OrdinalIgnoreCase);
    }
}
