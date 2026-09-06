using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Task_Flyout.Services
{
    internal static class PerformanceDiagnostics
    {
        internal const string Header = "run_id,sequence,timestamp,scenario,metric,start,end,duration,outcome,source,schema_version,build_id,build_version,configuration,architecture,operation_id,sample_kind,timing_basis,dropped_before,write_failures_before";
        private static readonly PerformanceOncePolicy Once = new();
        private static readonly object InitializeLock = new();
        private static readonly string RunId = Guid.NewGuid().ToString("N");
        private static readonly DateTimeOffset ProcessStartedAt = GetProcessStart();
        private static readonly Assembly BuildAssembly = typeof(PerformanceDiagnostics).Assembly;
        private static readonly string BuildId = BuildAssembly.ManifestModule.ModuleVersionId.ToString("N");
        private static readonly string BuildVersion = BuildAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        private static readonly string Configuration = BuildAssembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "unknown";
        private static readonly BoundedDiagnosticWriter<PerformanceSample> Writer = new(Append);
        private static string? _path;
        private static int _enabled;

        public static bool IsEnabled => Volatile.Read(ref _enabled) != 0;

        public static void Initialize(bool enabled, string? path = null)
        {
            if (!enabled) return;
            lock (InitializeLock)
            {
                if (IsEnabled) return;
                try
                {
                    _path = path ?? AppDataPathHelper.ResolveLocal("Logs", "performance-diagnostics-v2.csv");
                    Volatile.Write(ref _enabled, 1);
                }
                catch { Debug.WriteLine("Performance diagnostics could not initialize."); }
            }
        }

        public static void MarkOnce(string key, string scenario, string metric, string outcome = "success", string source = "app")
        {
            if (!IsEnabled || !Once.TryClaim(key)) return;
            var now = DateTimeOffset.UtcNow;
            Queue(new PerformanceSample("", scenario, metric, source, "milestone", now, now, 0, outcome));
        }

        public static PerformanceOperation? StartInteraction(string scenario, string metric, string source)
            => IsEnabled ? new PerformanceOperation(scenario, metric, source,
                Once.TryClaim("interaction." + scenario) ? "cold_attempt" : "warm_attempt", Queue) : null;

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
                ? new Span(scenario, metric, source, ProcessStartedAt, processEstimate: true)
                : Span.Disabled;

        public static async Task FlushAsync(TimeSpan? timeout = null)
        {
            if (!IsEnabled) return;
            try { await FlushCoreAsync().WaitAsync(timeout ?? TimeSpan.FromSeconds(2)); }
            catch { }
        }

        private static async Task FlushCoreAsync()
        {
            await Writer.DrainAsync().ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            Queue(new PerformanceSample("", "diagnostics", "writer_health", "logger", "status", now, now, 0, "success"));
            await Writer.DrainAsync().ConfigureAwait(false);
        }

        private static void Queue(PerformanceSample entry) => Writer.TryEnqueue(entry);

        private static void Append(long sequence, PerformanceSample entry)
        {
            var path = _path!;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            DiagnosticLogRetention.RotateIfNeeded(path);
            bool headerRequired = !File.Exists(path) || new FileInfo(path).Length == 0;
            if (!headerRequired)
            {
                using var reader = new StreamReader(path);
                if (!string.Equals(reader.ReadLine(), Header, StringComparison.Ordinal))
                    throw new InvalidDataException("Performance diagnostics schema mismatch.");
            }
            using var writer = new StreamWriter(path, append: true, encoding: new UTF8Encoding(false));
            if (headerRequired) writer.WriteLine(Header);
            writer.WriteLine(string.Join(",",
                Csv(RunId), sequence.ToString(CultureInfo.InvariantCulture),
                Csv(entry.End.ToString("O", CultureInfo.InvariantCulture)),
                Csv(entry.Scenario), Csv(entry.Metric),
                Csv(entry.Start.ToString("O", CultureInfo.InvariantCulture)),
                Csv(entry.End.ToString("O", CultureInfo.InvariantCulture)),
                entry.Duration.ToString("0.###", CultureInfo.InvariantCulture),
                Csv(entry.Outcome), Csv(entry.Source), "2",
                Csv(BuildId), Csv(BuildVersion), Csv(Configuration),
                Csv(RuntimeInformation.ProcessArchitecture.ToString()),
                Csv(entry.OperationId), Csv(entry.SampleKind), Csv(entry.TimingBasis),
                Writer.Dropped.ToString(CultureInfo.InvariantCulture),
                Writer.Failures.ToString(CultureInfo.InvariantCulture)));
        }

        private static DateTimeOffset GetProcessStart()
        {
            try
            {
                using var process = Process.GetCurrentProcess();
                return process.StartTime.ToUniversalTime();
            }
            catch { return DateTimeOffset.UtcNow; }
        }

        private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

        internal sealed class Span : IDisposable
        {
            internal static readonly Span Disabled = new(null, null, null, default, disabled: true);
            private readonly string? _scenario;
            private readonly string? _metric;
            private readonly string? _source;
            private readonly DateTimeOffset _start;
            private readonly long _startedAt = Stopwatch.GetTimestamp();
            private readonly bool _processEstimate;
            private readonly string? _successKey;
            private int _completed;

            internal Span(
                string? scenario,
                string? metric,
                string? source,
                DateTimeOffset start,
                bool disabled = false,
                string? successKey = null,
                bool processEstimate = false)
            {
                _scenario = scenario;
                _metric = metric;
                _source = source;
                _start = start;
                _successKey = successKey;
                _processEstimate = processEstimate;
                _completed = disabled ? 1 : 0;
            }

            public void Complete(string outcome = "success")
            {
                if (Interlocked.Exchange(ref _completed, 1) != 0) return;
                if (string.Equals(outcome, "success", StringComparison.Ordinal)
                    && _successKey != null
                    && !Once.TryClaim(_successKey)) return;
                var end = DateTimeOffset.UtcNow;
                Queue(new PerformanceSample("", _scenario!, _metric!, _source!, "span", _start, end,
                    _processEstimate ? Math.Max(0, (end - _start).TotalMilliseconds) : Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds,
                    outcome, _processEstimate ? "process_wall_estimate" : "monotonic"));
            }

            public void Dispose() => Complete("cancelled");
        }
    }
}
