using System;
using System.Collections.Generic;

namespace Task_Flyout.Services
{
    internal readonly record struct PerformanceSample(
        string OperationId,
        string Scenario,
        string Metric,
        string Source,
        string SampleKind,
        DateTimeOffset Start,
        DateTimeOffset End,
        double Duration,
        string Outcome,
        string TimingBasis = "monotonic");

    // One command can have intermediate checkpoints and exactly one terminal outcome.
    internal sealed class PerformanceOperation : IDisposable
    {
        private readonly object _gate = new();
        private readonly TimeProvider _clock;
        private readonly Action<PerformanceSample> _record;
        private readonly PerformanceSample _sample;
        private readonly long _startedAt;
        private readonly HashSet<string> _checkpoints = new(StringComparer.Ordinal);
        private bool _completed;

        public PerformanceOperation(
            string scenario, string metric, string source, string sampleKind,
            Action<PerformanceSample> record, TimeProvider? clock = null)
        {
            _clock = clock ?? TimeProvider.System;
            _record = record;
            _startedAt = _clock.GetTimestamp();
            _sample = new PerformanceSample(Guid.NewGuid().ToString("N"), scenario,
                metric, source, sampleKind, _clock.GetUtcNow(), default, 0, "success");
            _record(_sample with { End = _sample.Start, Outcome = "started" });
        }

        public void Checkpoint(string metric, string outcome = "success")
        {
            lock (_gate)
            {
                if (_completed || metric == _sample.Metric || !_checkpoints.Add(metric)) return;
                Record(metric, outcome);
            }
        }

        public void Complete(string outcome = "success")
        {
            lock (_gate)
            {
                if (_completed) return;
                _completed = true;
                Record(_sample.Metric, outcome);
            }
        }

        private void Record(string metric, string outcome)
            => _record(_sample with
            {
                Metric = metric,
                End = _clock.GetUtcNow(),
                Duration = _clock.GetElapsedTime(_startedAt).TotalMilliseconds,
                Outcome = outcome
            });

        public void Dispose() => Complete("cancelled");
    }
}
