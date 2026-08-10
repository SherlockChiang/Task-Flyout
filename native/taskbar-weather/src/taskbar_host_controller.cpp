#include "taskbar_host_controller.h"

#include "taskbar_detour.h"
#include "taskbar_tree_profile.h"
#include "weather_pipe_client.h"
#include "weather_view_model.h"

#include <Windows.h>

#include <array>
#include <atomic>
#include <cstdint>
#include <new>
#include <utility>

#include <winrt/base.h>

namespace taskflyout::taskbar {
namespace {

constexpr std::size_t kMaxTaskbarFrameLeases = 4;
constexpr DWORD kWeatherPipePollIntervalMilliseconds = 15000;
constexpr DWORD kWeatherPipeStopWaitMilliseconds = 2000;

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

SRWLOCK g_weatherModelLock = SRWLOCK_INIT;
WeatherViewModel g_weatherModel{};
bool g_weatherModelAvailable = false;
HANDLE g_weatherPipeStopEvent = nullptr;
HANDLE g_weatherPipeThread = nullptr;

const WeatherViewModel& PreviewModel() {
    static const WeatherViewModel model = CreateWeatherViewModel(
        L"\u2601",
        L"--",
        L"Task Flyout");
    return model;
}

bool ClearWeatherModel() noexcept {
    AcquireSRWLockExclusive(&g_weatherModelLock);
    const bool changed = g_weatherModelAvailable;
    g_weatherModel.icon.clear();
    g_weatherModel.temperature.clear();
    g_weatherModel.condition.clear();
    g_weatherModelAvailable = false;
    ReleaseSRWLockExclusive(&g_weatherModelLock);
    return changed;
}

BOOL CALLBACK PostTaskbarRelayout(HWND window, LPARAM) noexcept {
    wchar_t className[64]{};
    if (GetClassNameW(window, className, ARRAYSIZE(className)) > 0 &&
        (lstrcmpW(className, L"Shell_TrayWnd") == 0 ||
         lstrcmpW(className, L"Shell_SecondaryTrayWnd") == 0)) {
        PostMessageW(window, WM_SETTINGCHANGE, 0, 0);
    }
    return TRUE;
}

void RequestTaskbarRelayout() noexcept {
    EnumWindows(&PostTaskbarRelayout, 0);
}

void InvalidateWeatherModel() noexcept {
    if (ClearWeatherModel()) {
        RequestTaskbarRelayout();
    }
}

void PublishWeatherModel(WeatherViewModel model) noexcept {
    bool changed = false;
    AcquireSRWLockExclusive(&g_weatherModelLock);
    try {
        changed = !g_weatherModelAvailable ||
            g_weatherModel.icon != model.icon ||
            g_weatherModel.temperature != model.temperature ||
            g_weatherModel.condition != model.condition;
        if (changed) {
            g_weatherModel = std::move(model);
            g_weatherModelAvailable = true;
        }
    } catch (...) {
        changed = false;
    }
    ReleaseSRWLockExclusive(&g_weatherModelLock);
    if (changed) {
        RequestTaskbarRelayout();
    }
}

WeatherViewModel CurrentWeatherModel() {
    WeatherViewModel model = PreviewModel();
    AcquireSRWLockShared(&g_weatherModelLock);
    try {
        if (g_weatherModelAvailable) {
            model = g_weatherModel;
        }
    } catch (...) {
    }
    ReleaseSRWLockShared(&g_weatherModelLock);
    return model;
}

struct WeatherPipeWorkerContext final {
    HMODULE selfModule = nullptr;
    HANDLE stopEvent = nullptr;
};

DWORD WINAPI WeatherPipeWorkerProc(void* parameter) {
    auto* context = static_cast<WeatherPipeWorkerContext*>(parameter);
    HMODULE selfModule = context->selfModule;
    HANDLE stopEvent = context->stopEvent;
    delete context;

    bool apartmentInitialized = false;
    while (WaitForSingleObject(stopEvent, 0) == WAIT_TIMEOUT) {
        if (!apartmentInitialized) {
            try {
                winrt::init_apartment(
                    winrt::apartment_type::multi_threaded);
                apartmentInitialized = true;
            } catch (...) {
                if (WaitForSingleObject(
                        stopEvent,
                        kWeatherPipePollIntervalMilliseconds) ==
                    WAIT_OBJECT_0) {
                    break;
                }
                continue;
            }
        }

        const WeatherPipeQueryResult query =
            QueryWeatherPipe(stopEvent);
        if (query.status == WeatherPipeQueryStatus::Cancelled ||
            WaitForSingleObject(stopEvent, 0) == WAIT_OBJECT_0) {
            break;
        }
        if (query.status == WeatherPipeQueryStatus::Updated) {
            PublishWeatherModel(query.model);
        } else {
            InvalidateWeatherModel();
        }
        if (WaitForSingleObject(
                stopEvent,
                kWeatherPipePollIntervalMilliseconds) ==
            WAIT_OBJECT_0) {
            break;
        }
    }
    if (apartmentInitialized) {
        winrt::uninit_apartment();
    }
    FreeLibraryAndExitThread(selfModule, 0);
}

bool StartWeatherPipeWorker() noexcept {
    if (g_weatherPipeThread) {
        const DWORD workerState =
            WaitForSingleObject(g_weatherPipeThread, 0);
        if (workerState == WAIT_TIMEOUT) {
            return WaitForSingleObject(g_weatherPipeStopEvent, 0) ==
                WAIT_TIMEOUT;
        }
        if (workerState != WAIT_OBJECT_0) {
            return false;
        }
        CloseHandle(g_weatherPipeThread);
        CloseHandle(g_weatherPipeStopEvent);
        g_weatherPipeThread = nullptr;
        g_weatherPipeStopEvent = nullptr;
    }

    HANDLE stopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!stopEvent) {
        return false;
    }

    HMODULE selfModule = nullptr;
    if (!GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
            reinterpret_cast<LPCWSTR>(&WeatherPipeWorkerProc),
            &selfModule)) {
        CloseHandle(stopEvent);
        return false;
    }

    auto* context = new (std::nothrow) WeatherPipeWorkerContext{
        selfModule,
        stopEvent};
    if (!context) {
        FreeLibrary(selfModule);
        CloseHandle(stopEvent);
        return false;
    }

    (void)ClearWeatherModel();
    HANDLE thread = CreateThread(
        nullptr,
        0,
        WeatherPipeWorkerProc,
        context,
        0,
        nullptr);
    if (!thread) {
        delete context;
        FreeLibrary(selfModule);
        CloseHandle(stopEvent);
        return false;
    }

    g_weatherPipeStopEvent = stopEvent;
    g_weatherPipeThread = thread;
    return true;
}

bool StopWeatherPipeWorker() noexcept {
    if (!g_weatherPipeThread) {
        (void)ClearWeatherModel();
        return true;
    }

    if (!SetEvent(g_weatherPipeStopEvent)) {
        return false;
    }
    if (WaitForSingleObject(
            g_weatherPipeThread,
            kWeatherPipeStopWaitMilliseconds) != WAIT_OBJECT_0) {
        return false;
    }

    CloseHandle(g_weatherPipeThread);
    CloseHandle(g_weatherPipeStopEvent);
    g_weatherPipeThread = nullptr;
    g_weatherPipeStopEvent = nullptr;
    (void)ClearWeatherModel();
    return true;
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
        CurrentWeatherModel(),
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
    const TaskbarDetourResult detourResult =
        StartTaskbarFrameDetour(&OnTaskbarFrameLayout);
    TaskbarHostControllerResult result = MapStartResult(detourResult);
    if ((detourResult == TaskbarDetourResult::Installed ||
         detourResult == TaskbarDetourResult::AlreadyActive) &&
        !StartWeatherPipeWorker()) {
        if (detourResult == TaskbarDetourResult::Installed) {
            StopTaskbarFrameDetour(&RestoreAllTaskbarFrames);
        }
        result = TaskbarHostControllerResult::StartRejected;
    }
    Runtime().lastResult.store(result, std::memory_order_release);
    return result;
}

TaskbarHostControllerResult StopTaskbarWeatherController() noexcept {
    if (!StopWeatherPipeWorker()) {
        Runtime().lastResult.store(
            TaskbarHostControllerResult::StopRejected,
            std::memory_order_release);
        return TaskbarHostControllerResult::StopRejected;
    }
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
