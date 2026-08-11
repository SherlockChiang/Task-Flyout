using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class StandaloneTaskbarBrokerClientTests
{
    [Fact]
    public void Locator_requires_both_adjacent_binaries()
    {
        string directory = CreateTempDirectory();
        try
        {
            Assert.Equal(
                StandaloneTaskbarBinaryResolution.BrokerMissing,
                StandaloneTaskbarBinaryLocator.TryResolve(directory, out _));

            string broker = Path.Combine(
                directory,
                StandaloneTaskbarBinaryLocator.BrokerFileName);
            File.WriteAllBytes(broker, Array.Empty<byte>());
            Assert.Equal(
                StandaloneTaskbarBinaryResolution.HostMissing,
                StandaloneTaskbarBinaryLocator.TryResolve(directory, out _));

            string host = Path.Combine(
                directory,
                StandaloneTaskbarBinaryLocator.HostFileName);
            File.WriteAllBytes(host, Array.Empty<byte>());
            var resolution = StandaloneTaskbarBinaryLocator.TryResolve(
                directory,
                out StandaloneTaskbarBinaryPaths paths);

            Assert.Equal(
                StandaloneTaskbarBinaryResolution.Available,
                resolution);
            Assert.Equal(Path.GetFullPath(broker), paths.BrokerPath);
            Assert.Equal(Path.GetFullPath(host), paths.HostPath);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task Missing_binary_does_not_start_a_process()
    {
        string directory = CreateTempDirectory();
        var runner = new FakeRunner();
        try
        {
            var client = new StandaloneTaskbarBrokerClient(directory, runner);

            var result = await client.StartAsync();

            Assert.Equal(
                StandaloneTaskbarBrokerResultKind.BinaryMissing,
                result.Kind);
            Assert.Equal(0, runner.CallCount);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task Probe_uses_strict_probe_arguments_and_maps_supported()
    {
        string directory = CreateBinaryDirectory();
        var runner = new FakeRunner
        {
            Result = Completed(0, "{\"status\":\"supported\"}")
        };
        try
        {
            var client = new StandaloneTaskbarBrokerClient(directory, runner);

            var result = await client.ProbeAsync();

            Assert.Equal(
                StandaloneTaskbarBrokerResultKind.ProbeSupported,
                result.Kind);
            Assert.Equal(
                new[] { "probe", "--strict" },
                runner.Arguments);
            Assert.Equal(StandaloneTaskbarBrokerClient.ProbeTimeout, runner.Timeout);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task Start_passes_host_as_an_argument_and_remains_unverified()
    {
        string directory = CreateBinaryDirectory();
        var runner = new FakeRunner
        {
            Result = Completed(
                0,
                "{\"status\":\"acknowledged\",\"command\":\"start\"," +
                "\"processId\":42,\"controlNonce\":17," +
                "\"controllerStatus\":\"started\"}")
        };
        try
        {
            var client = new StandaloneTaskbarBrokerClient(directory, runner);

            var result = await client.StartAsync();

            Assert.Equal(
                StandaloneTaskbarBrokerResultKind.ControllerActiveUnverified,
                result.Kind);
            Assert.Equal(
                new StandaloneTaskbarControllerIdentity(42, 17),
                result.ControllerIdentity);
            Assert.Equal("start", runner.Arguments[0]);
            Assert.Equal("--host", runner.Arguments[1]);
            Assert.Equal(
                Path.Combine(
                    Path.GetFullPath(directory),
                    StandaloneTaskbarBinaryLocator.HostFileName),
                runner.Arguments[2]);
            Assert.Equal(StandaloneTaskbarBrokerClient.ControlTimeout, runner.Timeout);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task Stop_accepts_idempotent_not_started_acknowledgement()
    {
        string directory = CreateBinaryDirectory();
        var runner = new FakeRunner
        {
            Result = Completed(
                0,
                "{\"status\":\"acknowledged\",\"command\":\"stop\",\"controllerStatus\":\"not-started\"}")
        };
        try
        {
            var result = await new StandaloneTaskbarBrokerClient(directory, runner)
                .StopAsync();

            Assert.Equal(
                StandaloneTaskbarBrokerResultKind.ControllerInactive,
                result.Kind);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task Status_passes_host_and_maps_live_mount_proof()
    {
        string directory = CreateBinaryDirectory();
        var runner = new FakeRunner
        {
            Result = Completed(
                0,
                "{\"status\":\"acknowledged\",\"command\":\"status\",\"controllerStatus\":\"mount-ready\"}")
        };
        try
        {
            var result = await new StandaloneTaskbarBrokerClient(directory, runner)
                .GetStatusAsync();

            Assert.Equal(
                StandaloneTaskbarBrokerResultKind.ControllerMountReady,
                result.Kind);
            Assert.Equal("status", runner.Arguments[0]);
            Assert.Equal("--host", runner.Arguments[1]);
            Assert.Equal(
                Path.Combine(
                    Path.GetFullPath(directory),
                    StandaloneTaskbarBinaryLocator.HostFileName),
                runner.Arguments[2]);
            Assert.Equal(StandaloneTaskbarBrokerClient.StatusTimeout, runner.Timeout);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task Timeout_is_temporary_for_probe_but_ambiguous_for_control()
    {
        string directory = CreateBinaryDirectory();
        try
        {
            var probeRunner = new FakeRunner
            {
                Result = ProcessResult(StandaloneTaskbarBrokerProcessKind.TimedOut)
            };
            var probe = await new StandaloneTaskbarBrokerClient(directory, probeRunner)
                .ProbeAsync();
            Assert.Equal(StandaloneTaskbarBrokerResultKind.TimedOut, probe.Kind);

            var startRunner = new FakeRunner
            {
                Result = ProcessResult(StandaloneTaskbarBrokerProcessKind.TimedOut)
            };
            var start = await new StandaloneTaskbarBrokerClient(directory, startRunner)
                .StartAsync();
            Assert.Equal(StandaloneTaskbarBrokerResultKind.Ambiguous, start.Kind);

            var statusRunner = new FakeRunner
            {
                Result = ProcessResult(StandaloneTaskbarBrokerProcessKind.TimedOut)
            };
            var status = await new StandaloneTaskbarBrokerClient(directory, statusRunner)
                .GetStatusAsync();
            Assert.Equal(StandaloneTaskbarBrokerResultKind.TimedOut, status.Kind);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task Cancellation_and_launch_failure_are_not_reported_as_success()
    {
        string directory = CreateBinaryDirectory();
        try
        {
            var cancelled = await new StandaloneTaskbarBrokerClient(
                directory,
                new FakeRunner
                {
                    Result = ProcessResult(StandaloneTaskbarBrokerProcessKind.Cancelled)
                }).StopAsync();
            Assert.Equal(StandaloneTaskbarBrokerResultKind.Cancelled, cancelled.Kind);

            var failed = await new StandaloneTaskbarBrokerClient(
                directory,
                new FakeRunner
                {
                    Result = ProcessResult(StandaloneTaskbarBrokerProcessKind.LaunchFailed)
                }).StartAsync();
            Assert.Equal(StandaloneTaskbarBrokerResultKind.Failed, failed.Kind);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task Output_failures_cannot_hide_uncertain_control_state()
    {
        string directory = CreateBinaryDirectory();
        try
        {
            var probe = await new StandaloneTaskbarBrokerClient(
                directory,
                new FakeRunner
                {
                    Result = ProcessResult(
                        StandaloneTaskbarBrokerProcessKind.OutputLimitExceeded)
                }).ProbeAsync();
            Assert.Equal(StandaloneTaskbarBrokerResultKind.InvalidResponse, probe.Kind);

            var stop = await new StandaloneTaskbarBrokerClient(
                directory,
                new FakeRunner
                {
                    Result = ProcessResult(
                        StandaloneTaskbarBrokerProcessKind.OutputReadFailed)
                }).StopAsync();
            Assert.Equal(StandaloneTaskbarBrokerResultKind.Ambiguous, stop.Kind);

            var status = await new StandaloneTaskbarBrokerClient(
                directory,
                new FakeRunner
                {
                    Result = ProcessResult(
                        StandaloneTaskbarBrokerProcessKind.OutputReadFailed)
                }).GetStatusAsync();
            Assert.Equal(
                StandaloneTaskbarBrokerResultKind.InvalidResponse,
                status.Kind);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public void Process_start_info_disables_shell_and_uses_argument_list()
    {
        var info = StandaloneTaskbarBrokerProcessRunner.CreateStartInfo(
            "C:\\Task Flyout\\TaskFlyout.TaskbarBroker.exe",
            new[] { "start", "--host", "C:\\Task Flyout\\TaskFlyout.TaskbarHost.dll" });

        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
        Assert.True(info.RedirectStandardOutput);
        Assert.True(info.RedirectStandardError);
        Assert.Equal(
            new[] { "start", "--host", "C:\\Task Flyout\\TaskFlyout.TaskbarHost.dll" },
            info.ArgumentList);
        Assert.Equal("C:\\Task Flyout", info.WorkingDirectory);
    }

    private static StandaloneTaskbarBrokerProcessResult Completed(
        int exitCode,
        string output)
        => new(
            StandaloneTaskbarBrokerProcessKind.Completed,
            exitCode,
            output);

    private static StandaloneTaskbarBrokerProcessResult ProcessResult(
        StandaloneTaskbarBrokerProcessKind kind)
        => new(kind, -1, string.Empty);

    private static string CreateBinaryDirectory()
    {
        string directory = CreateTempDirectory();
        File.WriteAllBytes(
            Path.Combine(
                directory,
                StandaloneTaskbarBinaryLocator.BrokerFileName),
            Array.Empty<byte>());
        File.WriteAllBytes(
            Path.Combine(
                directory,
                StandaloneTaskbarBinaryLocator.HostFileName),
            Array.Empty<byte>());
        return directory;
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "TaskFlyoutBrokerTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTempDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch
        {
        }
    }

    private sealed class FakeRunner : IStandaloneTaskbarBrokerProcessRunner
    {
        public StandaloneTaskbarBrokerProcessResult Result { get; init; }
            = ProcessResult(StandaloneTaskbarBrokerProcessKind.Completed);
        public int CallCount { get; private set; }
        public string ExecutablePath { get; private set; } = string.Empty;
        public IReadOnlyList<string> Arguments { get; private set; } = Array.Empty<string>();
        public TimeSpan Timeout { get; private set; }

        public Task<StandaloneTaskbarBrokerProcessResult> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            CallCount++;
            ExecutablePath = executablePath;
            Arguments = new List<string>(arguments);
            Timeout = timeout;
            return Task.FromResult(Result);
        }
    }
}
