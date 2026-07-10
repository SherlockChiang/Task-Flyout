using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Task_Flyout.Services
{
    internal static class RssStorageCleanup
    {
        public static void DeleteAll(
            string databasePath,
            string legacyCachePath,
            string imageCachePath,
            Action<string>? deleteFile = null,
            Action<string>? deleteDirectory = null,
            Action<TimeSpan>? delay = null)
        {
            deleteFile ??= File.Delete;
            deleteDirectory ??= path => Directory.Delete(path, recursive: true);
            delay ??= Thread.Sleep;
            SqliteConnection.ClearAllPools();

            var errors = new List<Exception>();
            foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm", legacyCachePath })
            {
                try { ExecuteWithRetry(() => deleteFile(path), delay); }
                catch (Exception ex)
                {
                    errors.Add(new IOException($"RSS data file could not be removed: {path}", ex));
                }
            }

            try
            {
                if (Directory.Exists(imageCachePath))
                    ExecuteWithRetry(() => deleteDirectory(imageCachePath), delay);
            }
            catch (Exception ex)
            {
                errors.Add(new IOException($"RSS image cache could not be removed: {imageCachePath}", ex));
            }

            if (errors.Count > 0)
                throw new IOException("One or more RSS data files could not be removed.", new AggregateException(errors));
        }

        private static void ExecuteWithRetry(Action action, Action<TimeSpan> delay)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    action();
                    return;
                }
                catch (Exception ex) when (attempt < 2 && ex is IOException or UnauthorizedAccessException)
                {
                    delay(attempt == 0 ? TimeSpan.FromMilliseconds(50) : TimeSpan.FromMilliseconds(150));
                }
            }
        }
    }
}
