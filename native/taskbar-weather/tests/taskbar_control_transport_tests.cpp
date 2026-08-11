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

    if (!passed) {
        return 1;
    }
    fputws(L"Taskbar control transport policy tests passed.\n", stdout);
    return 0;
}
