#pragma once

#include "taskbar_host_contract.h"

#include <Windows.h>

#include <cstdint>

namespace taskflyout::taskbar {

enum class TaskbarDetourState : std::uint32_t {
    Dormant = 0,
    Validating = 1,
    Installing = 2,
    Active = 3,
    Stopping = 4,
    Removed = 5,
    Quarantined = 6,
};

enum class TaskbarDetourResult : std::uint32_t {
    Installed = 0,
    AlreadyActive = 1,
    Removed = 2,
    NotActive = 3,
    InvalidCallback = 4,
    WrongTaskbarThread = 5,
    CompatibilityRejected = 6,
    HostPinFailed = 7,
    TargetChanged = 8,
    InitializeFailed = 9,
    CreateFailed = 10,
    EnableFailed = 11,
    RestoreFailed = 12,
    DisableFailed = 13,
    DrainTimedOut = 14,
    RemoveFailed = 15,
    UninitializeFailed = 16,
    Busy = 17,
    Quarantined = 18,
    CallbackFailed = 19,
};

struct TaskbarDetourSnapshot {
    TaskbarDetourState state = TaskbarDetourState::Dormant;
    TaskbarDetourResult lastResult =
        TaskbarDetourResult::NotActive;
    HostCompatibility compatibility = HostCompatibility::Unknown;
    std::int32_t minHookStatus = -1;
    std::uint32_t activeCallbacks = 0;
    std::uint32_t activeCustomCallbacks = 0;
    DWORD bootstrapThreadId = 0;
    bool hostPinned = false;
};

// Implementations must validate that the TaskbarFrame dispatcher has thread
// access before touching XAML. The runtime supplies a C++ exception boundary.
using TaskbarFrameLayoutCallback = void(WINAPI*)(void* taskbarFrame);
using TaskbarRestoreCallback = bool(WINAPI*)();

TaskbarDetourResult StartTaskbarFrameDetour(
    TaskbarFrameLayoutCallback callback) noexcept;

TaskbarDetourResult StopTaskbarFrameDetour(
    TaskbarRestoreCallback restore) noexcept;

TaskbarDetourSnapshot GetTaskbarDetourSnapshot() noexcept;

}  // namespace taskflyout::taskbar
