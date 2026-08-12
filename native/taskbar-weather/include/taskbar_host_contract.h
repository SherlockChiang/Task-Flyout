#pragma once

#include <Windows.h>

#include <cstdint>

namespace taskflyout::taskbar {

inline constexpr std::uint32_t kHostApiVersion = 5;
inline constexpr wchar_t kHostControlMessageName[] =
    L"TaskFlyout.TaskbarHost.Control.v2.{7C091D78-7BD5-4E99-B7BD-647E45F30CF6}";
inline constexpr wchar_t kHostControlAcknowledgementMessageName[] =
    L"TaskFlyout.TaskbarHost.Ack.v1.{8069B47C-5E79-44BB-91E8-846C02C9E4D9}";

enum class HostControlCommand : std::uintptr_t {
    Start = 1,
    Stop = 2,
    Status = 3,
};

enum class HostControlAcknowledgement : std::uint32_t {
    Unknown = 0,
    Started = 1,
    AlreadyStarted = 2,
    Stopped = 3,
    NotStarted = 4,
    StartRejected = 5,
    StopRejected = 6,
    MountReady = 7,
    MountPending = 8,
    StatusRejected = 9,
};

// Fixed, privacy-safe controller state accompanying an acknowledgement. Keep
// these values protocol-stable: they cross the Explorer/broker process
// boundary and deliberately contain no pointers or private XAML details.
enum class HostControlDiagnostic : std::uint32_t {
    None = 0,
    AwaitingLayout = 1,
    BridgeUnresolved = 2,
    TreeProfileMismatch = 3,
    LeaseUnavailable = 4,
    SlotStructureConflict = 5,
    SlotGeometryConflict = 6,
    MountViewFailed = 7,
    MountAppendFailed = 8,
    MountRestoreFailed = 9,
    MountedNotReady = 10,
    MountReady = 11,
};

struct HostControlAcknowledgementEnvelope {
    HostControlAcknowledgement acknowledgement =
        HostControlAcknowledgement::Unknown;
    HostControlDiagnostic diagnostic = HostControlDiagnostic::None;
};

struct HostControlRequestEnvelope {
    HostControlCommand command = HostControlCommand::Start;
    std::uint32_t nonce = 0;
};

static_assert(sizeof(std::uintptr_t) >= sizeof(std::uint64_t));

constexpr std::uintptr_t EncodeHostControlRequest(
    const HostControlCommand command,
    const std::uint32_t nonce) noexcept {
    return (static_cast<std::uintptr_t>(nonce) << 32u) |
        (static_cast<std::uintptr_t>(command) & 0xFFFFFFFFu);
}

constexpr HostControlRequestEnvelope DecodeHostControlRequest(
    const std::uintptr_t encoded) noexcept {
    return {
        static_cast<HostControlCommand>(encoded & 0xFFFFFFFFu),
        static_cast<std::uint32_t>(encoded >> 32u)};
}

constexpr std::uintptr_t EncodeHostControlAcknowledgement(
    const HostControlAcknowledgement acknowledgement,
    const HostControlDiagnostic diagnostic) noexcept {
    return (static_cast<std::uintptr_t>(diagnostic) << 32u) |
        (static_cast<std::uintptr_t>(acknowledgement) & 0xFFFFFFFFu);
}

constexpr HostControlAcknowledgementEnvelope
DecodeHostControlAcknowledgement(const std::uintptr_t encoded) noexcept {
    return {
        static_cast<HostControlAcknowledgement>(encoded & 0xFFFFFFFFu),
        static_cast<HostControlDiagnostic>(encoded >> 32u)};
}

constexpr bool IsHostControlAcknowledgementForCommand(
    const HostControlCommand command,
    const HostControlAcknowledgement acknowledgement) noexcept {
    if (command == HostControlCommand::Start) {
        return acknowledgement == HostControlAcknowledgement::Started ||
            acknowledgement ==
                HostControlAcknowledgement::AlreadyStarted ||
            acknowledgement ==
                HostControlAcknowledgement::StartRejected;
    }
    if (command == HostControlCommand::Stop) {
        return acknowledgement == HostControlAcknowledgement::Stopped ||
            acknowledgement == HostControlAcknowledgement::NotStarted ||
            acknowledgement == HostControlAcknowledgement::StopRejected;
    }
    if (command == HostControlCommand::Status) {
        return acknowledgement == HostControlAcknowledgement::MountReady ||
            acknowledgement == HostControlAcknowledgement::MountPending ||
            acknowledgement == HostControlAcknowledgement::NotStarted ||
            acknowledgement == HostControlAcknowledgement::StatusRejected;
    }
    return false;
}

enum class HostCompatibility : std::uint32_t {
    Unknown = 0,
    Supported = 1,
    UnsupportedProcess = 2,
    UnsupportedWindowsBuild = 3,
    TaskbarViewMissing = 4,
    TaskbarViewUnreadable = 5,
    TaskbarViewNotAllowlisted = 6,
    TaskbarHookTargetMismatch = 7,
};

struct HostCompatibilityReport {
    std::uint32_t size;
    std::uint32_t apiVersion;
    HostCompatibility status;
    std::uint32_t windowsBuild;
    std::uint32_t timeDateStamp;
    std::uint32_t sizeOfImage;
    std::uint32_t checksum;
};

}  // namespace taskflyout::taskbar

extern "C" {

#if defined(TASKFLYOUT_TASKBAR_HOST_EXPORTS)
#define TASKFLYOUT_TASKBAR_HOST_API __declspec(dllexport)
#else
#define TASKFLYOUT_TASKBAR_HOST_API __declspec(dllimport)
#endif

TASKFLYOUT_TASKBAR_HOST_API std::uint32_t WINAPI
TaskFlyoutTaskbarHost_GetApiVersion() noexcept;

TASKFLYOUT_TASKBAR_HOST_API BOOL WINAPI TaskFlyoutTaskbarHost_ProbeCurrentProcess(
    taskflyout::taskbar::HostCompatibilityReport* report) noexcept;

TASKFLYOUT_TASKBAR_HOST_API LRESULT CALLBACK TaskFlyoutTaskbarHost_EntryHook(
    int code,
    WPARAM wParam,
    LPARAM lParam) noexcept;

}
