#include "taskbar_control_transport.h"

#include <cstdio>
#include <string_view>

namespace {

bool Expect(bool condition, const wchar_t* message) {
    if (condition) {
        return true;
    }
    fwprintf(stderr, L"FAILED: %ls\n", message);
    return false;
}

}  // namespace

int wmain() {
    using namespace taskflyout::taskbar;

    bool passed = true;
    passed &= Expect(
        kHostApiVersion == 8u &&
            static_cast<std::uint32_t>(
                HostControlDiagnostic::DetourTargetNotObserved) == 12u &&
            static_cast<std::uint32_t>(
                HostControlDiagnostic::DetourInactive) == 13u &&
            static_cast<std::uint32_t>(
                HostControlDiagnostic::CallbackUnavailable) == 14u &&
            static_cast<std::uint32_t>(
                HostControlDiagnostic::CallbackReentrant) == 15u &&
            static_cast<std::uint32_t>(
                HostControlDiagnostic::CallbackRecheckRace) == 16u &&
            static_cast<std::uint32_t>(
                HostControlDiagnostic::BootstrapWindowInvalid) == 17u &&
            static_cast<std::uint32_t>(
                HostControlDiagnostic::BootstrapOwnerThreadMismatch) ==
                18u &&
            static_cast<std::uint32_t>(
                HostControlDiagnostic::BootstrapHostBoundsInvalid) == 19u &&
            static_cast<std::uint32_t>(
                HostControlDiagnostic::BootstrapQueryFailed) == 20u &&
            static_cast<std::uint32_t>(
                HostControlDiagnostic::BootstrapEnumerationOverflow) ==
                21u &&
            static_cast<std::uint32_t>(
                HostControlDiagnostic::BootstrapFrameNotObserved) == 22u &&
            static_cast<std::uint32_t>(
                HostControlDiagnostic::BootstrapFrameAmbiguous) == 23u &&
            static_cast<std::uint32_t>(
                HostControlDiagnostic::BootstrapTreeProfileMismatch) ==
                24u &&
            static_cast<std::uint32_t>(
                HostControlDiagnostic::BootstrapFrameValidated) == 25u &&
            static_cast<std::uint32_t>(
                HostControlDiagnostic::BootstrapHostQueryFailed) == 26u &&
            static_cast<std::uint32_t>(
                HostControlDiagnostic::BootstrapEnumerationFailed) == 27u &&
            static_cast<std::uint32_t>(
                HostControlDiagnostic::BootstrapClassInspectionFailed) ==
                28u &&
            static_cast<std::uint32_t>(
                HostControlDiagnostic::BootstrapIdentityProjectionFailed) ==
                29u &&
            static_cast<std::uint32_t>(
                HostControlDiagnostic::BootstrapTreeProbeUnavailable) == 30u,
        L"host API and additive diagnostic values must remain protocol-stable");
    constexpr std::uint32_t nonce = 0xA17E52C3u;
    constexpr auto encodedStart = EncodeHostControlRequest(
        HostControlCommand::Start,
        nonce);
    constexpr auto decodedStart = DecodeHostControlRequest(encodedStart);
    passed &= Expect(
        decodedStart.command == HostControlCommand::Start &&
            decodedStart.nonce == nonce,
        L"the control envelope should preserve its command and nonce");
    constexpr auto encodedStatus = EncodeHostControlRequest(
        HostControlCommand::Status,
        nonce);
    constexpr auto decodedStatus = DecodeHostControlRequest(encodedStatus);
    passed &= Expect(
        decodedStatus.command == HostControlCommand::Status &&
            decodedStatus.nonce == nonce,
        L"the status envelope should preserve its command and nonce");
    constexpr auto encodedAcknowledgement =
        EncodeHostControlAcknowledgement(
            HostControlAcknowledgement::MountPending,
            HostControlDiagnostic::TreeProfileMismatch);
    constexpr auto decodedAcknowledgement =
        DecodeHostControlAcknowledgement(encodedAcknowledgement);
    passed &= Expect(
        decodedAcknowledgement.acknowledgement ==
                HostControlAcknowledgement::MountPending &&
            decodedAcknowledgement.diagnostic ==
                HostControlDiagnostic::TreeProfileMismatch &&
            static_cast<std::uint32_t>(encodedAcknowledgement) ==
                static_cast<std::uint32_t>(
                    HostControlAcknowledgement::MountPending),
        L"the acknowledgement envelope should preserve its low result and "
        L"high diagnostic words");
    constexpr auto decodedUnknownDiagnostic =
        DecodeHostControlAcknowledgement(
            (static_cast<std::uintptr_t>(0xFFFFFFFFu) << 32u) |
            static_cast<std::uintptr_t>(
                HostControlAcknowledgement::MountPending));
    passed &= Expect(
        decodedUnknownDiagnostic.acknowledgement ==
                HostControlAcknowledgement::MountPending &&
            static_cast<std::uint32_t>(
                decodedUnknownDiagnostic.diagnostic) == 0xFFFFFFFFu,
        L"an unknown diagnostic must not corrupt the acknowledgement word");
    passed &= Expect(
        IsHostControlAcknowledgementForCommand(
            HostControlCommand::Start,
            HostControlAcknowledgement::Started) &&
        IsHostControlAcknowledgementForCommand(
            HostControlCommand::Start,
            HostControlAcknowledgement::AlreadyStarted) &&
        IsHostControlAcknowledgementForCommand(
            HostControlCommand::Start,
            HostControlAcknowledgement::StartRejected),
        L"start should accept only start-family controller results");
    passed &= Expect(
        IsHostControlAcknowledgementForCommand(
            HostControlCommand::Status,
            HostControlAcknowledgement::MountReady) &&
        IsHostControlAcknowledgementForCommand(
            HostControlCommand::Status,
            HostControlAcknowledgement::MountPending) &&
        IsHostControlAcknowledgementForCommand(
            HostControlCommand::Status,
            HostControlAcknowledgement::NotStarted) &&
        IsHostControlAcknowledgementForCommand(
            HostControlCommand::Status,
            HostControlAcknowledgement::StatusRejected),
        L"status should accept only mount-state controller results");
    passed &= Expect(
        !IsHostControlAcknowledgementForCommand(
            HostControlCommand::Start,
            HostControlAcknowledgement::Stopped) &&
        !IsHostControlAcknowledgementForCommand(
            HostControlCommand::Stop,
            HostControlAcknowledgement::Started) &&
        !IsHostControlAcknowledgementForCommand(
            static_cast<HostControlCommand>(99),
            HostControlAcknowledgement::StartRejected),
        L"cross-command and unknown acknowledgements should fail closed");
    passed &= Expect(
        EvaluateHostControlAcknowledgement(
            HostControlCommand::Start,
            HostControlAcknowledgement::Started) ==
                HostControlDispatchStatus::Acknowledged &&
        EvaluateHostControlAcknowledgement(
            HostControlCommand::Stop,
            HostControlAcknowledgement::NotStarted) ==
                HostControlDispatchStatus::Acknowledged &&
        EvaluateHostControlAcknowledgement(
            HostControlCommand::Status,
            HostControlAcknowledgement::MountReady) ==
                HostControlDispatchStatus::Acknowledged,
        L"successful and idempotent controller outcomes should acknowledge");
    passed &= Expect(
        EvaluateHostControlAcknowledgement(
            HostControlCommand::Start,
            HostControlAcknowledgement::StartRejected) ==
                HostControlDispatchStatus::ControllerRejected &&
        EvaluateHostControlAcknowledgement(
            HostControlCommand::Start,
            HostControlAcknowledgement::Stopped) ==
                HostControlDispatchStatus::AcknowledgementInvalid &&
        EvaluateHostControlAcknowledgement(
            HostControlCommand::Status,
            HostControlAcknowledgement::StatusRejected) ==
                HostControlDispatchStatus::ControllerRejected,
        L"rejections and cross-command replies should remain distinct");

    const auto invalid = DispatchHostControl(
        static_cast<HostControlCommand>(99));
    passed &= Expect(
        invalid.status == HostControlDispatchStatus::InvalidCommand,
        L"the broker must reject unknown control commands before probing");
    passed &= Expect(
        std::wstring_view(HostControlDispatchStatusName(
            HostControlDispatchStatus::MessageDispatchFailed)) ==
            L"message-dispatch-failed",
        L"control failures should remain diagnosable");
    passed &= Expect(
        std::wstring_view(HostControlDispatchStatusName(
            HostControlDispatchStatus::Acknowledged)) == L"acknowledged" &&
        std::wstring_view(HostControlDispatchStatusName(
            HostControlDispatchStatus::ControllerRejected)) ==
                L"controller-rejected",
        L"acknowledged and rejected controller outcomes should be distinct");
    passed &= Expect(
        std::wstring_view(HostControlAcknowledgementName(
            HostControlAcknowledgement::AlreadyStarted)) ==
                L"already-started" &&
        std::wstring_view(HostControlAcknowledgementName(
            static_cast<HostControlAcknowledgement>(99))) == L"invalid",
        L"controller acknowledgement names should be stable and bounded");
    passed &= Expect(
        std::wstring_view(HostControlCommandName(
            HostControlCommand::Status)) == L"status" &&
        std::wstring_view(HostControlAcknowledgementName(
            HostControlAcknowledgement::MountReady)) == L"mount-ready",
        L"status command and mount acknowledgement names should be stable");
    passed &= Expect(
        std::wstring_view(HostControlDiagnosticName(
            HostControlDiagnostic::None)) == L"none" &&
        std::wstring_view(HostControlDiagnosticName(
            HostControlDiagnostic::AwaitingLayout)) == L"awaiting-layout" &&
        std::wstring_view(HostControlDiagnosticName(
            HostControlDiagnostic::TreeProfileMismatch)) ==
                L"tree-profile-mismatch" &&
        std::wstring_view(HostControlDiagnosticName(
            HostControlDiagnostic::MountAppendFailed)) ==
                L"mount-append-failed" &&
        std::wstring_view(HostControlDiagnosticName(
            HostControlDiagnostic::MountReady)) == L"mount-ready" &&
        std::wstring_view(HostControlDiagnosticName(
            HostControlDiagnostic::DetourTargetNotObserved)) ==
                L"detour-target-not-observed" &&
        std::wstring_view(HostControlDiagnosticName(
            HostControlDiagnostic::DetourInactive)) == L"detour-inactive" &&
        std::wstring_view(HostControlDiagnosticName(
            HostControlDiagnostic::CallbackUnavailable)) ==
                L"callback-unavailable" &&
        std::wstring_view(HostControlDiagnosticName(
            HostControlDiagnostic::CallbackReentrant)) ==
                L"callback-reentrant" &&
        std::wstring_view(HostControlDiagnosticName(
            HostControlDiagnostic::CallbackRecheckRace)) ==
                L"callback-recheck-race" &&
        std::wstring_view(HostControlDiagnosticName(
            HostControlDiagnostic::BootstrapWindowInvalid)) ==
                L"bootstrap-window-invalid" &&
        std::wstring_view(HostControlDiagnosticName(
            HostControlDiagnostic::BootstrapOwnerThreadMismatch)) ==
                L"bootstrap-owner-thread-mismatch" &&
        std::wstring_view(HostControlDiagnosticName(
            HostControlDiagnostic::BootstrapHostBoundsInvalid)) ==
                L"bootstrap-host-bounds-invalid" &&
        std::wstring_view(HostControlDiagnosticName(
            HostControlDiagnostic::BootstrapQueryFailed)) ==
                L"bootstrap-query-failed" &&
        std::wstring_view(HostControlDiagnosticName(
            HostControlDiagnostic::BootstrapEnumerationOverflow)) ==
                L"bootstrap-enumeration-overflow" &&
        std::wstring_view(HostControlDiagnosticName(
            HostControlDiagnostic::BootstrapFrameNotObserved)) ==
                L"bootstrap-frame-not-observed" &&
        std::wstring_view(HostControlDiagnosticName(
            HostControlDiagnostic::BootstrapFrameAmbiguous)) ==
                L"bootstrap-frame-ambiguous" &&
        std::wstring_view(HostControlDiagnosticName(
            HostControlDiagnostic::BootstrapTreeProfileMismatch)) ==
                L"bootstrap-tree-profile-mismatch" &&
        std::wstring_view(HostControlDiagnosticName(
            HostControlDiagnostic::BootstrapFrameValidated)) ==
                L"bootstrap-frame-validated" &&
        std::wstring_view(HostControlDiagnosticName(
            HostControlDiagnostic::BootstrapHostQueryFailed)) ==
                L"bootstrap-host-query-failed" &&
        std::wstring_view(HostControlDiagnosticName(
            HostControlDiagnostic::BootstrapEnumerationFailed)) ==
                L"bootstrap-enumeration-failed" &&
        std::wstring_view(HostControlDiagnosticName(
            HostControlDiagnostic::BootstrapClassInspectionFailed)) ==
                L"bootstrap-class-inspection-failed" &&
        std::wstring_view(HostControlDiagnosticName(
            HostControlDiagnostic::BootstrapIdentityProjectionFailed)) ==
                L"bootstrap-identity-projection-failed" &&
        std::wstring_view(HostControlDiagnosticName(
            HostControlDiagnostic::BootstrapTreeProbeUnavailable)) ==
                L"bootstrap-tree-probe-unavailable" &&
        std::wstring_view(HostControlDiagnosticName(
            static_cast<HostControlDiagnostic>(0xFFFFFFFFu))) == L"invalid",
        L"controller diagnostic names should be stable and bounded");

    if (!passed) {
        return 1;
    }
    fputws(L"Taskbar control transport policy tests passed.\n", stdout);
    return 0;
}
