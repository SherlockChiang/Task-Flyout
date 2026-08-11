#include "taskbar_host_controller.h"

#include "taskbar_detour.h"
#include "taskbar_mount_readiness_state.h"
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
constexpr DWORD kMountReadinessReportIntervalMilliseconds = 5000;
constexpr ULONGLONG kMountObservationMaximumAgeMilliseconds = 12000;
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
SRWLOCK g_mountReadinessLock = SRWLOCK_INIT;
TaskbarMountReadinessState g_mountReadinessState{};
HANDLE g_mountReadinessEvent = nullptr;

void SignalMountReadinessWorkerLocked() noexcept {
    if (g_mountReadinessEvent) {
        (void)SetEvent(g_mountReadinessEvent);
    }
}

void SignalMountReadinessWorker() noexcept {
    AcquireSRWLockShared(&g_mountReadinessLock);
    SignalMountReadinessWorkerLocked();
    ReleaseSRWLockShared(&g_mountReadinessLock);
}

void ResetMountReadinessSession(
    const std::uint32_t controllerNonce) noexcept {
    AcquireSRWLockExclusive(&g_mountReadinessLock);
    BeginTaskbarMountReadinessSession(
        g_mountReadinessState,
        controllerNonce,
        GetTickCount64());
    SignalMountReadinessWorkerLocked();
    ReleaseSRWLockExclusive(&g_mountReadinessLock);
}

void PublishMountReadinessObservation(const bool ready) noexcept {
    AcquireSRWLockExclusive(&g_mountReadinessLock);
    if (RecordTaskbarMountReadinessObservation(
            g_mountReadinessState,
            ready,
            GetTickCount64())) {
        SignalMountReadinessWorkerLocked();
    }
    ReleaseSRWLockExclusive(&g_mountReadinessLock);
}

TaskbarMountReadinessSnapshot CaptureMountReadinessObservation(
    const ULONGLONG nowTicks) noexcept {
    AcquireSRWLockExclusive(&g_mountReadinessLock);
    const TaskbarMountReadinessSnapshot snapshot =
        SnapshotTaskbarMountReadiness(
            g_mountReadinessState,
            nowTicks,
            kMountObservationMaximumAgeMilliseconds);
    ReleaseSRWLockExclusive(&g_mountReadinessLock);
    return snapshot;
}

void AcknowledgePendingMountLost(
    const std::uint32_t controllerNonce,
    const std::uint64_t generation) noexcept {
    AcquireSRWLockExclusive(&g_mountReadinessLock);
    if (AcknowledgeTaskbarMountLost(
            g_mountReadinessState,
            controllerNonce,
            generation) &&
        g_mountReadinessState.observedReady) {
        SignalMountReadinessWorkerLocked();
    }
    ReleaseSRWLockExclusive(&g_mountReadinessLock);
}

void EnableMountReadinessEvent(const HANDLE event) noexcept {
    AcquireSRWLockExclusive(&g_mountReadinessLock);
    g_mountReadinessEvent = event;
    SignalMountReadinessWorkerLocked();
    ReleaseSRWLockExclusive(&g_mountReadinessLock);
}

HANDLE DetachMountReadinessEvent() noexcept {
    AcquireSRWLockExclusive(&g_mountReadinessLock);
    HANDLE event = g_mountReadinessEvent;
    g_mountReadinessEvent = nullptr;
    ReleaseSRWLockExclusive(&g_mountReadinessLock);
    return event;
}

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
    HANDLE mountReadinessEvent = nullptr;
};

DWORD RemainingWorkerWait(
    const ULONGLONG nowTicks,
    const ULONGLONG deadlineTicks) noexcept {
    if (deadlineTicks <= nowTicks) {
        return 0;
    }
    const ULONGLONG remaining = deadlineTicks - nowTicks;
    return remaining > MAXDWORD
        ? MAXDWORD
        : static_cast<DWORD>(remaining);
}

DWORD WINAPI WeatherPipeWorkerProc(void* parameter) {
    auto* context = static_cast<WeatherPipeWorkerContext*>(parameter);
    HMODULE selfModule = context->selfModule;
    HANDLE stopEvent = context->stopEvent;
    HANDLE openEvent = context->openEvent;
    HANDLE mountReadinessEvent = context->mountReadinessEvent;
    delete context;

    bool apartmentInitialized = false;
    ULONGLONG nextWeatherPollTicks = 0;
    ULONGLONG nextMountReportTicks = 0;
    std::uint32_t lastControllerNonce = 0;
    std::uint64_t lastAcceptedMountGeneration = 0;
    std::uint64_t lastAcceptedReadyProofSequence = 0;
    bool lastAcceptedMountReady = false;
    bool mountReportAccepted = false;
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
                const std::array<HANDLE, 3> waitHandles{
                    stopEvent,
                    openEvent,
                    mountReadinessEvent};
                const DWORD wait = WaitForMultipleObjects(
                    static_cast<DWORD>(waitHandles.size()),
                    waitHandles.data(),
                    FALSE,
                    kMountReadinessReportIntervalMilliseconds);
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

        ULONGLONG nowTicks = GetTickCount64();
        if (nextWeatherPollTicks == 0 ||
            nowTicks >= nextWeatherPollTicks) {
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
            nextWeatherPollTicks =
                GetTickCount64() + kWeatherPipePollIntervalMilliseconds;
        }

        nowTicks = GetTickCount64();
        if (nextMountReportTicks == 0 ||
            nowTicks >= nextMountReportTicks) {
            const TaskbarMountReadinessSnapshot snapshot =
                CaptureMountReadinessObservation(nowTicks);
            const std::uint32_t controllerNonce =
                snapshot.controllerNonce;
            if (controllerNonce != 0) {
                if (controllerNonce != lastControllerNonce) {
                    lastControllerNonce = controllerNonce;
                    lastAcceptedMountGeneration = 0;
                    lastAcceptedReadyProofSequence = 0;
                    lastAcceptedMountReady = false;
                    mountReportAccepted = false;
                }
                const bool stateChanged = !mountReportAccepted ||
                    snapshot.generation != lastAcceptedMountGeneration ||
                    snapshot.ready != lastAcceptedMountReady;
                const bool hasNewReadyProof = snapshot.ready &&
                    snapshot.proofSequence !=
                        lastAcceptedReadyProofSequence;
                const TaskbarMountReportAction action =
                    EvaluateTaskbarMountReportAction(
                        snapshot.pendingLost,
                        stateChanged,
                        hasNewReadyProof);
                if (action != TaskbarMountReportAction::None) {
                    const WeatherPipeMountReadinessStatus report =
                        ReportMountReadiness(
                            controllerNonce,
                            snapshot.generation,
                            snapshot.ready,
                            stopEvent);
                    if (report ==
                            WeatherPipeMountReadinessStatus::Cancelled ||
                        WaitForSingleObject(stopEvent, 0) == WAIT_OBJECT_0) {
                        break;
                    }
                    if (report ==
                        WeatherPipeMountReadinessStatus::Accepted) {
                        mountReportAccepted = true;
                        lastAcceptedMountGeneration = snapshot.generation;
                        lastAcceptedMountReady = snapshot.ready;
                        if (snapshot.ready) {
                            lastAcceptedReadyProofSequence =
                                snapshot.proofSequence;
                        }
                        if (snapshot.pendingLost) {
                            AcknowledgePendingMountLost(
                                controllerNonce,
                                snapshot.generation);
                        }
                    }
                }
                // Drive the next owner-thread proof. This notification is only
                // a request; a later ready heartbeat is sent only after the
                // proof sequence actually advances.
                RequestTaskbarRelayout();
            } else {
                lastControllerNonce = 0;
                mountReportAccepted = false;
            }
            nextMountReportTicks = GetTickCount64() +
                kMountReadinessReportIntervalMilliseconds;
        }

        nowTicks = GetTickCount64();
        const DWORD weatherWait = RemainingWorkerWait(
            nowTicks,
            nextWeatherPollTicks);
        const DWORD mountWait = RemainingWorkerWait(
            nowTicks,
            nextMountReportTicks);
        const DWORD waitTimeout = weatherWait < mountWait
            ? weatherWait
            : mountWait;
        if (waitTimeout == 0) {
            continue;
        }

        const std::array<HANDLE, 3> waitHandles{
            stopEvent,
            openEvent,
            mountReadinessEvent};
        const DWORD wait = WaitForMultipleObjects(
            static_cast<DWORD>(waitHandles.size()),
            waitHandles.data(),
            FALSE,
            waitTimeout);
        if (wait == WAIT_OBJECT_0 || wait == WAIT_FAILED) {
            break;
        }
        if (wait == WAIT_OBJECT_0 + 1u) {
            // The auto-reset event has been consumed. Keep the pending bit set
            // until the request finishes so clicks during one exchange merge.
            continue;
        }
        if (wait == WAIT_OBJECT_0 + 2u) {
            nextMountReportTicks = 0;
            continue;
        }
        if (wait != WAIT_TIMEOUT) {
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
        HANDLE oldMountReadinessEvent = DetachMountReadinessEvent();
        CloseHandle(g_weatherPipeThread);
        CloseHandle(g_weatherPipeStopEvent);
        if (oldOpenEvent) {
            CloseHandle(oldOpenEvent);
        }
        if (oldMountReadinessEvent) {
            CloseHandle(oldMountReadinessEvent);
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
    HANDLE mountReadinessEvent =
        CreateEventW(nullptr, FALSE, FALSE, nullptr);
    if (!mountReadinessEvent) {
        CloseHandle(openEvent);
        CloseHandle(stopEvent);
        return false;
    }

    HMODULE selfModule = nullptr;
    if (!GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
             reinterpret_cast<LPCWSTR>(&WeatherPipeWorkerProc),
             &selfModule)) {
        CloseHandle(mountReadinessEvent);
        CloseHandle(openEvent);
        CloseHandle(stopEvent);
        return false;
    }

    auto* context = new (std::nothrow) WeatherPipeWorkerContext{
        selfModule,
        stopEvent,
        openEvent,
        mountReadinessEvent};
    if (!context) {
        FreeLibrary(selfModule);
        CloseHandle(mountReadinessEvent);
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
        CloseHandle(mountReadinessEvent);
        CloseHandle(openEvent);
        CloseHandle(stopEvent);
        return false;
    }

    g_weatherPipeStopEvent = stopEvent;
    g_weatherPipeThread = thread;
    EnableWeatherActivationRequests(openEvent);
    EnableMountReadinessEvent(mountReadinessEvent);
    return true;
}

bool StopWeatherPipeWorker() noexcept {
    if (!g_weatherPipeThread) {
        HANDLE openEvent = DetachWeatherActivationEvent();
        HANDLE mountReadinessEvent = DetachMountReadinessEvent();
        if (openEvent) {
            CloseHandle(openEvent);
        }
        if (mountReadinessEvent) {
            CloseHandle(mountReadinessEvent);
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
    HANDLE mountReadinessEvent = DetachMountReadinessEvent();
    CloseHandle(g_weatherPipeThread);
    CloseHandle(g_weatherPipeStopEvent);
    if (openEvent) {
        CloseHandle(openEvent);
    }
    if (mountReadinessEvent) {
        CloseHandle(mountReadinessEvent);
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

bool RefreshCurrentMountReadiness() noexcept {
    bool ready = false;
    auto& runtime = Runtime();
    for (auto& lease : runtime.leases) {
        ResetExpiredLease(lease);
        if (!ready && lease.occupied) {
            try {
                ready = IsWeatherButtonMountReady(lease.mount);
            } catch (...) {
                ready = false;
            }
        }
    }
    PublishMountReadinessObservation(ready);
    return ready;
}

struct MountReadinessRefreshScope final {
    ~MountReadinessRefreshScope() {
        (void)RefreshCurrentMountReadiness();
    }
};

void ThrowCallbackFailure() {
    struct CallbackFailure final {};
    throw CallbackFailure{};
}

void WINAPI OnTaskbarFrameLayout(void* privateTaskbarFrame) {
    const TaskbarFrameBridgeResult bridge =
        ResolveTaskbarFrameFromPrivateAbi(privateTaskbarFrame);
    if (bridge.status != TaskbarFrameBridgeStatus::Resolved) {
        PublishMountReadinessObservation(false);
        return;
    }
    MountReadinessRefreshScope readinessRefresh;

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

TaskbarHostControllerResult StartTaskbarWeatherController(
    const std::uint32_t controllerNonce) noexcept {
    if (controllerNonce == 0) {
        Runtime().lastResult.store(
            TaskbarHostControllerResult::StartRejected,
            std::memory_order_release);
        return TaskbarHostControllerResult::StartRejected;
    }

    // Revoke the previous reporting identity before changing detour/worker
    // state. Publish the new nonce only after both are known to be active, so
    // an in-flight worker can never pair it with the previous mount snapshot.
    ResetMountReadinessSession(0);
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
    if (result == TaskbarHostControllerResult::Started ||
        result == TaskbarHostControllerResult::AlreadyStarted) {
        ResetMountReadinessSession(controllerNonce);
        SignalMountReadinessWorker();
        RequestTaskbarRelayout();
    }
    Runtime().lastResult.store(result, std::memory_order_release);
    return result;
}

TaskbarHostControllerResult StopTaskbarWeatherController() noexcept {
    ResetMountReadinessSession(0);
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

TaskbarHostControllerResult QueryTaskbarWeatherControllerStatus() noexcept {
    const TaskbarDetourSnapshot detour = GetTaskbarDetourSnapshot();
    if (detour.state == TaskbarDetourState::Dormant ||
        detour.state == TaskbarDetourState::Removed) {
        return TaskbarHostControllerResult::NotStarted;
    }
    if (detour.state != TaskbarDetourState::Active ||
        detour.bootstrapThreadId == 0 ||
        detour.bootstrapThreadId != GetCurrentThreadId()) {
        return TaskbarHostControllerResult::StatusRejected;
    }

    auto& runtime = Runtime();
    if (RefreshCurrentMountReadiness()) {
        SignalMountReadinessWorker();
        runtime.lastResult.store(
            TaskbarHostControllerResult::MountReady,
            std::memory_order_release);
        return TaskbarHostControllerResult::MountReady;
    }

    // Starting the controller and publishing a weather snapshot both request
    // layout, but a coalesced shell notification may not yield a frame callback.
    // A bounded status poll can safely request another asynchronous pass.
    RequestTaskbarRelayout();
    SignalMountReadinessWorker();
    runtime.lastResult.store(
        TaskbarHostControllerResult::MountPending,
        std::memory_order_release);
    return TaskbarHostControllerResult::MountPending;
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
        case TaskbarHostControllerResult::MountReady:
            return L"mount-ready";
        case TaskbarHostControllerResult::MountPending:
            return L"mount-pending";
        case TaskbarHostControllerResult::StatusRejected:
            return L"status-rejected";
    }
    return L"unknown";
}

}  // namespace taskflyout::taskbar
