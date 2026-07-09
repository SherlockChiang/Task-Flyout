using System;
using System.IO;

namespace Task_Flyout.Services
{
    internal static class ProviderAuthCleanup
    {
        public static string AppDataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskFlyout");

        public static string GoogleLegacyTokenPath => Path.Combine(AppDataRoot, "GoogleToken");

        public static string MicrosoftAuthRecordPath
        {
            get
            {
                Directory.CreateDirectory(AppDataRoot);
                return Path.Combine(AppDataRoot, "ms_auth_record.bin");
            }
        }

        public static bool SupportsProvider(string providerName)
            => providerName.Equals("Google", StringComparison.OrdinalIgnoreCase) ||
               providerName.Equals("Microsoft", StringComparison.OrdinalIgnoreCase);
    }
}
