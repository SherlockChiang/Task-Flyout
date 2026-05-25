using System;
using System.IO;
using System.Linq;

namespace Task_Flyout.Services
{
    public static class AppDataPathHelper
    {
        private const string AppFolderName = "TaskFlyout";

        public static string RoamingRoot =>
            EnsureDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName));

        public static string LocalRoot =>
            EnsureDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName));

        public static string ResolveRoaming(params string[] relativeSegments)
            => ResolveUnderRoot(RoamingRoot, relativeSegments);

        public static string ResolveLocal(params string[] relativeSegments)
            => ResolveUnderRoot(LocalRoot, relativeSegments);

        public static string EnsureDirectory(string path)
        {
            Directory.CreateDirectory(path);
            return path;
        }

        public static string ResolveUnderRoot(string root, params string[] relativeSegments)
        {
            var fullRoot = Path.GetFullPath(root);
            var combined = relativeSegments.Aggregate(fullRoot, Path.Combine);
            var fullPath = Path.GetFullPath(combined);
            var rootWithSeparator = fullRoot.EndsWith(Path.DirectorySeparatorChar)
                ? fullRoot
                : fullRoot + Path.DirectorySeparatorChar;

            if (!fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) &&
                !fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Resolved path escapes the application data directory.");

            return fullPath;
        }
    }
}
