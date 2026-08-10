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
HANDLE g_weatherOpenEvent = nullptr;
SRWLOCK g_weatherActivationLock = SRWLOCK_INIT;
std::atomic_bool g_weatherActivationPending{false};
bool g_weatherActivationEnabled = false;

void DisableWeatherActivationRequests() noexcept {
    AcquireSRWLockExclusive(&g_weatherActivationLock);
    g_weatherActivationEnabled = false;
    g_weatherActivationPending.store(false, std::memory_order_release);
    ReleaseSRWLockExclusive(&g_weatherActivationLock);
}

HANDLE DetachWeatherActivationEvent() noexcept {
    AcquireSRWLockExclusive(&g_weatherActivationLock);
    g_weatherActivationEnabled = false;
    g_weatherActivationPending.store(false, std::memory_order_release);
    HANDLE event = g_weatherOpenEvent;
    g_weatherOpenEvent = nullptr;
    ReleaseSRWLockExclusive(&g_weatherActivationLock);
    return event;
}

void EnableWeatherActivationRequests(const HANDLE event) noexcept {
    AcquireSRWLockExclusive(&g_weatherActivationLock);
    g_weatherActivationPending.store(false, std::memory_order_release);
    g_weatherOpenEvent = event;
    g_weatherActivationEnabled = event != nullptr;
    ReleaseSRWLockExclusive(&g_weatherActivationLock);
}

void WINAPI QueueWeatherOpenRequest() noexcept {
    AcquireSRWLockShared(&g_weatherActivationLock);
    if (g_weatherActivationEnabled && g_weatherOpenEvent) {
        bool expected = false;
        if (g_weatherActivationPending.compare_exchange_strong(
                expected,
                true,
                std::memory_order_acq_rel,
                std::memory_order_acquire) &&
            !SetEvent(g_weatherOpenEvent)) {
            g_weatherActivationPending.store(
                false,
                std::memory_order_release);
        }
    }
    ReleaseSRWLockShared(&g_weatherActivationLock);
}

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
    HANDLE openEvent = nullptr;
};

DWORD WINAPI WeatherPipeWorkerProc(void* parameter) {
    auto* context = static_cast<WeatherPipeWorkerContext*>(parameter);
    HMODULE selfModule = context->selfModule;
    HANDLE stopEvent = context->stopEvent;
    HANDLE openEvent = context->openEvent;
    delete context;

    bool apartmentInitialized = false;
    bool pollSnapshot = true;
    for (;;) {
        if (WaitForSingleObject(stopEvent, 0) == WAIT_OBJECT_0) {
            break;
        }
        if (!apartmentInitialized) {
            try {
                winrt::init_apartment(
                    winrt::apartment_type::multi_threaded);
                apartmentInitialized = true;
            } catch (...) {
                const std::array<HANDLE, 2> waitHandles{
                    stopEvent,
                    openEvent};
                const DWORD wait = WaitForMultipleObjects(
                    static_cast<DWORD>(waitHandles.size()),
                    waitHandles.data(),
                    FALSE,
                    kWeatherPipePollIntervalMilliseconds);
                if (wait == WAIT_OBJECT_0 || wait == WAIT_FAILED) {
                    break;
                }
                continue;
            }
        }

        if (g_weatherActivationPending.load(std::memory_order_acquire)) {
            const WeatherPipeActivationStatus activation =
                RequestWeatherOpen(stopEvent);
            g_weatherActivationPending.store(
                false,
                std::memory_order_release);
            if (activation == WeatherPipeActivationStatus::Cancelled ||
                WaitForSingleObject(stopEvent, 0) == WAIT_OBJECT_0) {
                break;
            }
            continue;
        }

        if (pollSnapshot) {
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
            pollSnapshot = false;
        }

        const std::array<HANDLE, 2> waitHandles{stopEvent, openEvent};
        const DWORD wait = WaitForMultipleObjects(
            static_cast<DWORD>(waitHandles.size()),
            waitHandles.data(),
            FALSE,
            kWeatherPipePollIntervalMilliseconds);
        if (wait == WAIT_OBJECT_0 || wait == WAIT_FAILED) {
            break;
        }
        if (wait == WAIT_OBJECT_0 + 1u) {
            // The auto-reset event has been consumed. Keep the pending bit set
            // until the request finishes so clicks during one exchange merge.
            continue;
        }
        if (wait == WAIT_TIMEOUT) {
            pollSnapshot = true;
        } else if (wait != WAIT_OBJECT_0 + 1u) {
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
        HANDLE oldOpenEvent = DetachWeatherActivationEvent();
        CloseHandle(g_weatherPipeThread);
        CloseHandle(g_weatherPipeStopEvent);
        if (oldOpenEvent) {
            CloseHandle(oldOpenEvent);
        }
        g_weatherPipeThread = nullptr;
        g_weatherPipeStopEvent = nullptr;
    }

    HANDLE stopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!stopEvent) {
        return false;
    }
    HANDLE openEvent = CreateEventW(nullptr, FALSE, FALSE, nullptr);
    if (!openEvent) {
        CloseHandle(stopEvent);
        return false;
    }

    HMODULE selfModule = nullptr;
    if (!GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
             reinterpret_cast<LPCWSTR>(&WeatherPipeWorkerProc),
             &selfModule)) {
        CloseHandle(openEvent);
        CloseHandle(stopEvent);
        return false;
    }

    auto* context = new (std::nothrow) WeatherPipeWorkerContext{
        selfModule,
        stopEvent,
        openEvent};
    if (!context) {
        FreeLibrary(selfModule);
        CloseHandle(openEvent);
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
        CloseHandle(openEvent);
        CloseHandle(stopEvent);
        return false;
    }

    g_weatherPipeStopEvent = stopEvent;
    g_weatherPipeThread = thread;
    EnableWeatherActivationRequests(openEvent);
    return true;
}

bool StopWeatherPipeWorker() noexcept {
    if (!g_weatherPipeThread) {
        HANDLE openEvent = DetachWeatherActivationEvent();
        if (openEvent) {
            CloseHandle(openEvent);
        }
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

    HANDLE openEvent = DetachWeatherActivationEvent();
    CloseHandle(g_weatherPipeThread);
    CloseHandle(g_weatherPipeStopEvent);
    if (openEvent) {
        CloseHandle(openEvent);
    }
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
            if (!lease.mount.mounted) {
                lease = {};
                return;
            }
            // A frame weak reference can expire before the Button weak
            // reference. Give the owner thread one last chance to revoke the
            // event token; never discard a live token with a blind reset.
            if (!lease.mount.button.get()) {
                lease = {};
                return;
            }
            if (RestoreWeatherButton(lease.mount) ==
                TaskbarMountStatus::Restored) {
                lease = {};
            }
        }
    } catch (...) {
        // Keep a mounted lease after an exception so stop/quarantine can
        // retry token revocation instead of losing ownership metadata.
        if (!lease.mount.mounted) {
            lease = {};
        }
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
            if (!lease.mount.mounted) {
                lease = {};
                if (!freeLease) {
                    freeLease = &lease;
                }
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
        &QueueWeatherOpenRequest,
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
    // Disable producer-side signalling before joining the worker. The
    // exclusive lock drains any click callback currently inside SetEvent.
    DisableWeatherActivationRequests();
    const bool workerStopped = StopWeatherPipeWorker();

    // Always attempt XAML restoration, even if the worker missed its bounded
    // stop deadline. A retained Click token is a separate cleanup obligation.
    const TaskbarDetourResult detourResult =
        StopTaskbarFrameDetour(&RestoreAllTaskbarFrames);
    TaskbarHostControllerResult result = MapStopResult(detourResult);
    if (!workerStopped ||
        (detourResult != TaskbarDetourResult::Removed &&
         detourResult != TaskbarDetourResult::NotActive)) {
        result = TaskbarHostControllerResult::StopRejected;
    }
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
