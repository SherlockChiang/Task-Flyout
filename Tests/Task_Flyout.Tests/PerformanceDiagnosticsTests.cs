using Microsoft.VisualBasic.FileIO;
using System.Reflection;
using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public sealed class PerformanceDiagnosticsTests
{
    [Fact]
    public async Task Opt_in_writer_emits_parseable_versioned_correlated_records()
    {
        string path = Path.Combine(Path.GetTempPath(), $"taskflyout-performance-{Guid.NewGuid():N}.csv");
        try
        {
            PerformanceDiagnostics.Initialize(false, path);
            Assert.Null(PerformanceDiagnostics.StartInteraction("test", "end", "ui"));
            PerformanceDiagnostics.StartSpan("test", "disabled").Complete();
            await PerformanceDiagnostics.FlushAsync();
            Assert.False(File.Exists(path));

            PerformanceDiagnostics.Initialize(true, path);
            using var first = PerformanceDiagnostics.StartInteraction("test", "end", "ui");
            first!.Checkpoint("checkpoint");
            first.Complete();
            using var second = PerformanceDiagnostics.StartInteraction("test", "end", "ui");
            second!.Complete("superseded");
            PerformanceDiagnostics.StartProcessSpanOnce("test.process", "startup", "start", "process").Complete();
            PerformanceDiagnostics.StartSpan("test", "span").Complete();
            PerformanceDiagnostics.MarkOnce("test.once", "test", "milestone");
            PerformanceDiagnostics.MarkOnce("test.once", "test", "milestone");
            await PerformanceDiagnostics.FlushAsync();

            using var parser = new TextFieldParser(path) { Delimiters = [","], HasFieldsEnclosedInQuotes = true };
            var header = parser.ReadFields()!;
            Assert.Equal(PerformanceDiagnostics.Header.Split(','), header);
            var rows = new List<Dictionary<string, string>>();
            while (!parser.EndOfData)
            {
                var fields = parser.ReadFields()!;
                Assert.Equal(header.Length, fields.Length);
                rows.Add(header.Zip(fields).ToDictionary(pair => pair.First, pair => pair.Second));
            }
            Assert.Equal(9, rows.Count);
            Assert.All(rows, row =>
            {
                Assert.Equal("2", row["schema_version"]);
                Assert.True(Guid.TryParseExact(row["build_id"], "N", out _));
                Assert.Equal(typeof(PerformanceDiagnostics).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration, row["configuration"]);
                Assert.Equal("0", row["dropped_before"]);
                Assert.Equal("0", row["write_failures_before"]);
            });
            Assert.Equal(new[] { "started", "success", "success" }, rows.Take(3).Select(row => row["outcome"]));
            Assert.Single(rows.Take(3).Select(row => row["operation_id"]).Distinct());
            Assert.All(rows.Take(3), row => Assert.Equal("cold_attempt", row["sample_kind"]));
            Assert.Equal("warm_attempt", rows[3]["sample_kind"]);
            Assert.NotEqual(rows[0]["operation_id"], rows[3]["operation_id"]);
            Assert.Equal("process_wall_estimate", rows[5]["timing_basis"]);
            Assert.Equal("monotonic", rows[6]["timing_basis"]);
            Assert.Single(rows, row => row["metric"] == "milestone");
            Assert.Equal("writer_health", rows[^1]["metric"]);
        }
        finally { File.Delete(path); }
    }
}
