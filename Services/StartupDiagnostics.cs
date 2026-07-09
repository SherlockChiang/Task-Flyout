using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Task_Flyout.Services
{
    internal sealed class StartupDiagnostics
    {
        private static readonly object LogLock = new();
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly List<(string Name, long ElapsedMs)> _marks = new();
        private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;

        public static StartupDiagnostics Start() => new();

        public void Mark(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            _marks.Add((name.Trim(), _stopwatch.ElapsedMilliseconds));
        }

        public Task FlushAsync()
        {
            _stopwatch.Stop();
            var startedAt = _startedAt;
            var totalMs = _stopwatch.ElapsedMilliseconds;
            var marks = _marks.ToArray();
            return Task.Run(() => AppendLog(startedAt, totalMs, marks));
        }

        private static void AppendLog(DateTimeOffset startedAt, long totalMs, IReadOnlyList<(string Name, long ElapsedMs)> marks)
        {
            try
            {
                var path = AppDataPathHelper.ResolveRoaming("Logs", "startup.csv");
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                    AppDataPathHelper.EnsureDirectory(dir);

                var markText = string.Join(';', marks.Select(mark => $"{mark.Name}={mark.ElapsedMs}"));
                lock (LogLock)
                {
                    bool writeHeader = !File.Exists(path);
                    using var writer = new StreamWriter(path, append: true);
                    if (writeHeader)
                        writer.WriteLine("timestamp,totalMs,marks");
                    writer.WriteLine($"{Csv(startedAt.ToString("O"))},{totalMs},{Csv(markText)}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Writing startup diagnostics failed: {ex.Message}");
            }
        }

        private static string Csv(string value)
            => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    }
}
