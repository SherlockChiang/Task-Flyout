using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Task_Flyout.Services
{
    internal static class PerformanceDiagnostics
    {
        internal const string Header = "run_id,sequence,timestamp,scenario,metric,start,end,duration,outcome,source";
        private static readonly PerformanceOncePolicy Once = new();
        private static readonly object WriteLock = new();
        private static readonly string RunId = Guid.NewGuid().ToString("N");
        private static readonly DateTimeOffset ProcessStartedAt = GetProcessStart();
        private static Task _pendingWrite = Task.CompletedTask;
        private static string? _path;
        private static long _sequence;
        private static int _enabled;

        public static bool IsEnabled => Volatile.Read(ref _enabled) != 0;

        public static void Initialize(bool enabled, string? path = null)
        {
            if (!enabled || Interlocked.Exchange(ref _enabled, 1) != 0) return;
            _path = path ?? AppDataPathHelper.ResolveLocal("Logs", "performance-diagnostics.csv");
        }

        public static void MarkOnce(string key, string scenario, string metric, string outcome = "success", string source = "app")
        {
            if (!IsEnabled || !Once.TryClaim(key)) return;
            var now = DateTimeOffset.UtcNow;
            Queue(new Entry(now, scenario, metric, now, now, outcome, source));
        }

        public static Span StartSpan(string scenario, string metric, string source = "app")
            => IsEnabled ? new Span(scenario, metric, source, DateTimeOffset.UtcNow) : Span.Disabled;

        public static Span StartSpanOnce(string key, string scenario, string metric, string source = "app")
            => IsEnabled && Once.TryClaim(key)
                ? new Span(scenario, metric, source, DateTimeOffset.UtcNow)
                : Span.Disabled;

        public static Span StartSpanUntilSuccess(string key, string scenario, string metric, string source = "app")
            => IsEnabled && !Once.IsClaimed(key)
                ? new Span(scenario, metric, source, DateTimeOffset.UtcNow, successKey: key)
                : Span.Disabled;

        public static Span StartProcessSpanOnce(string key, string scenario, string metric, string source = "app")
            => IsEnabled && Once.TryClaim(key)
                ? new Span(scenario, metric, source, ProcessStartedAt)
                : Span.Disabled;

        public static async Task FlushAsync(TimeSpan? timeout = null)
        {
            Task pending;
            lock (WriteLock) pending = _pendingWrite;
            try { await pending.WaitAsync(timeout ?? TimeSpan.FromSeconds(2)); }
            catch { }
        }

        private static void Queue(Entry entry)
        {
            var path = _path;
            if (string.IsNullOrWhiteSpace(path)) return;
            lock (WriteLock)
            {
                long sequence = Interlocked.Increment(ref _sequence);
                _pendingWrite = _pendingWrite.ContinueWith(
                    _ => Append(path, sequence, entry),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);
            }
        }

        private static void Append(string path, long sequence, Entry entry)
        {
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                DiagnosticLogRetention.RotateIfNeeded(path);
                bool headerRequired = !File.Exists(path);
                using var writer = new StreamWriter(path, append: true, encoding: new UTF8Encoding(false));
                if (headerRequired) writer.WriteLine(Header);
                writer.WriteLine(string.Join(",",
                    Csv(RunId),
                    sequence.ToString(CultureInfo.InvariantCulture),
                    Csv(entry.Timestamp.ToString("O", CultureInfo.InvariantCulture)),
                    Csv(entry.Scenario),
                    Csv(entry.Metric),
                    Csv(entry.Start.ToString("O", CultureInfo.InvariantCulture)),
                    Csv(entry.End.ToString("O", CultureInfo.InvariantCulture)),
                    Math.Max(0, (entry.End - entry.Start).TotalMilliseconds).ToString("0.###", CultureInfo.InvariantCulture),
                    Csv(entry.Outcome),
                    Csv(entry.Source)));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Writing performance diagnostics failed: {ex.Message}");
            }
        }

        private static DateTimeOffset GetProcessStart()
        {
            try { return Process.GetCurrentProcess().StartTime.ToUniversalTime(); }
            catch { return DateTimeOffset.UtcNow; }
        }

        private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

        private readonly record struct Entry(
            DateTimeOffset Timestamp,
            string Scenario,
            string Metric,
            DateTimeOffset Start,
            DateTimeOffset End,
            string Outcome,
            string Source);

        internal sealed class Span : IDisposable
        {
            internal static readonly Span Disabled = new(null, null, null, default, disabled: true);
            private readonly string? _scenario;
            private readonly string? _metric;
            private readonly string? _source;
            private readonly DateTimeOffset _start;
            private readonly string? _successKey;
            private int _completed;

            internal Span(
                string? scenario,
                string? metric,
                string? source,
                DateTimeOffset start,
                bool disabled = false,
                string? successKey = null)
            {
                _scenario = scenario;
                _metric = metric;
                _source = source;
                _start = start;
                _successKey = successKey;
                _completed = disabled ? 1 : 0;
            }

            public void Complete(string outcome = "success")
            {
                if (Interlocked.Exchange(ref _completed, 1) != 0) return;
                if (string.Equals(outcome, "success", StringComparison.Ordinal)
                    && _successKey != null
                    && !Once.TryClaim(_successKey)) return;
                var end = DateTimeOffset.UtcNow;
                Queue(new Entry(end, _scenario!, _metric!, _start, end, outcome, _source!));
            }

            public void Dispose() => Complete("cancelled");
        }
    }
}
