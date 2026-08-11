using System;
using System.Text;
using System.Text.Json;

namespace Task_Flyout.Services;

/// <summary>
/// The small, privacy-safe result surface exposed by the standalone taskbar broker.
/// The native response contains paths, process identifiers, and implementation details;
/// none of those values are retained here.
/// </summary>
internal enum StandaloneTaskbarBrokerResultKind
{
    BinaryMissing,
    ProbeSupported,
    Unsupported,
    TemporarilyUnavailable,
    ControllerActiveUnverified,
    ControllerInactive,
    Rejected,
    Ambiguous,
    Failed,
    InvalidResponse
}

internal enum StandaloneTaskbarBrokerCommand
{
    Start,
    Stop
}

internal readonly record struct StandaloneTaskbarBrokerResult(
    StandaloneTaskbarBrokerResultKind Kind)
{
    public bool IsSuccessful
        => Kind is StandaloneTaskbarBrokerResultKind.ProbeSupported
            or StandaloneTaskbarBrokerResultKind.ControllerActiveUnverified
            or StandaloneTaskbarBrokerResultKind.ControllerInactive;
}

/// <summary>
/// Parses the one-line JSON contract emitted by TaskFlyout.TaskbarBroker.exe.
/// This parser deliberately accepts only the command/status fields needed by the
/// app and never returns native paths, identifiers, or free-form detail text.
/// </summary>
internal static class StandaloneTaskbarBrokerProtocol
{
    public const int MaxResponseBytes = 128 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private const string Supported = "supported";
    private const string Acknowledged = "acknowledged";
    private const string ControllerRejected = "controller-rejected";

    public static StandaloneTaskbarBrokerResult ParseProbe(
        string? standardOutput,
        int exitCode)
    {
        if (exitCode is 64 or 70)
            return new(StandaloneTaskbarBrokerResultKind.Failed);

        if (exitCode is not (0 or 2))
            return new(StandaloneTaskbarBrokerResultKind.Failed);

        if (!TryParseResponse(standardOutput, out var response))
            return new(StandaloneTaskbarBrokerResultKind.InvalidResponse);

        if (string.Equals(response.Status, Supported, StringComparison.Ordinal))
        {
            return exitCode == 0
                ? new(StandaloneTaskbarBrokerResultKind.ProbeSupported)
                : new(StandaloneTaskbarBrokerResultKind.Failed);
        }

        if (!IsKnownProbeStatus(response.Status))
            return new(StandaloneTaskbarBrokerResultKind.InvalidResponse);

        // The app invokes probe with --strict.  A non-supported profile must
        // therefore use the broker's documented exit code 2.
        if (exitCode != 2)
            return new(StandaloneTaskbarBrokerResultKind.Failed);

        return new(IsTemporarilyUnavailable(response.Status)
            ? StandaloneTaskbarBrokerResultKind.TemporarilyUnavailable
            : StandaloneTaskbarBrokerResultKind.Unsupported);
    }

    public static StandaloneTaskbarBrokerResult ParseControl(
        StandaloneTaskbarBrokerCommand expectedCommand,
        string? standardOutput,
        int exitCode)
    {
        if (expectedCommand is not StandaloneTaskbarBrokerCommand.Start and
            not StandaloneTaskbarBrokerCommand.Stop)
        {
            return new(StandaloneTaskbarBrokerResultKind.Failed);
        }

        if (exitCode is 64 or 70)
            return new(StandaloneTaskbarBrokerResultKind.Failed);

        if (exitCode is not (0 or 2))
            return new(StandaloneTaskbarBrokerResultKind.Failed);

        if (!TryParseResponse(standardOutput, out var response))
            return new(StandaloneTaskbarBrokerResultKind.InvalidResponse);

        string expectedCommandName = expectedCommand ==
            StandaloneTaskbarBrokerCommand.Start
            ? "start"
            : "stop";
        if (!string.Equals(
                response.Command,
                expectedCommandName,
                StringComparison.Ordinal))
        {
            return new(StandaloneTaskbarBrokerResultKind.InvalidResponse);
        }

        if (string.Equals(response.Status, Acknowledged, StringComparison.Ordinal))
        {
            if (exitCode != 0)
                return new(StandaloneTaskbarBrokerResultKind.Failed);

            bool validControllerState = expectedCommand ==
                StandaloneTaskbarBrokerCommand.Start
                ? response.ControllerStatus is "started" or "already-started"
                : response.ControllerStatus is "stopped" or "not-started";
            if (!validControllerState)
                return new(StandaloneTaskbarBrokerResultKind.InvalidResponse);

            return new(expectedCommand == StandaloneTaskbarBrokerCommand.Start
                ? StandaloneTaskbarBrokerResultKind.ControllerActiveUnverified
                : StandaloneTaskbarBrokerResultKind.ControllerInactive);
        }

        if (string.Equals(
                response.Status,
                ControllerRejected,
                StringComparison.Ordinal))
        {
            if (exitCode != 2)
                return new(StandaloneTaskbarBrokerResultKind.Failed);

            string expectedRejection = expectedCommand ==
                StandaloneTaskbarBrokerCommand.Start
                ? "start-rejected"
                : "stop-rejected";
            return string.Equals(
                    response.ControllerStatus,
                    expectedRejection,
                    StringComparison.Ordinal)
                ? new(StandaloneTaskbarBrokerResultKind.Rejected)
                : new(StandaloneTaskbarBrokerResultKind.InvalidResponse);
        }

        if (!IsKnownControlStatus(response.Status))
            return new(StandaloneTaskbarBrokerResultKind.InvalidResponse);

        if (exitCode != 2)
            return new(StandaloneTaskbarBrokerResultKind.Failed);

        if (response.Status == "probe-rejected")
        {
            if (!IsKnownProbeStatus(response.ProbeStatus) ||
                response.ProbeStatus == Supported)
            {
                return new(StandaloneTaskbarBrokerResultKind.InvalidResponse);
            }

            return new(IsTemporarilyUnavailable(response.ProbeStatus)
                ? StandaloneTaskbarBrokerResultKind.TemporarilyUnavailable
                : StandaloneTaskbarBrokerResultKind.Unsupported);
        }

        if (response.Status == "taskbar-window-changed")
            return new(StandaloneTaskbarBrokerResultKind.TemporarilyUnavailable);

        if (IsAmbiguousControlStatus(response.Status))
            return new(StandaloneTaskbarBrokerResultKind.Ambiguous);

        return new(StandaloneTaskbarBrokerResultKind.Failed);
    }

    private static bool TryParseResponse(
        string? standardOutput,
        out ParsedResponse response)
    {
        response = default;
        if (string.IsNullOrWhiteSpace(standardOutput))
            return false;

        byte[] utf8;
        try
        {
            if (StrictUtf8.GetByteCount(standardOutput) > MaxResponseBytes)
                return false;
            utf8 = StrictUtf8.GetBytes(standardOutput);
        }
        catch (EncoderFallbackException)
        {
            return false;
        }

        if (utf8.Length == 0 || utf8.Length > MaxResponseBytes)
            return false;

        try
        {
            return TryParseResponseObject(utf8, out response);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    // Kept separate so the property-name token is consumed before its value.
    private static bool TryParseResponseObject(
        ReadOnlySpan<byte> utf8,
        out ParsedResponse response)
    {
        response = default;
        var reader = new Utf8JsonReader(
            utf8,
            new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8
            });

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            return false;

        string? status = null;
        string? command = null;
        string? controllerStatus = null;
        string? probeStatus = null;
        bool sawStatus = false;
        bool sawCommand = false;
        bool sawControllerStatus = false;
        bool sawProbeStatus = false;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                return false;

            bool isStatus = reader.ValueTextEquals("status"u8);
            bool isCommand = reader.ValueTextEquals("command"u8);
            bool isControllerStatus = reader.ValueTextEquals("controllerStatus"u8);
            bool isProbeStatus = reader.ValueTextEquals("probeStatus"u8);

            if (!reader.Read())
                return false;

            if (isStatus || isCommand || isControllerStatus || isProbeStatus)
            {
                if (reader.TokenType != JsonTokenType.String)
                    return false;

                string? value = reader.GetString();
                if (value is null)
                    return false;

                if (isStatus)
                {
                    if (sawStatus) return false;
                    sawStatus = true;
                    status = value;
                }
                else if (isCommand)
                {
                    if (sawCommand) return false;
                    sawCommand = true;
                    command = value;
                }
                else if (isControllerStatus)
                {
                    if (sawControllerStatus) return false;
                    sawControllerStatus = true;
                    controllerStatus = value;
                }
                else
                {
                    if (sawProbeStatus) return false;
                    sawProbeStatus = true;
                    probeStatus = value;
                }
            }
            else
            {
                reader.Skip();
            }
        }

        if (reader.TokenType != JsonTokenType.EndObject || reader.Read())
            return false;
        if (!sawStatus || string.IsNullOrEmpty(status))
            return false;

        response = new ParsedResponse(status, command, controllerStatus, probeStatus);
        return true;
    }

    private static bool IsKnownProbeStatus(string? status)
        => status is "supported"
            or "taskbar-window-missing"
            or "taskbar-owner-unavailable"
            or "taskbar-owner-not-explorer"
            or "session-mismatch"
            or "taskbar-view-missing"
            or "taskbar-view-unreadable"
            or "unsupported-windows-build"
            or "taskbar-view-not-allowlisted"
            or "taskbar-hook-target-mismatch";

    private static bool IsTemporarilyUnavailable(string? status)
        => status is "taskbar-window-missing"
            or "taskbar-owner-unavailable"
            or "session-mismatch"
            or "taskbar-view-missing";

    private static bool IsKnownControlStatus(string? status)
        => status is "invalid-command"
            or "probe-rejected"
            or "taskbar-window-changed"
            or "message-registration-failed"
            or "host-path-unavailable"
            or "host-load-failed"
            or "host-api-mismatch"
            or "entry-hook-unavailable"
            or "hook-install-failed"
            or "message-dispatch-failed"
            or "hook-remove-failed"
            or "acknowledgement-window-failed"
            or "acknowledgement-timed-out"
            or "acknowledgement-wait-failed"
            or "acknowledgement-invalid";

    private static bool IsAmbiguousControlStatus(string? status)
        => status is "message-dispatch-failed"
            or "hook-remove-failed"
            or "acknowledgement-timed-out"
            or "acknowledgement-wait-failed"
            or "acknowledgement-invalid";

    private readonly record struct ParsedResponse(
        string Status,
        string? Command,
        string? ControllerStatus,
        string? ProbeStatus);
}
