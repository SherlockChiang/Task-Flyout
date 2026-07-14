using System;
using System.IO;

namespace Task_Flyout.Services
{
    internal static class DiagnosticLogRetention
    {
        public const long DefaultMaximumBytes = 2L * 1024 * 1024;

        public static void RotateIfNeeded(string path, long maximumBytes = DefaultMaximumBytes)
        {
            if (maximumBytes < 1 || !File.Exists(path)) return;

            var info = new FileInfo(path);
            if (info.Length < maximumBytes) return;

            string backupPath = path + ".1";
            File.Move(path, backupPath, overwrite: true);
        }
    }
}
