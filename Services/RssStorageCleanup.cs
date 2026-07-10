using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;

namespace Task_Flyout.Services
{
    internal static class RssStorageCleanup
    {
        public static void DeleteAll(
            string databasePath,
            string legacyCachePath,
            string imageCachePath,
            Action<string>? deleteFile = null,
            Action<string>? deleteDirectory = null)
        {
            deleteFile ??= File.Delete;
            deleteDirectory ??= path => Directory.Delete(path, recursive: true);
            SqliteConnection.ClearAllPools();

            var errors = new List<Exception>();
            foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm", legacyCachePath })
            {
                try { deleteFile(path); }
                catch (Exception ex)
                {
                    errors.Add(new IOException($"RSS data file could not be removed: {path}", ex));
                }
            }

            try
            {
                if (Directory.Exists(imageCachePath))
                    deleteDirectory(imageCachePath);
            }
            catch (Exception ex)
            {
                errors.Add(new IOException($"RSS image cache could not be removed: {imageCachePath}", ex));
            }

            if (errors.Count > 0)
                throw new IOException("One or more RSS data files could not be removed.", new AggregateException(errors));
        }
    }
}
