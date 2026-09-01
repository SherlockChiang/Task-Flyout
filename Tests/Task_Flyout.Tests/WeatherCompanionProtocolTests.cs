using System.Text;
using System.Text.Json;
using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class WeatherCompanionProtocolTests
{
    [Theory]
    [InlineData("ping", (int)WeatherCompanionCommand.Ping)]
    [InlineData("get-snapshot", (int)WeatherCompanionCommand.GetSnapshot)]
    [InlineData("open-weather", (int)WeatherCompanionCommand.OpenWeather)]
    public void Parses_supported_commands(string command, int expected)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = 1,
            command,
            ignored = new { nested = true }
        });

        Assert.True(WeatherCompanionProtocol.TryParseRequest(json, out var request, out var error));
        Assert.Equal(WeatherCompanionProtocolError.None, error);
        Assert.Equal(WeatherCompanionProtocol.Version, request.Version);
        Assert.Equal((WeatherCompanionCommand)expected, request.Command);
    }

    [Theory]
    [InlineData("ready", (int)WeatherCompanionMountState.Ready)]
    [InlineData("lost", (int)WeatherCompanionMountState.Lost)]
    public void Parses_bounded_mount_readiness_reports(
        string state,
        int expected)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = 1,
            command = "report-mount-readiness",
            controllerNonce = 17u,
            mountGeneration = 23ul,
            mountState = state
        });

        Assert.True(WeatherCompanionProtocol.TryParseRequest(
            json,
            out var request,
            out var error));
        Assert.Equal(WeatherCompanionProtocolError.None, error);
        Assert.Equal(
            WeatherCompanionCommand.ReportMountReadiness,
            request.Command);
        Assert.Equal(17u, request.ControllerNonce);
        Assert.Equal(23ul, request.MountGeneration);
        Assert.Equal((WeatherCompanionMountState)expected, request.MountState);
    }

    [Theory]
    [InlineData("{\"version\":1,\"command\":\"report-mount-readiness\"}")]
    [InlineData("{\"version\":1,\"command\":\"report-mount-readiness\",\"controllerNonce\":0,\"mountGeneration\":1,\"mountState\":\"ready\"}")]
    [InlineData("{\"version\":1,\"command\":\"report-mount-readiness\",\"controllerNonce\":1,\"mountGeneration\":0,\"mountState\":\"ready\"}")]
    [InlineData("{\"version\":1,\"command\":\"report-mount-readiness\",\"controllerNonce\":1,\"mountGeneration\":1,\"mountState\":\"pending\"}")]
    [InlineData("{\"version\":1,\"command\":\"ping\",\"controllerNonce\":1}")]
    public void Rejects_missing_or_cross_command_mount_arguments(string json)
    {
        Assert.False(WeatherCompanionProtocol.TryParseRequest(
            Encoding.UTF8.GetBytes(json),
            out _,
            out var error));
        Assert.Equal(WeatherCompanionProtocolError.InvalidArguments, error);
    }

    [Theory]
    [InlineData("", (int)WeatherCompanionProtocolError.EmptyRequest)]
    [InlineData("{}", (int)WeatherCompanionProtocolError.MissingVersion)]
    [InlineData("{\"version\":1}", (int)WeatherCompanionProtocolError.MissingCommand)]
    [InlineData("{\"version\":2,\"command\":\"ping\"}", (int)WeatherCompanionProtocolError.UnsupportedVersion)]
    [InlineData("{\"version\":1,\"command\":\"PING\"}", (int)WeatherCompanionProtocolError.UnsupportedCommand)]
    [InlineData("{\"version\":1,\"command\":\"unknown\"}", (int)WeatherCompanionProtocolError.UnsupportedCommand)]
    [InlineData("{\"version\":1,\"version\":1,\"command\":\"ping\"}", (int)WeatherCompanionProtocolError.InvalidJson)]
    [InlineData("{\"version\":1,\"command\":\"ping\",\"command\":\"ping\"}", (int)WeatherCompanionProtocolError.InvalidJson)]
    [InlineData("{\"version\":1,\"command\":", (int)WeatherCompanionProtocolError.InvalidJson)]
    [InlineData("[]", (int)WeatherCompanionProtocolError.InvalidJson)]
    [InlineData("{\"version\":1,\"command\":\"ping\"} true", (int)WeatherCompanionProtocolError.InvalidJson)]
    public void Rejects_invalid_requests(string json, int expected)
    {
        bool parsed = WeatherCompanionProtocol.TryParseRequest(
            Encoding.UTF8.GetBytes(json),
            out _,
            out var error);

        Assert.False(parsed);
        Assert.Equal((WeatherCompanionProtocolError)expected, error);
    }

    [Fact]
    public void Rejects_oversized_request_before_parsing()
    {
        byte[] bytes = new byte[WeatherCompanionProtocol.MaxRequestBytes + 1];
        Array.Fill(bytes, (byte)' ');

        Assert.False(WeatherCompanionProtocol.TryParseRequest(bytes, out _, out var error));
        Assert.Equal(WeatherCompanionProtocolError.RequestTooLarge, error);
    }

    [Fact]
    public void Accepts_request_at_exact_size_limit()
    {
        byte[] prefix = Encoding.UTF8.GetBytes("{\"version\":1,\"command\":\"ping\"}");
        byte[] request = new byte[WeatherCompanionProtocol.MaxRequestBytes];
        prefix.CopyTo(request, 0);
        Array.Fill(request, (byte)' ', prefix.Length, request.Length - prefix.Length);

        Assert.True(WeatherCompanionProtocol.TryParseRequest(request, out _, out var error));
        Assert.Equal(WeatherCompanionProtocolError.None, error);
    }

    [Fact]
    public void Rejects_malformed_utf8_and_excessive_depth()
    {
        byte[] malformedUtf8 =
        {
            (byte)'{', (byte)'"', 0xC3, 0x28, (byte)'"', (byte)':', (byte)'1', (byte)'}'
        };
        string tooDeep = new string('[', 9) + new string(']', 9);

        Assert.False(WeatherCompanionProtocol.TryParseRequest(
            malformedUtf8, out _, out var malformedError));
        Assert.Equal(WeatherCompanionProtocolError.InvalidJson, malformedError);
        Assert.False(WeatherCompanionProtocol.TryParseRequest(
            Encoding.UTF8.GetBytes(tooDeep), out _, out var depthError));
        Assert.Equal(WeatherCompanionProtocolError.InvalidJson, depthError);
    }

    [Fact]
    public void Snapshot_normalization_preserves_emoji_and_bounds_fields()
    {
        var snapshot = WeatherCompanionProtocol.CreateSnapshot(
            "☀️\0",
            new string('2', 40) + "°C",
            "Clear\r\n\u2028\u2029" + new string('x', 120),
            " Shanghai ",
            "Storm\twarning\u202Ehidden\u2069",
            new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.FromHours(8)));

        Assert.StartsWith("☀️", snapshot.Icon);
        Assert.DoesNotContain('\0', snapshot.Icon);
        Assert.Equal(32, snapshot.Temperature.EnumerateRunes().Count());
        Assert.DoesNotContain('\r', snapshot.Description);
        Assert.DoesNotContain('\n', snapshot.Description);
        Assert.DoesNotContain('\u2028', snapshot.Description);
        Assert.DoesNotContain('\u2029', snapshot.Description);
        Assert.Equal("Shanghai", snapshot.Location);
        Assert.Equal("Storm warninghidden", snapshot.Alert);
        Assert.DoesNotContain('\u202E', snapshot.Alert);
        Assert.DoesNotContain('\u2069', snapshot.Alert);
        Assert.Equal(TimeSpan.Zero, snapshot.UpdatedUtc.Offset);
    }

    [Fact]
    public void Serializes_bounded_snapshot_response_with_stable_schema()
    {
        var snapshot = WeatherCompanionProtocol.CreateSnapshot(
            "☀",
            "26°C",
            "Clear",
            "Shanghai",
            "",
            DateTimeOffset.Parse("2026-08-09T04:00:00Z"));

        byte[] json = WeatherCompanionProtocol.SerializeResponse(
            WeatherCompanionResponseStatus.Ok,
            snapshot,
            "ready");

        Assert.InRange(json.Length, 1, WeatherCompanionProtocol.MaxResponseBytes);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal("ok", root.GetProperty("status").GetString());
        Assert.Equal("ready", root.GetProperty("detail").GetString());
        Assert.Equal("☀", root.GetProperty("snapshot").GetProperty("icon").GetString());
        Assert.Equal("26°C", root.GetProperty("snapshot").GetProperty("temperature").GetString());
        Assert.Equal(
            DateTimeOffset.Parse("2026-08-09T04:00:00Z"),
            root.GetProperty("snapshot").GetProperty("updatedUtc").GetDateTimeOffset());
    }

    [Fact]
    public void Error_response_omits_snapshot()
    {
        byte[] json = WeatherCompanionProtocol.SerializeResponse(
            WeatherCompanionResponseStatus.InvalidRequest,
            detail: "bad-request");

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal("invalid-request", document.RootElement.GetProperty("status").GetString());
        Assert.False(document.RootElement.TryGetProperty("snapshot", out _));
    }

    [Theory]
    [InlineData(-30, true)]
    [InlineData(-120, true)]
    [InlineData(-121, false)]
    [InlineData(4, true)]
    [InlineData(6, false)]
    public void Snapshot_freshness_bounds_stale_and_future_data(
        int ageMinutes,
        bool expected)
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-09T04:00:00Z");
        var snapshot = WeatherCompanionProtocol.CreateSnapshot(
            "☀",
            "26°C",
            "Clear",
            "Shanghai",
            "",
            now + TimeSpan.FromMinutes(ageMinutes));

        Assert.Equal(expected, WeatherCompanionProtocol.IsSnapshotFresh(
            snapshot,
            now,
            TimeSpan.FromHours(2)));
    }

    [Fact]
    public void Snapshot_without_timestamp_is_not_fresh()
    {
        var snapshot = WeatherCompanionProtocol.CreateSnapshot(
            "☀", "26°C", "Clear", "Shanghai", "", DateTimeOffset.MinValue);

        Assert.False(WeatherCompanionProtocol.IsSnapshotFresh(
            snapshot,
            DateTimeOffset.UtcNow,
            TimeSpan.FromHours(2)));
    }
}
