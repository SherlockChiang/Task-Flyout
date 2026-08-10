#include "taskbar_host_controller.h"

#include "taskbar_detour.h"
#include "taskbar_tree_profile.h"
#include "weather_view_model.h"

#include <Windows.h>

#include <array>
#include <atomic>
#include <cstdint>

namespace taskflyout::taskbar {
namespace {

constexpr std::size_t kMaxTaskbarFrameLeases = 4;

struct TaskbarFrameLease {
    winrt::weak_ref<winrt::Windows::UI::Xaml::FrameworkElement> frame;
    TaskbarWeatherMountState mount;
    bool occupied = false;
};

struct TaskbarHostRuntime {
    std::array<TaskbarFrameLease, kMaxTaskbarFrameLeases> leases{};
    std::atomic<TaskbarHostControllerResult> lastResult{
        TaskbarHostControllerResult::NotStarted};
};

TaskbarHostRuntime& Runtime() {
    static TaskbarHostRuntime runtime;
    return runtime;
}

const WeatherViewModel& PreviewModel() {
    static const WeatherViewModel model = CreateWeatherViewModel(
        L"\u2601",
        L"--",
        L"Task Flyout");
    return model;
}

void ResetExpiredLease(TaskbarFrameLease& lease) noexcept {
    if (!lease.occupied) {
        return;
    }
    try {
        if (!lease.frame.get()) {
            lease = {};
        }
    } catch (...) {
        lease = {};
    }
}

TaskbarFrameLease* FindOrCreateLease(
    const winrt::Windows::UI::Xaml::FrameworkElement& frame) noexcept {
    auto& runtime = Runtime();
    TaskbarFrameLease* freeLease = nullptr;
    for (auto& lease : runtime.leases) {
        ResetExpiredLease(lease);
        if (!lease.occupied) {
            if (!freeLease) {
                freeLease = &lease;
            }
            continue;
        }
        try {
            if (lease.frame.get() == frame) {
                return &lease;
            }
        } catch (...) {
            lease = {};
            if (!freeLease) {
                freeLease = &lease;
            }
        }
    }

    if (!freeLease) {
        return nullptr;
    }
    try {
        freeLease->frame = frame;
        freeLease->occupied = true;
        return freeLease;
    } catch (...) {
        *freeLease = {};
        return nullptr;
    }
}

bool RestoreLease(TaskbarFrameLease& lease) noexcept {
    if (!lease.occupied || !lease.mount.mounted) {
        return true;
    }
    return RestoreWeatherButton(lease.mount) ==
        TaskbarMountStatus::Restored;
}

void ThrowCallbackFailure() {
    struct CallbackFailure final {};
    throw CallbackFailure{};
}

void WINAPI OnTaskbarFrameLayout(void* privateTaskbarFrame) {
    const TaskbarFrameBridgeResult bridge =
        ResolveTaskbarFrameFromPrivateAbi(privateTaskbarFrame);
    if (bridge.status != TaskbarFrameBridgeStatus::Resolved) {
        return;
    }

    const TaskbarTreeProfile profile =
        ProbeTaskbarFrameTree(bridge.frame);
    TaskbarFrameLease* lease = FindOrCreateLease(bridge.frame);
    if (!lease) {
        return;
    }

    winrt::Windows::UI::Xaml::DependencyObject ownedChild{nullptr};
    try {
        ownedChild = lease->mount.button.get();
    } catch (...) {
        ownedChild = nullptr;
    }
    const TaskbarSlotProbeResult slot = ProbeTaskbarSlot(
        profile,
        {},
        ownedChild);
    const TaskbarHostFrameAction action =
        EvaluateTaskbarHostFrameAction({
            bridge.status,
            profile.status,
            slot.status,
            slot.geometry.status,
            lease->mount.mounted});

    if (action == TaskbarHostFrameAction::NoChange) {
        return;
    }
    if (action == TaskbarHostFrameAction::Restore) {
        if (!RestoreLease(*lease)) {
            ThrowCallbackFailure();
        }
        return;
    }

    const TaskbarMountStatus mountStatus = MountWeatherButton(
        profile,
        PreviewModel(),
        slot,
        lease->mount);
    switch (mountStatus) {
        case TaskbarMountStatus::Mounted:
        case TaskbarMountStatus::AlreadyMounted:
        case TaskbarMountStatus::Updated:
            return;
        case TaskbarMountStatus::ViewCreationFailed:
        case TaskbarMountStatus::AppendFailed:
        case TaskbarMountStatus::TreeNotReady:
        case TaskbarMountStatus::StructureNotAllowlisted:
        case TaskbarMountStatus::LeftSlotUnavailable:
        case TaskbarMountStatus::SlotGeometryInvalid:
            return;
        case TaskbarMountStatus::Restored:
        case TaskbarMountStatus::WrongOwnerThread:
        case TaskbarMountStatus::InvalidState:
        case TaskbarMountStatus::RestoreFailed:
        case TaskbarMountStatus::RollbackFailed:
            ThrowCallbackFailure();
            return;
    }
}

bool WINAPI RestoreAllTaskbarFrames() {
    auto& runtime = Runtime();
    bool restored = true;
    for (auto& lease : runtime.leases) {
        ResetExpiredLease(lease);
        if (!RestoreLease(lease)) {
            restored = false;
        }
    }
    if (restored) {
        for (auto& lease : runtime.leases) {
            lease = {};
        }
    }
    return restored;
}

TaskbarHostControllerResult MapStartResult(
    const TaskbarDetourResult result) noexcept {
    switch (result) {
        case TaskbarDetourResult::Installed:
            return TaskbarHostControllerResult::Started;
        case TaskbarDetourResult::AlreadyActive:
            return TaskbarHostControllerResult::AlreadyStarted;
        default:
            return TaskbarHostControllerResult::StartRejected;
    }
}

TaskbarHostControllerResult MapStopResult(
    const TaskbarDetourResult result) noexcept {
    switch (result) {
        case TaskbarDetourResult::Removed:
            return TaskbarHostControllerResult::Stopped;
        case TaskbarDetourResult::NotActive:
            return TaskbarHostControllerResult::NotStarted;
        default:
            return TaskbarHostControllerResult::StopRejected;
    }
}

}  // namespace

TaskbarHostFrameAction EvaluateTaskbarHostFrameAction(
    const TaskbarHostFrameDecisionInput& input) noexcept {
    if (input.bridgeStatus != TaskbarFrameBridgeStatus::Resolved) {
        return TaskbarHostFrameAction::NoChange;
    }
    if (input.treeStatus != TaskbarTreeProbeStatus::LandmarksMatched ||
        input.slotProbeStatus != TaskbarSlotProbeStatus::SnapshotReady) {
        return input.mounted
            ? TaskbarHostFrameAction::Restore
            : TaskbarHostFrameAction::NoChange;
    }
    if (input.geometryStatus ==
        TaskbarSlotGeometryStatus::CandidateAvailable) {
        return TaskbarHostFrameAction::MountOrUpdate;
    }
    return input.mounted
        ? TaskbarHostFrameAction::Restore
        : TaskbarHostFrameAction::NoChange;
}

TaskbarHostControllerResult StartTaskbarWeatherController() noexcept {
    const TaskbarHostControllerResult result = MapStartResult(
        StartTaskbarFrameDetour(&OnTaskbarFrameLayout));
    Runtime().lastResult.store(result, std::memory_order_release);
    return result;
}

TaskbarHostControllerResult StopTaskbarWeatherController() noexcept {
    const TaskbarHostControllerResult result = MapStopResult(
        StopTaskbarFrameDetour(&RestoreAllTaskbarFrames));
    Runtime().lastResult.store(result, std::memory_order_release);
    return result;
}

const wchar_t* TaskbarHostControllerResultName(
    const TaskbarHostControllerResult result) noexcept {
    switch (result) {
        case TaskbarHostControllerResult::Started:
            return L"started";
        case TaskbarHostControllerResult::AlreadyStarted:
            return L"already-started";
        case TaskbarHostControllerResult::Stopped:
            return L"stopped";
        case TaskbarHostControllerResult::NotStarted:
            return L"not-started";
        case TaskbarHostControllerResult::StartRejected:
            return L"start-rejected";
        case TaskbarHostControllerResult::StopRejected:
            return L"stop-rejected";
    }
    return L"unknown";
}

}  // namespace taskflyout::taskbar
