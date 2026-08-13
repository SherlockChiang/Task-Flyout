using System;
using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class StandaloneTaskbarBrokerProtocolTests
{
    [Fact]
    public void Supported_probe_requires_strict_success_exit_code()
    {
        var result = StandaloneTaskbarBrokerProtocol.ParseProbe(
            ProbeJson("supported"),
            0);

        Assert.Equal(
            StandaloneTaskbarBrokerResultKind.ProbeSupported,
            result.Kind);
    }

    [Theory]
    [InlineData("taskbar-window-missing", (int)StandaloneTaskbarBrokerResultKind.TemporarilyUnavailable)]
    [InlineData("taskbar-owner-unavailable", (int)StandaloneTaskbarBrokerResultKind.TemporarilyUnavailable)]
    [InlineData("session-mismatch", (int)StandaloneTaskbarBrokerResultKind.TemporarilyUnavailable)]
    [InlineData("taskbar-view-missing", (int)StandaloneTaskbarBrokerResultKind.TemporarilyUnavailable)]
    [InlineData("taskbar-owner-not-explorer", (int)StandaloneTaskbarBrokerResultKind.Unsupported)]
    [InlineData("taskbar-view-unreadable", (int)StandaloneTaskbarBrokerResultKind.Unsupported)]
    [InlineData("unsupported-windows-build", (int)StandaloneTaskbarBrokerResultKind.Unsupported)]
    [InlineData("taskbar-view-not-allowlisted", (int)StandaloneTaskbarBrokerResultKind.Unsupported)]
    [InlineData("taskbar-hook-target-mismatch", (int)StandaloneTaskbarBrokerResultKind.Unsupported)]
    public void Strict_probe_maps_known_profile_statuses(
        string status,
        int expected)
    {
        var result = StandaloneTaskbarBrokerProtocol.ParseProbe(
            ProbeJson(status),
            2);

        Assert.Equal((StandaloneTaskbarBrokerResultKind)expected, result.Kind);
    }

    [Fact]
    public void Probe_rejects_non_strict_or_inconsistent_exit_codes()
    {
        Assert.Equal(
            StandaloneTaskbarBrokerResultKind.Failed,
            StandaloneTaskbarBrokerProtocol.ParseProbe(
                ProbeJson("supported"),
                2).Kind);
        Assert.Equal(
            StandaloneTaskbarBrokerResultKind.Failed,
            StandaloneTaskbarBrokerProtocol.ParseProbe(
                ProbeJson("taskbar-window-missing"),
                0).Kind);
    }

    [Fact]
    public void Start_accepts_started_and_idempotent_started_results()
    {
        foreach (string controllerStatus in new[] { "started", "already-started" })
        {
            var result = StandaloneTaskbarBrokerProtocol.ParseControl(
                StandaloneTaskbarBrokerCommand.Start,
                ControlJson("acknowledged", "start", controllerStatus),
                0);

            Assert.Equal(
                StandaloneTaskbarBrokerResultKind.ControllerActiveUnverified,
                result.Kind);
            Assert.Equal(
                new StandaloneTaskbarControllerIdentity(1, 5),
                result.ControllerIdentity);
        }
    }

    [Fact]
    public void Older_start_acknowledgement_remains_unverified_without_a_lease_identity()
    {
        var result = StandaloneTaskbarBrokerProtocol.ParseControl(
            StandaloneTaskbarBrokerCommand.Start,
            "{\"status\":\"acknowledged\",\"command\":\"start\"," +
            "\"processId\":1,\"controllerStatus\":\"started\"}",
            0);

        Assert.Equal(
            StandaloneTaskbarBrokerResultKind.ControllerActiveUnverified,
            result.Kind);
        Assert.Null(result.ControllerIdentity);
    }

    [Fact]
    public void Stop_accepts_stopped_and_idempotent_not_started_results()
    {
        foreach (string controllerStatus in new[] { "stopped", "not-started" })
        {
            var result = StandaloneTaskbarBrokerProtocol.ParseControl(
                StandaloneTaskbarBrokerCommand.Stop,
                ControlJson("acknowledged", "stop", controllerStatus),
                0);

            Assert.Equal(
                StandaloneTaskbarBrokerResultKind.ControllerInactive,
                result.Kind);
        }
    }

    [Theory]
    [InlineData("mount-ready", (int)StandaloneTaskbarBrokerResultKind.ControllerMountReady)]
    [InlineData("mount-pending", (int)StandaloneTaskbarBrokerResultKind.ControllerActiveUnverified)]
    [InlineData("not-started", (int)StandaloneTaskbarBrokerResultKind.ControllerInactive)]
    public void Status_accepts_only_bounded_mount_states(
        string controllerStatus,
        int expected)
    {
        var result = StandaloneTaskbarBrokerProtocol.ParseControl(
            StandaloneTaskbarBrokerCommand.Status,
            ControlJson("acknowledged", "status", controllerStatus),
            0);

        Assert.Equal((StandaloneTaskbarBrokerResultKind)expected, result.Kind);
    }

    [Theory]
    [InlineData("tree-profile-mismatch")]
    [InlineData("detour-target-not-observed")]
    [InlineData("detour-inactive")]
    [InlineData("callback-unavailable")]
    [InlineData("callback-reentrant")]
    [InlineData("callback-recheck-race")]
    [InlineData("bootstrap-window-invalid")]
    [InlineData("bootstrap-owner-thread-mismatch")]
    [InlineData("bootstrap-host-bounds-invalid")]
    [InlineData("bootstrap-query-failed")]
    [InlineData("bootstrap-enumeration-overflow")]
    [InlineData("bootstrap-frame-not-observed")]
    [InlineData("bootstrap-frame-ambiguous")]
    [InlineData("bootstrap-tree-profile-mismatch")]
    [InlineData("bootstrap-frame-validated")]
    [InlineData("bootstrap-host-query-failed")]
    [InlineData("bootstrap-enumeration-failed")]
    [InlineData("bootstrap-class-inspection-failed")]
    [InlineData("bootstrap-identity-projection-failed")]
    [InlineData("bootstrap-tree-probe-unavailable")]
    [InlineData("bootstrap-root-unavailable")]
    [InlineData("bootstrap-root-query-failed")]
    [InlineData("bootstrap-root-not-associated-with-taskbar")]
    [InlineData("bootstrap-root-frame-not-observed")]
    [InlineData("bootstrap-root-frame-ambiguous")]
    [InlineData("bootstrap-root-tree-profile-mismatch")]
    [InlineData("root-bootstrap-validated")]
    [InlineData("bootstrap-root-enumeration-overflow")]
    public void Additive_controller_diagnostic_preserves_older_parser_behavior(
        string controllerDiagnostic)
    {
        var result = StandaloneTaskbarBrokerProtocol.ParseControl(
            StandaloneTaskbarBrokerCommand.Status,
            ControlJson("acknowledged", "status", "mount-pending")
                .Replace(
                    "\"probeStatus\"",
                    $"\"controllerDiagnostic\":\"{controllerDiagnostic}\"," +
                    "\"probeStatus\"",
                    StringComparison.Ordinal),
            0);

        Assert.Equal(
            StandaloneTaskbarBrokerResultKind.ControllerActiveUnverified,
            result.Kind);
    }

    [Fact]
    public void Control_rejects_cross_command_and_wrong_controller_family()
    {
        Assert.Equal(
            StandaloneTaskbarBrokerResultKind.InvalidResponse,
            StandaloneTaskbarBrokerProtocol.ParseControl(
                StandaloneTaskbarBrokerCommand.Start,
                ControlJson("acknowledged", "stop", "stopped"),
                0).Kind);
        Assert.Equal(
            StandaloneTaskbarBrokerResultKind.InvalidResponse,
            StandaloneTaskbarBrokerProtocol.ParseControl(
                StandaloneTaskbarBrokerCommand.Start,
                ControlJson("acknowledged", "start", "stopped"),
                0).Kind);
        Assert.Equal(
            StandaloneTaskbarBrokerResultKind.InvalidResponse,
            StandaloneTaskbarBrokerProtocol.ParseControl(
                StandaloneTaskbarBrokerCommand.Stop,
                ControlJson("acknowledged", "stop", "started"),
                0).Kind);
        Assert.Equal(
            StandaloneTaskbarBrokerResultKind.InvalidResponse,
            StandaloneTaskbarBrokerProtocol.ParseControl(
                StandaloneTaskbarBrokerCommand.Status,
                ControlJson("acknowledged", "status", "started"),
                0).Kind);
    }

    [Fact]
    public void Controller_rejection_is_distinct_and_command_specific()
    {
        Assert.Equal(
            StandaloneTaskbarBrokerResultKind.Rejected,
            StandaloneTaskbarBrokerProtocol.ParseControl(
                StandaloneTaskbarBrokerCommand.Start,
                ControlJson("controller-rejected", "start", "start-rejected"),
                2).Kind);
        Assert.Equal(
            StandaloneTaskbarBrokerResultKind.Rejected,
            StandaloneTaskbarBrokerProtocol.ParseControl(
                StandaloneTaskbarBrokerCommand.Stop,
                ControlJson("controller-rejected", "stop", "stop-rejected"),
                2).Kind);
        Assert.Equal(
            StandaloneTaskbarBrokerResultKind.InvalidResponse,
            StandaloneTaskbarBrokerProtocol.ParseControl(
                StandaloneTaskbarBrokerCommand.Start,
                ControlJson("controller-rejected", "start", "stop-rejected"),
                2).Kind);
        Assert.Equal(
            StandaloneTaskbarBrokerResultKind.Rejected,
            StandaloneTaskbarBrokerProtocol.ParseControl(
                StandaloneTaskbarBrokerCommand.Status,
                ControlJson("controller-rejected", "status", "status-rejected"),
                2).Kind);
    }

    [Theory]
    [InlineData("message-dispatch-failed")]
    [InlineData("hook-remove-failed")]
    [InlineData("acknowledgement-timed-out")]
    [InlineData("acknowledgement-wait-failed")]
    [InlineData("acknowledgement-invalid")]
    public void Dispatch_and_acknowledgement_failures_are_ambiguous(string status)
    {
        var result = StandaloneTaskbarBrokerProtocol.ParseControl(
            StandaloneTaskbarBrokerCommand.Start,
            ControlJson(status, "start", "unknown"),
            2);

        Assert.Equal(
            StandaloneTaskbarBrokerResultKind.Ambiguous,
            result.Kind);
    }

    [Fact]
    public void Control_probe_rejection_preserves_only_safe_category()
    {
        Assert.Equal(
            StandaloneTaskbarBrokerResultKind.TemporarilyUnavailable,
            StandaloneTaskbarBrokerProtocol.ParseControl(
                StandaloneTaskbarBrokerCommand.Start,
                ControlJson(
                    "probe-rejected",
                    "start",
                    "unknown",
                    "taskbar-window-missing"),
                2).Kind);
        Assert.Equal(
            StandaloneTaskbarBrokerResultKind.Unsupported,
            StandaloneTaskbarBrokerProtocol.ParseControl(
                StandaloneTaskbarBrokerCommand.Start,
                ControlJson(
                    "probe-rejected",
                    "start",
                    "unknown",
                    "taskbar-view-not-allowlisted"),
                2).Kind);
        Assert.Equal(
            StandaloneTaskbarBrokerResultKind.InvalidResponse,
            StandaloneTaskbarBrokerProtocol.ParseControl(
                StandaloneTaskbarBrokerCommand.Start,
                ControlJson(
                    "probe-rejected",
                    "start",
                    "unknown",
                    "supported"),
                2).Kind);
    }

    [Fact]
    public void Invalid_requested_command_fails_closed()
    {
        var result = StandaloneTaskbarBrokerProtocol.ParseControl(
            (StandaloneTaskbarBrokerCommand)99,
            ControlJson("acknowledged", "stop", "stopped"),
            0);

        Assert.Equal(StandaloneTaskbarBrokerResultKind.Failed, result.Kind);
    }

    [Theory]
    [InlineData(64)]
    [InlineData(70)]
    [InlineData(1)]
    public void Process_failures_do_not_require_or_expose_json(int exitCode)
    {
        var probe = StandaloneTaskbarBrokerProtocol.ParseProbe(null, exitCode);
        var control = StandaloneTaskbarBrokerProtocol.ParseControl(
            StandaloneTaskbarBrokerCommand.Stop,
            null,
            exitCode);

        Assert.Equal(StandaloneTaskbarBrokerResultKind.Failed, probe.Kind);
        Assert.Equal(StandaloneTaskbarBrokerResultKind.Failed, control.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[]")]
    [InlineData("{\"status\":\"supported\"} true")]
    [InlineData("{\"status\":\"supported\"}{\"status\":\"supported\"}")]
    [InlineData("{\"status\":\"supported\",")]
    [InlineData("{\"status\":\"supported\",}")]
    [InlineData("/*comment*/{\"status\":\"supported\"}")]
    public void Empty_malformed_or_multiple_json_is_invalid(string? output)
    {
        var result = StandaloneTaskbarBrokerProtocol.ParseProbe(output, 0);

        Assert.Equal(
            StandaloneTaskbarBrokerResultKind.InvalidResponse,
            result.Kind);
    }

    [Fact]
    public void Unknown_status_and_duplicate_contract_fields_are_invalid()
    {
        Assert.Equal(
            StandaloneTaskbarBrokerResultKind.InvalidResponse,
            StandaloneTaskbarBrokerProtocol.ParseProbe(
                ProbeJson("future-status"),
                2).Kind);
        Assert.Equal(
            StandaloneTaskbarBrokerResultKind.InvalidResponse,
            StandaloneTaskbarBrokerProtocol.ParseProbe(
                "{\"status\":\"supported\",\"status\":\"supported\"}",
                0).Kind);
        Assert.Equal(
            StandaloneTaskbarBrokerResultKind.InvalidResponse,
            StandaloneTaskbarBrokerProtocol.ParseControl(
                StandaloneTaskbarBrokerCommand.Start,
                "{\"status\":\"acknowledged\",\"command\":\"start\",\"command\":\"start\",\"controllerStatus\":\"started\"}",
                0).Kind);
        Assert.Equal(
            StandaloneTaskbarBrokerResultKind.InvalidResponse,
            StandaloneTaskbarBrokerProtocol.ParseControl(
                StandaloneTaskbarBrokerCommand.Start,
                "{\"status\":\"acknowledged\",\"command\":\"start\"," +
                "\"controllerStatus\":\"started\",\"processId\":1," +
                "\"controlNonce\":5,\"controlNonce\":5}",
                0).Kind);
    }

    [Fact]
    public void Contract_fields_must_have_string_values()
    {
        Assert.Equal(
            StandaloneTaskbarBrokerResultKind.InvalidResponse,
            StandaloneTaskbarBrokerProtocol.ParseProbe(
                "{\"status\":true}",
                0).Kind);
        Assert.Equal(
            StandaloneTaskbarBrokerResultKind.InvalidResponse,
            StandaloneTaskbarBrokerProtocol.ParseControl(
                StandaloneTaskbarBrokerCommand.Start,
                "{\"status\":\"acknowledged\",\"command\":\"start\",\"controllerStatus\":42}",
                0).Kind);
    }

    [Fact]
    public void Invalid_utf16_surrogates_fail_closed_without_throwing()
    {
        string malformed = "{\"status\":\"supported\"}" + '\uD800';

        var result = StandaloneTaskbarBrokerProtocol.ParseProbe(malformed, 0);

        Assert.Equal(
            StandaloneTaskbarBrokerResultKind.InvalidResponse,
            result.Kind);
    }

    [Fact]
    public void Oversized_input_is_rejected_before_json_processing()
    {
        string oversized = new string(
            ' ',
            StandaloneTaskbarBrokerProtocol.MaxResponseBytes + 1);

        var result = StandaloneTaskbarBrokerProtocol.ParseProbe(oversized, 0);

        Assert.Equal(
            StandaloneTaskbarBrokerResultKind.InvalidResponse,
            result.Kind);
    }

    [Fact]
    public void Native_detail_paths_and_ids_are_not_part_of_result()
    {
        const string output =
            "{\"status\":\"supported\",\"detail\":\"secret\"," +
            "\"processId\":123,\"threadId\":456," +
            "\"explorerPath\":\"C:\\\\private\\\\explorer.exe\"}";

        var result = StandaloneTaskbarBrokerProtocol.ParseProbe(output, 0);

        Assert.Equal(
            StandaloneTaskbarBrokerResultKind.ProbeSupported,
            result.Kind);
    }

    private static string ProbeJson(string status)
        => $"{{\"status\":\"{status}\",\"detail\":\"internal\",\"windowsBuild\":26200,\"processId\":1,\"threadId\":2,\"sessionId\":3,\"explorerPath\":\"C:\\\\Windows\\\\explorer.exe\",\"taskbarViewPath\":\"C:\\\\Windows\\\\Taskbar.View.dll\",\"machine\":34404,\"timeDateStamp\":1,\"sizeOfImage\":2,\"checksum\":3}}";

    private static string ControlJson(
        string status,
        string command,
        string controllerStatus,
        string probeStatus = "supported")
        => $"{{\"status\":\"{status}\",\"detail\":\"internal\",\"command\":\"{command}\",\"processId\":1,\"controlNonce\":5,\"threadId\":2,\"messageId\":3,\"acknowledgementMessageId\":4,\"controllerStatus\":\"{controllerStatus}\",\"probeStatus\":\"{probeStatus}\"}}";
}
