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

enum class TaskbarHostControllerResult : std::uint32_t {
    Started = 0,
    AlreadyStarted = 1,
    Stopped = 2,
    NotStarted = 3,
    StartRejected = 4,
    StopRejected = 5,
};

// These functions must be called from the primary taskbar owner thread. The
// controller is activated only by the exported WH_CALLWNDPROC entry hook's
// private control message; loading the DLL alone remains inert.
TaskbarHostControllerResult StartTaskbarWeatherController() noexcept;
TaskbarHostControllerResult StopTaskbarWeatherController() noexcept;

const wchar_t* TaskbarHostControllerResultName(
    TaskbarHostControllerResult result) noexcept;

}  // namespace taskflyout::taskbar
