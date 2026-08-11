#pragma once

#include "taskbar_frame_bridge.h"
#include "taskbar_slot_probe.h"
#include "taskbar_weather_mount.h"

#include <cstdint>

namespace taskflyout::taskbar {

enum class TaskbarHostFrameAction : std::uint32_t {
    NoChange = 0,
    MountOrUpdate = 1,
    Restore = 2,
};

struct TaskbarHostFrameDecisionInput {
    TaskbarFrameBridgeStatus bridgeStatus =
        TaskbarFrameBridgeStatus::NullPrivateObject;
    TaskbarTreeProbeStatus treeStatus =
        TaskbarTreeProbeStatus::XamlTreeUnavailable;
    TaskbarSlotProbeStatus slotProbeStatus =
        TaskbarSlotProbeStatus::XamlTreeUnavailable;
    TaskbarSlotGeometryStatus geometryStatus =
        TaskbarSlotGeometryStatus::UnknownStructure;
    bool mounted = false;
};

TaskbarHostFrameAction EvaluateTaskbarHostFrameAction(
    const TaskbarHostFrameDecisionInput& input) noexcept;

constexpr bool IsTaskbarMountObservationFresh(
    const bool ready,
    const std::uint64_t observedAtTicks,
    const std::uint64_t nowTicks,
    const std::uint64_t maximumAgeTicks) noexcept {
    return ready && observedAtTicks != 0 && maximumAgeTicks != 0 &&
        nowTicks >= observedAtTicks &&
        nowTicks - observedAtTicks <= maximumAgeTicks;
}

enum class TaskbarMountReportAction : std::uint32_t {
    None = 0,
    Current = 1,
    PendingLost = 2,
};

constexpr TaskbarMountReportAction EvaluateTaskbarMountReportAction(
    const bool pendingLost,
    const bool stateChanged,
    const bool hasNewReadyProof) noexcept {
    if (pendingLost) {
        return TaskbarMountReportAction::PendingLost;
    }
    return stateChanged || hasNewReadyProof
        ? TaskbarMountReportAction::Current
        : TaskbarMountReportAction::None;
}

enum class TaskbarHostControllerResult : std::uint32_t {
    Started = 0,
    AlreadyStarted = 1,
    Stopped = 2,
    NotStarted = 3,
    StartRejected = 4,
    StopRejected = 5,
    MountReady = 6,
    MountPending = 7,
    StatusRejected = 8,
};

// These functions must be called from the primary taskbar owner thread. The
// controller is activated only by the exported WH_CALLWNDPROC entry hook's
// private control message; loading the DLL alone remains inert.
TaskbarHostControllerResult StartTaskbarWeatherController(
    std::uint32_t controllerNonce) noexcept;
TaskbarHostControllerResult StopTaskbarWeatherController() noexcept;
TaskbarHostControllerResult QueryTaskbarWeatherControllerStatus() noexcept;

const wchar_t* TaskbarHostControllerResultName(
    TaskbarHostControllerResult result) noexcept;

}  // namespace taskflyout::taskbar
