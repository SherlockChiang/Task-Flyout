#pragma once

#include "taskbar_frame_bridge.h"
#include "taskbar_host_contract.h"
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

HostControlDiagnostic EvaluateTaskbarHostFrameDiagnostic(
    const TaskbarHostFrameDecisionInput& input) noexcept;

HostControlDiagnostic TaskbarMountStatusDiagnostic(
    TaskbarMountStatus status) noexcept;

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

struct TaskbarHostControllerStatusSnapshot {
    TaskbarHostControllerResult result =
        TaskbarHostControllerResult::NotStarted;
    HostControlDiagnostic diagnostic = HostControlDiagnostic::None;
};

// These functions must be called from the primary taskbar owner thread. The
// controller is activated only by the exported WH_CALLWNDPROC entry hook's
// private control message; loading the DLL alone remains inert.
TaskbarHostControllerResult StartTaskbarWeatherController(
    std::uint32_t controllerNonce) noexcept;
TaskbarHostControllerResult StopTaskbarWeatherController() noexcept;
TaskbarHostControllerResult QueryTaskbarWeatherControllerStatus() noexcept;
TaskbarHostControllerStatusSnapshot
QueryTaskbarWeatherControllerStatusSnapshot() noexcept;

HostControlDiagnostic CurrentTaskbarWeatherControllerDiagnostic() noexcept;

const wchar_t* TaskbarHostControllerResultName(
    TaskbarHostControllerResult result) noexcept;

}  // namespace taskflyout::taskbar
