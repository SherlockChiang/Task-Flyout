using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class WeatherCompanionServerTests
{
    [Fact]
    public async Task Framing_round_trips_a_bounded_payload()
    {
        byte[] payload = Encoding.UTF8.GetBytes("weather");
        await using var stream = new MemoryStream();

        await WeatherCompanionFraming.WriteFrameAsync(stream, payload, 64, CancellationToken.None);
        stream.Position = 0;
        byte[]? decoded = await WeatherCompanionFraming.ReadFrameAsync(
            stream, 64, CancellationToken.None);

        Assert.Equal(payload, decoded);
    }

    [Fact]
    public async Task Framing_rejects_oversized_and_truncated_payloads()
    {
        byte[] oversizedHeader = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(oversizedHeader, 65);
        await using var oversized = new MemoryStream(oversizedHeader);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await WeatherCompanionFraming.ReadFrameAsync(
                oversized, 64, CancellationToken.None));

        byte[] truncatedFrame = new byte[6];
        BinaryPrimitives.WriteUInt32LittleEndian(truncatedFrame, 4);
        await using var truncated = new MemoryStream(truncatedFrame);
        await Assert.ThrowsAsync<EndOfStreamException>(async () =>
            await WeatherCompanionFraming.ReadFrameAsync(
                truncated, 64, CancellationToken.None));
    }

    [Fact]
    public void Pipe_name_is_versioned_and_scoped_to_the_user_sid()
    {
        Assert.Equal(
            "TaskFlyout.Weather.v1.S-1-5-21-123.7",
            WeatherCompanionServer.BuildPipeName("S-1-5-21-123", 7));
        Assert.Throws<ArgumentException>(() =>
            WeatherCompanionServer.BuildPipeName("bad/name", 7));
        Assert.Throws<ArgumentException>(() =>
            WeatherCompanionServer.BuildPipeName("S-1-5-21-123", -1));
    }

    [Fact]
    public async Task Server_handles_one_framed_request_and_response()
    {
        string pipeName = $"TaskFlyout.Weather.Tests.{Guid.NewGuid():N}";
        WeatherCompanionCommand? received = null;
        uint clientProcessId = 0;
        await using var server = new WeatherCompanionServer(
            (request, clientContext, _) =>
            {
                received = request.Command;
                clientProcessId = clientContext.ProcessId;
                return ValueTask.FromResult(WeatherCompanionProtocol.SerializeResponse(
                    WeatherCompanionResponseStatus.Ok,
                    detail: "ready"));
            },
            pipeName,
            operationTimeout: TimeSpan.FromSeconds(2),
            retryDelay: TimeSpan.FromMilliseconds(10));
        server.Start();

        using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await client.ConnectAsync(timeout.Token);
        byte[] request = Encoding.UTF8.GetBytes("{\"version\":1,\"command\":\"ping\"}");
        await WeatherCompanionFraming.WriteFrameAsync(
            client, request, WeatherCompanionProtocol.MaxRequestBytes, timeout.Token);
        byte[]? response = await WeatherCompanionFraming.ReadFrameAsync(
            client, WeatherCompanionProtocol.MaxResponseBytes, timeout.Token);

        Assert.NotNull(response);
        using JsonDocument document = JsonDocument.Parse(response);
        Assert.Equal("ok", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(WeatherCompanionCommand.Ping, received);
        Assert.Equal((uint)Environment.ProcessId, clientProcessId);
    }

    [Fact]
    public async Task Server_rejects_oversized_frame_before_allocating_payload()
    {
        string pipeName = $"TaskFlyout.Weather.Tests.{Guid.NewGuid():N}";
        await using var server = new WeatherCompanionServer(
            (_, _) => throw new InvalidOperationException("handler must not run"),
            pipeName,
            operationTimeout: TimeSpan.FromSeconds(2),
            retryDelay: TimeSpan.FromMilliseconds(10));
        server.Start();

        using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await client.ConnectAsync(timeout.Token);
        byte[] header = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(
            header,
            WeatherCompanionProtocol.MaxRequestBytes + 1u);
        await client.WriteAsync(header, timeout.Token);
        await client.FlushAsync(timeout.Token);
        byte[]? response = await WeatherCompanionFraming.ReadFrameAsync(
            client, WeatherCompanionProtocol.MaxResponseBytes, timeout.Token);

        Assert.NotNull(response);
        using JsonDocument document = JsonDocument.Parse(response);
        Assert.Equal("invalid-request", document.RootElement.GetProperty("status").GetString());
        Assert.Equal("request-too-large", document.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Dispose_cancels_a_connected_slow_client()
    {
        string pipeName = $"TaskFlyout.Weather.Tests.{Guid.NewGuid():N}";
        var server = new WeatherCompanionServer(
            (_, _) => ValueTask.FromResult(Array.Empty<byte>()),
            pipeName,
            operationTimeout: TimeSpan.FromSeconds(30),
            retryDelay: TimeSpan.FromMilliseconds(10));
        server.Start();

        using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await client.ConnectAsync(connectTimeout.Token);

        await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Server_accepts_next_client_after_partial_frame_timeout()
    {
        string pipeName = $"TaskFlyout.Weather.Tests.{Guid.NewGuid():N}";
        await using var server = new WeatherCompanionServer(
            (_, _) => ValueTask.FromResult(WeatherCompanionProtocol.SerializeResponse(
                WeatherCompanionResponseStatus.Ok)),
            pipeName,
            operationTimeout: TimeSpan.FromMilliseconds(250),
            retryDelay: TimeSpan.FromMilliseconds(10));
        server.Start();

        using (var slowClient = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        {
            using var slowTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await slowClient.ConnectAsync(slowTimeout.Token);
            await slowClient.WriteAsync(new byte[] { 1, 0 }, slowTimeout.Token);
            await slowClient.FlushAsync(slowTimeout.Token);
            await Task.Delay(500, slowTimeout.Token);
        }

        using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await client.ConnectAsync(timeout.Token);
        await WeatherCompanionFraming.WriteFrameAsync(
            client,
            Encoding.UTF8.GetBytes("{\"version\":1,\"command\":\"ping\"}"),
            WeatherCompanionProtocol.MaxRequestBytes,
            timeout.Token);
        byte[]? response = await WeatherCompanionFraming.ReadFrameAsync(
            client,
            WeatherCompanionProtocol.MaxResponseBytes,
            timeout.Token);

        Assert.NotNull(response);
        using JsonDocument document = JsonDocument.Parse(response);
        Assert.Equal("ok", document.RootElement.GetProperty("status").GetString());
    }
}
