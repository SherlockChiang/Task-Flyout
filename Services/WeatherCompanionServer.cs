using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Task_Flyout.Services
{
    internal readonly record struct WeatherCompanionClientContext(
        uint ProcessId);

    internal static class WeatherCompanionFraming
    {
        private const int HeaderBytes = sizeof(uint);

        public static async ValueTask<byte[]?> ReadFrameAsync(
            Stream stream,
            int maxPayloadBytes,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPayloadBytes);

            byte[] header = new byte[HeaderBytes];
            int firstRead = await stream.ReadAsync(
                header.AsMemory(0, HeaderBytes), cancellationToken).ConfigureAwait(false);
            if (firstRead == 0) return null;

            await ReadRemainingAsync(
                stream,
                header.AsMemory(firstRead, HeaderBytes - firstRead),
                cancellationToken).ConfigureAwait(false);

            uint payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(header);
            if (payloadLength > maxPayloadBytes)
                throw new InvalidDataException("Weather companion frame exceeds the protocol limit.");

            byte[] payload = new byte[(int)payloadLength];
            await ReadRemainingAsync(stream, payload, cancellationToken).ConfigureAwait(false);
            return payload;
        }

        public static async ValueTask WriteFrameAsync(
            Stream stream,
            ReadOnlyMemory<byte> payload,
            int maxPayloadBytes,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPayloadBytes);
            if (payload.Length > maxPayloadBytes)
                throw new InvalidDataException("Weather companion frame exceeds the protocol limit.");

            byte[] header = new byte[HeaderBytes];
            BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)payload.Length);
            await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            if (!payload.IsEmpty)
                await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private static async ValueTask ReadRemainingAsync(
            Stream stream,
            Memory<byte> destination,
            CancellationToken cancellationToken)
        {
            while (!destination.IsEmpty)
            {
                int read = await stream.ReadAsync(destination, cancellationToken).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException("Weather companion frame ended early.");
                destination = destination[read..];
            }
        }
    }

    internal sealed class WeatherCompanionServer : IAsyncDisposable
    {
        public const string PipeNamePrefix = "TaskFlyout.Weather.v1";

        private static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(1);

        private readonly object _lifecycleLock = new();
        private readonly Func<WeatherCompanionRequest, WeatherCompanionClientContext, CancellationToken, ValueTask<byte[]>> _requestHandler;
        private readonly TimeSpan _operationTimeout;
        private readonly TimeSpan _retryDelay;
        private CancellationTokenSource? _shutdown;
        private Task? _runTask;
        private NamedPipeServerStream? _activePipe;
        private bool _disposed;

        public WeatherCompanionServer(
            Func<WeatherCompanionRequest, CancellationToken, ValueTask<byte[]>> requestHandler,
            string? pipeName = null,
            TimeSpan? operationTimeout = null,
            TimeSpan? retryDelay = null)
            : this(
                (request, _, cancellationToken) =>
                    requestHandler(request, cancellationToken),
                pipeName,
                operationTimeout,
                retryDelay)
        {
            ArgumentNullException.ThrowIfNull(requestHandler);
        }

        internal WeatherCompanionServer(
            Func<WeatherCompanionRequest, WeatherCompanionClientContext, CancellationToken, ValueTask<byte[]>> requestHandler,
            string? pipeName = null,
            TimeSpan? operationTimeout = null,
            TimeSpan? retryDelay = null)
        {
            _requestHandler = requestHandler ?? throw new ArgumentNullException(nameof(requestHandler));
            PipeName = pipeName ?? GetCurrentUserPipeName();
            if (string.IsNullOrWhiteSpace(PipeName))
                throw new ArgumentException("A pipe name is required.", nameof(pipeName));

            _operationTimeout = operationTimeout ?? DefaultOperationTimeout;
            _retryDelay = retryDelay ?? DefaultRetryDelay;
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_operationTimeout, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_retryDelay, TimeSpan.Zero);
        }

        public string PipeName { get; }

        public void Start()
        {
            lock (_lifecycleLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_runTask != null) return;

                _shutdown = new CancellationTokenSource();
                _runTask = Task.Run(() => RunAsync(_shutdown.Token));
            }
        }

        public async ValueTask DisposeAsync()
        {
            CancellationTokenSource? shutdown;
            Task? runTask;
            NamedPipeServerStream? activePipe;
            lock (_lifecycleLock)
            {
                if (_disposed) return;
                _disposed = true;
                shutdown = _shutdown;
                runTask = _runTask;
                activePipe = _activePipe;
                _shutdown = null;
                _runTask = null;
            }

            shutdown?.Cancel();
            activePipe?.Dispose();
            if (runTask != null)
            {
                try
                {
                    await runTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (shutdown?.IsCancellationRequested == true)
                {
                }
            }
            shutdown?.Dispose();
        }

        public static string GetCurrentUserPipeName()
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            string? sid = identity.User?.Value;
            if (string.IsNullOrWhiteSpace(sid))
                throw new InvalidOperationException("The current Windows user SID is unavailable.");
            return BuildPipeName(sid, Process.GetCurrentProcess().SessionId);
        }

        internal static string BuildPipeName(string userSid, int sessionId)
        {
            if (string.IsNullOrWhiteSpace(userSid) ||
                userSid.IndexOfAny(['\\', '/', ':']) >= 0 ||
                sessionId < 0)
            {
                throw new ArgumentException("A valid Windows user SID is required.", nameof(userSid));
            }

            _ = new SecurityIdentifier(userSid);
            return $"{PipeNamePrefix}.{userSid}.{sessionId}";
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                NamedPipeServerStream? pipe = null;
                try
                {
                    pipe = CreatePipe();
                    lock (_lifecycleLock)
                    {
                        if (_disposed)
                        {
                            pipe.Dispose();
                            return;
                        }
                        _activePipe = pipe;
                    }

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                        try
                        {
                            await HandleConnectionAsync(pipe, cancellationToken).ConfigureAwait(false);
                        }
                        finally
                        {
                            if (pipe.IsConnected) pipe.Disconnect();
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    Debug.WriteLine($"Weather companion pipe listener failed: {ex.Message}");
                    try
                    {
                        await Task.Delay(_retryDelay, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
                finally
                {
                    lock (_lifecycleLock)
                    {
                        if (ReferenceEquals(_activePipe, pipe)) _activePipe = null;
                    }
                    pipe?.Dispose();
                }
            }
        }

        private NamedPipeServerStream CreatePipe()
            => new(
                PipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous |
                PipeOptions.CurrentUserOnly |
                PipeOptions.FirstPipeInstance,
                WeatherCompanionProtocol.MaxRequestBytes + sizeof(uint),
                WeatherCompanionProtocol.MaxResponseBytes + sizeof(uint));

        private async Task HandleConnectionAsync(
            NamedPipeServerStream pipe,
            CancellationToken serverCancellation)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(serverCancellation);
            timeout.CancelAfter(_operationTimeout);

            try
            {
                byte[]? payload;
                try
                {
                    payload = await WeatherCompanionFraming.ReadFrameAsync(
                        pipe,
                        WeatherCompanionProtocol.MaxRequestBytes,
                        timeout.Token).ConfigureAwait(false);
                }
                catch (InvalidDataException)
                {
                    await WriteProtocolErrorAsync(
                        pipe,
                        WeatherCompanionResponseStatus.InvalidRequest,
                        "request-too-large",
                        timeout.Token).ConfigureAwait(false);
                    await WaitForClientCloseAsync(pipe, timeout.Token).ConfigureAwait(false);
                    return;
                }

                if (payload == null) return;
                if (!WeatherCompanionProtocol.TryParseRequest(payload, out var request, out var error))
                {
                    WeatherCompanionResponseStatus status =
                        error == WeatherCompanionProtocolError.UnsupportedVersion
                            ? WeatherCompanionResponseStatus.VersionMismatch
                            : WeatherCompanionResponseStatus.InvalidRequest;
                    await WriteProtocolErrorAsync(
                        pipe,
                        status,
                        GetProtocolErrorName(error),
                        timeout.Token).ConfigureAwait(false);
                    await WaitForClientCloseAsync(pipe, timeout.Token).ConfigureAwait(false);
                    return;
                }

                byte[] response;
                try
                {
                    WeatherCompanionClientContext clientContext =
                        GetClientContext(pipe);
                    response = await _requestHandler(
                        request,
                        clientContext,
                        timeout.Token).ConfigureAwait(false);
                    if (response.Length > WeatherCompanionProtocol.MaxResponseBytes)
                        throw new InvalidDataException("Weather companion handler response is too large.");
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Weather companion request failed: {ex.Message}");
                    response = WeatherCompanionProtocol.SerializeResponse(
                        WeatherCompanionResponseStatus.InternalError,
                        detail: "request-failed");
                }

                await WeatherCompanionFraming.WriteFrameAsync(
                    pipe,
                    response,
                    WeatherCompanionProtocol.MaxResponseBytes,
                    timeout.Token).ConfigureAwait(false);

                // Disconnecting immediately can discard a response that is still in
                // the pipe buffer. The one-request client closes after reading it;
                // keep the instance connected until then or until the transaction
                // deadline expires.
                await WaitForClientCloseAsync(pipe, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
            }
            catch (IOException)
            {
                // A same-user client may disconnect at any point. Closing this
                // one-request pipe instance is sufficient recovery.
            }
        }

        private static ValueTask WriteProtocolErrorAsync(
            Stream pipe,
            WeatherCompanionResponseStatus status,
            string detail,
            CancellationToken cancellationToken)
            => WeatherCompanionFraming.WriteFrameAsync(
                pipe,
                WeatherCompanionProtocol.SerializeResponse(status, detail: detail),
                WeatherCompanionProtocol.MaxResponseBytes,
                cancellationToken);

        private static async Task WaitForClientCloseAsync(
            NamedPipeServerStream pipe,
            CancellationToken cancellationToken)
        {
            byte[] trailingData = new byte[256];
            while (await pipe.ReadAsync(trailingData, cancellationToken).ConfigureAwait(false) != 0)
            {
            }
        }

        private static string GetProtocolErrorName(WeatherCompanionProtocolError error)
            => error switch
            {
                WeatherCompanionProtocolError.EmptyRequest => "empty-request",
                WeatherCompanionProtocolError.RequestTooLarge => "request-too-large",
                WeatherCompanionProtocolError.InvalidJson => "invalid-json",
                WeatherCompanionProtocolError.MissingVersion => "missing-version",
                WeatherCompanionProtocolError.UnsupportedVersion => "unsupported-version",
                WeatherCompanionProtocolError.MissingCommand => "missing-command",
                WeatherCompanionProtocolError.UnsupportedCommand => "unsupported-command",
                WeatherCompanionProtocolError.InvalidArguments => "invalid-arguments",
                _ => "invalid-request"
            };

        private static WeatherCompanionClientContext GetClientContext(
            NamedPipeServerStream pipe)
        {
            try
            {
                return GetNamedPipeClientProcessId(
                        pipe.SafePipeHandle,
                        out uint processId)
                    ? new WeatherCompanionClientContext(processId)
                    : default;
            }
            catch (Exception ex) when (
                ex is InvalidOperationException or ObjectDisposedException)
            {
                return default;
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetNamedPipeClientProcessId(
            SafePipeHandle pipe,
            out uint clientProcessId);
    }
}
