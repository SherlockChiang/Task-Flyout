using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;

namespace Task_Flyout.Services;

internal enum StandaloneTaskbarBinaryResolution
{
    Available,
    BrokerMissing,
    HostMissing,
    InvalidBaseDirectory
}

internal readonly record struct StandaloneTaskbarBinaryPaths(
    string BrokerPath,
    string HostPath);

internal static class StandaloneTaskbarBinaryLocator
{
    public const string BrokerFileName = "TaskFlyout.TaskbarBroker.exe";
    public const string HostFileName = "TaskFlyout.TaskbarHost.dll";

    public static StandaloneTaskbarBinaryResolution TryResolve(
        string? baseDirectory,
        out StandaloneTaskbarBinaryPaths paths)
    {
        paths = default;
        if (string.IsNullOrWhiteSpace(baseDirectory))
            return StandaloneTaskbarBinaryResolution.InvalidBaseDirectory;

        try
        {
            string root = Path.GetFullPath(baseDirectory);
            string brokerPath = Path.Combine(root, BrokerFileName);
            string hostPath = Path.Combine(root, HostFileName);
            if (!File.Exists(brokerPath))
                return StandaloneTaskbarBinaryResolution.BrokerMissing;
            if (!File.Exists(hostPath))
                return StandaloneTaskbarBinaryResolution.HostMissing;

            paths = new StandaloneTaskbarBinaryPaths(brokerPath, hostPath);
            return StandaloneTaskbarBinaryResolution.Available;
        }
        catch (ArgumentException)
        {
            return StandaloneTaskbarBinaryResolution.InvalidBaseDirectory;
        }
        catch (IOException)
        {
            return StandaloneTaskbarBinaryResolution.InvalidBaseDirectory;
        }
        catch (UnauthorizedAccessException)
        {
            return StandaloneTaskbarBinaryResolution.InvalidBaseDirectory;
        }
    }
}

internal enum StandaloneTaskbarBrokerProcessKind
{
    Completed,
    TimedOut,
    Cancelled,
    LaunchFailed,
    OutputLimitExceeded,
    OutputReadFailed
}

internal readonly record struct StandaloneTaskbarBrokerProcessResult(
    StandaloneTaskbarBrokerProcessKind Kind,
    int ExitCode,
    string StandardOutput);

internal interface IStandaloneTaskbarBrokerClient
{
    Task<StandaloneTaskbarBrokerResult> ProbeAsync(
        CancellationToken cancellationToken = default);

    Task<StandaloneTaskbarBrokerResult> StartAsync(
        CancellationToken cancellationToken = default);

    Task<StandaloneTaskbarBrokerResult> StopAsync(
        CancellationToken cancellationToken = default);
}

internal interface IStandaloneTaskbarBrokerProcessRunner
{
    Task<StandaloneTaskbarBrokerProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

internal sealed class StandaloneTaskbarBrokerProcessRunner :
    IStandaloneTaskbarBrokerProcessRunner
{
    public const int MaxStreamBytes = 32 * 1024;
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(2);
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public async Task<StandaloneTaskbarBrokerProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new(
                StandaloneTaskbarBrokerProcessKind.Cancelled,
                -1,
                string.Empty);
        }

        using var process = new Process();
        try
        {
            process.StartInfo = CreateStartInfo(executablePath, arguments);
            process.EnableRaisingEvents = true;
            if (!process.Start())
            {
                return new(
                    StandaloneTaskbarBrokerProcessKind.LaunchFailed,
                    -1,
                    string.Empty);
            }
        }
        catch (ArgumentException)
        {
            return LaunchFailed();
        }
        catch (InvalidOperationException)
        {
            return LaunchFailed();
        }
        catch (Win32Exception)
        {
            return LaunchFailed();
        }

        Task exitTask;
        Task<BoundedStreamResult> stdoutTask;
        Task<BoundedStreamResult> stderrTask;
        try
        {
            exitTask = process.WaitForExitAsync(CancellationToken.None);
            stdoutTask = ReadBoundedAsync(process.StandardOutput.BaseStream);
            stderrTask = ReadBoundedAsync(process.StandardError.BaseStream);
        }
        catch (InvalidOperationException)
        {
            TryKill(process);
            return new(
                StandaloneTaskbarBrokerProcessKind.OutputReadFailed,
                -1,
                string.Empty);
        }

        Task timeoutTask = Task.Delay(
            timeout <= TimeSpan.Zero ? TimeSpan.Zero : timeout);
        Task cancellationTask = Task.Delay(
            Timeout.InfiniteTimeSpan,
            cancellationToken);
        Task winner;
        try
        {
            winner = await Task.WhenAny(
                exitTask,
                timeoutTask,
                cancellationTask).ConfigureAwait(false);
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or ObjectDisposedException)
        {
            TryKill(process);
            await AwaitCleanupAsync(exitTask, stdoutTask, stderrTask)
                .ConfigureAwait(false);
            return new(
                StandaloneTaskbarBrokerProcessKind.OutputReadFailed,
                -1,
                string.Empty);
        }

        if (winner != exitTask && !HasExited(process))
        {
            bool cancelled = winner == cancellationTask ||
                cancellationToken.IsCancellationRequested;
            TryKill(process);
            await AwaitCleanupAsync(exitTask, stdoutTask, stderrTask)
                .ConfigureAwait(false);
            return new(
                cancelled
                    ? StandaloneTaskbarBrokerProcessKind.Cancelled
                    : StandaloneTaskbarBrokerProcessKind.TimedOut,
                -1,
                string.Empty);
        }

        try
        {
            await exitTask.ConfigureAwait(false);
            BoundedStreamResult stdout = await stdoutTask.ConfigureAwait(false);
            BoundedStreamResult stderr = await stderrTask.ConfigureAwait(false);
            int exitCode = process.ExitCode;

            if (stdout.Failed || stderr.Failed)
                return new(
                    StandaloneTaskbarBrokerProcessKind.OutputReadFailed,
                    exitCode,
                    string.Empty);
            if (stdout.Overflowed || stderr.Overflowed)
                return new(
                    StandaloneTaskbarBrokerProcessKind.OutputLimitExceeded,
                    exitCode,
                    string.Empty);

            try
            {
                return new(
                    StandaloneTaskbarBrokerProcessKind.Completed,
                    exitCode,
                    StrictUtf8.GetString(stdout.Bytes));
            }
            catch (DecoderFallbackException)
            {
                return new(
                    StandaloneTaskbarBrokerProcessKind.OutputReadFailed,
                    exitCode,
                    string.Empty);
            }
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or ObjectDisposedException or
            IOException)
        {
            TryKill(process);
            await AwaitCleanupAsync(exitTask, stdoutTask, stderrTask)
                .ConfigureAwait(false);
            return new(
                StandaloneTaskbarBrokerProcessKind.OutputReadFailed,
                -1,
                string.Empty);
        }
    }

    internal static ProcessStartInfo CreateStartInfo(
        string executablePath,
        IReadOnlyList<string> arguments)
    {
        string? workingDirectory = Path.GetDirectoryName(executablePath);
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? AppContext.BaseDirectory
                : workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = StrictUtf8,
            StandardErrorEncoding = StrictUtf8
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    private static StandaloneTaskbarBrokerProcessResult LaunchFailed()
        => new(
            StandaloneTaskbarBrokerProcessKind.LaunchFailed,
            -1,
            string.Empty);

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }

    private static async Task AwaitCleanupAsync(
        Task exitTask,
        Task<BoundedStreamResult> stdoutTask,
        Task<BoundedStreamResult> stderrTask)
    {
        try
        {
            await Task.WhenAll(exitTask, stdoutTask, stderrTask)
                .WaitAsync(CleanupTimeout)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (
            ex is OperationCanceledException or TimeoutException or
            InvalidOperationException or ObjectDisposedException or IOException)
        {
        }
    }

    private static async Task<BoundedStreamResult> ReadBoundedAsync(
        Stream stream)
    {
        var buffer = new byte[4096];
        using var output = new MemoryStream(capacity: MaxStreamBytes);
        bool overflowed = false;
        try
        {
            int read;
            while ((read = await stream.ReadAsync(
                       buffer.AsMemory(),
                       CancellationToken.None).ConfigureAwait(false)) != 0)
            {
                int remaining = MaxStreamBytes - checked((int)output.Length);
                if (remaining > 0)
                {
                    int keep = Math.Min(remaining, read);
                    output.Write(buffer, 0, keep);
                    if (keep != read)
                        overflowed = true;
                }
                else
                {
                    overflowed = true;
                }
            }

            return new(output.ToArray(), overflowed, false);
        }
        catch (Exception ex) when (
            ex is IOException or ObjectDisposedException or
            OperationCanceledException)
        {
            return new(Array.Empty<byte>(), false, true);
        }
    }

    private readonly record struct BoundedStreamResult(
        byte[] Bytes,
        bool Overflowed,
        bool Failed);
}

internal sealed class StandaloneTaskbarBrokerClient : IStandaloneTaskbarBrokerClient
{
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan ControlTimeout = TimeSpan.FromSeconds(10);

    private readonly string _baseDirectory;
    private readonly IStandaloneTaskbarBrokerProcessRunner _runner;

    public StandaloneTaskbarBrokerClient()
        : this(
            AppContext.BaseDirectory,
            new StandaloneTaskbarBrokerProcessRunner())
    {
    }

    internal StandaloneTaskbarBrokerClient(
        string baseDirectory,
        IStandaloneTaskbarBrokerProcessRunner runner)
    {
        _baseDirectory = baseDirectory;
        _runner = runner;
    }

    public Task<StandaloneTaskbarBrokerResult> ProbeAsync(
        CancellationToken cancellationToken = default)
        => ExecuteAsync(
            command: null,
            cancellationToken);

    public Task<StandaloneTaskbarBrokerResult> StartAsync(
        CancellationToken cancellationToken = default)
        => ExecuteAsync(
            StandaloneTaskbarBrokerCommand.Start,
            cancellationToken);

    public Task<StandaloneTaskbarBrokerResult> StopAsync(
        CancellationToken cancellationToken = default)
        => ExecuteAsync(
            StandaloneTaskbarBrokerCommand.Stop,
            cancellationToken);

    private async Task<StandaloneTaskbarBrokerResult> ExecuteAsync(
        StandaloneTaskbarBrokerCommand? command,
        CancellationToken cancellationToken)
    {
        StandaloneTaskbarBinaryResolution resolution =
            StandaloneTaskbarBinaryLocator.TryResolve(
                _baseDirectory,
                out StandaloneTaskbarBinaryPaths paths);
        if (resolution != StandaloneTaskbarBinaryResolution.Available)
        {
            return new(StandaloneTaskbarBrokerResultKind.BinaryMissing);
        }

        bool isProbe = command is null;
        var arguments = new List<string>(isProbe ? 2 : 4);
        if (isProbe)
        {
            arguments.Add("probe");
            arguments.Add("--strict");
        }
        else
        {
            arguments.Add(command == StandaloneTaskbarBrokerCommand.Start
                ? "start"
                : "stop");
            arguments.Add("--host");
            arguments.Add(paths.HostPath);
        }

        StandaloneTaskbarBrokerProcessResult processResult =
            await _runner.RunAsync(
                paths.BrokerPath,
                arguments,
                isProbe ? ProbeTimeout : ControlTimeout,
                cancellationToken).ConfigureAwait(false);

        switch (processResult.Kind)
        {
            case StandaloneTaskbarBrokerProcessKind.Completed:
                if (isProbe)
                {
                    return StandaloneTaskbarBrokerProtocol.ParseProbe(
                        processResult.StandardOutput,
                        processResult.ExitCode);
                }

                return StandaloneTaskbarBrokerProtocol.ParseControl(
                    command!.Value,
                    processResult.StandardOutput,
                    processResult.ExitCode);
            case StandaloneTaskbarBrokerProcessKind.TimedOut:
                return new(isProbe
                    ? StandaloneTaskbarBrokerResultKind.TimedOut
                    : StandaloneTaskbarBrokerResultKind.Ambiguous);
            case StandaloneTaskbarBrokerProcessKind.Cancelled:
                return new(StandaloneTaskbarBrokerResultKind.Cancelled);
            case StandaloneTaskbarBrokerProcessKind.OutputLimitExceeded:
            case StandaloneTaskbarBrokerProcessKind.OutputReadFailed:
                return new(isProbe
                    ? StandaloneTaskbarBrokerResultKind.InvalidResponse
                    : StandaloneTaskbarBrokerResultKind.Ambiguous);
            case StandaloneTaskbarBrokerProcessKind.LaunchFailed:
            default:
                return new(StandaloneTaskbarBrokerResultKind.Failed);
        }
    }
}
