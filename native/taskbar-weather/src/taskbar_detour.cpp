#include "taskbar_detour.h"

#include "loaded_taskbar_profile.h"

#include <MinHook.h>

#include <atomic>
#include <cstdint>

namespace taskflyout::taskbar {
namespace {

using TaskbarFrameLayoutFunction = void(WINAPI*)(void* taskbarFrame);

constexpr DWORD kDrainTimeoutMilliseconds = 500;
constexpr DWORD kStableZeroMilliseconds = 25;

SRWLOCK g_runtimeLock = SRWLOCK_INIT;
ValidatedTaskbarModule g_taskbarModule;
std::atomic<TaskbarDetourState> g_state{TaskbarDetourState::Dormant};
std::atomic<TaskbarFrameLayoutFunction> g_original{nullptr};
std::atomic<TaskbarFrameLayoutCallback> g_callback{nullptr};
volatile LONG g_activeCallbacks = 0;
TaskbarDetourResult g_lastResult = TaskbarDetourResult::NotActive;
HostCompatibility g_compatibility = HostCompatibility::Unknown;
MH_STATUS g_lastMinHookStatus = MH_UNKNOWN;
DWORD g_bootstrapThreadId = 0;
bool g_hostPinned = false;
bool g_minHookInitialized = false;
bool g_hookCreated = false;
bool g_hookEnabled = false;
int g_hostModuleAnchor = 0;
thread_local std::uint32_t g_detourDepth = 0;
thread_local bool g_insideCustomUpdate = false;

class ExclusiveRuntimeLock final {
public:
    ExclusiveRuntimeLock() noexcept {
        AcquireSRWLockExclusive(&g_runtimeLock);
    }

    ExclusiveRuntimeLock(const ExclusiveRuntimeLock&) = delete;
    ExclusiveRuntimeLock& operator=(const ExclusiveRuntimeLock&) = delete;

    ~ExclusiveRuntimeLock() {
        ReleaseSRWLockExclusive(&g_runtimeLock);
    }
};

class SharedRuntimeLock final {
public:
    SharedRuntimeLock() noexcept {
        AcquireSRWLockShared(&g_runtimeLock);
    }

    SharedRuntimeLock(const SharedRuntimeLock&) = delete;
    SharedRuntimeLock& operator=(const SharedRuntimeLock&) = delete;

    ~SharedRuntimeLock() {
        ReleaseSRWLockShared(&g_runtimeLock);
    }
};

class ActiveCallbackLease final {
public:
    ActiveCallbackLease() noexcept {
        InterlockedIncrement(&g_activeCallbacks);
    }

    ActiveCallbackLease(const ActiveCallbackLease&) = delete;
    ActiveCallbackLease& operator=(const ActiveCallbackLease&) = delete;

    ~ActiveCallbackLease() {
        InterlockedDecrement(&g_activeCallbacks);
        WakeByAddressAll(const_cast<LONG*>(&g_activeCallbacks));
    }
};

class DetourDepthGuard final {
public:
    DetourDepthGuard() noexcept {
        ++g_detourDepth;
    }

    DetourDepthGuard(const DetourDepthGuard&) = delete;
    DetourDepthGuard& operator=(const DetourDepthGuard&) = delete;

    ~DetourDepthGuard() {
        if (g_detourDepth != 0) {
            --g_detourDepth;
        }
    }
};

class CustomUpdateGuard final {
public:
    CustomUpdateGuard() noexcept : acquired_(!g_insideCustomUpdate) {
        if (acquired_) {
            g_insideCustomUpdate = true;
        }
    }

    CustomUpdateGuard(const CustomUpdateGuard&) = delete;
    CustomUpdateGuard& operator=(const CustomUpdateGuard&) = delete;

    ~CustomUpdateGuard() {
        if (acquired_) {
            g_insideCustomUpdate = false;
        }
    }

    explicit operator bool() const noexcept {
        return acquired_;
    }

private:
    bool acquired_ = false;
};

LONG ReadActiveCallbackCount() noexcept {
    return InterlockedCompareExchange(&g_activeCallbacks, 0, 0);
}

void RecordResult(TaskbarDetourResult result) noexcept {
    g_lastResult = result;
}

TaskbarDetourResult Quarantine(
    TaskbarDetourResult result,
    MH_STATUS status = MH_UNKNOWN) noexcept {
    if (status != MH_UNKNOWN) {
        g_lastMinHookStatus = status;
    }
    g_callback.store(nullptr, std::memory_order_release);
    g_state.store(TaskbarDetourState::Quarantined,
                  std::memory_order_release);
    RecordResult(result);
    return result;
}

bool PinHostModule() noexcept {
    if (g_hostPinned) {
        return true;
    }

    HMODULE self = nullptr;
    if (!GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                GET_MODULE_HANDLE_EX_FLAG_PIN,
            reinterpret_cast<LPCWSTR>(&g_hostModuleAnchor),
            &self)) {
        return false;
    }

    g_hostPinned = true;
    return true;
}

void WINAPI TaskbarFrameLayoutDetour(void* taskbarFrame) {
    ActiveCallbackLease callbackLease;
    DetourDepthGuard depthGuard;

    const TaskbarFrameLayoutFunction original =
        g_original.load(std::memory_order_acquire);
    if (original) {
        original(taskbarFrame);
    }

    if (g_state.load(std::memory_order_acquire) ==
        TaskbarDetourState::Active) {
        const TaskbarFrameLayoutCallback callback =
            g_callback.load(std::memory_order_acquire);
        CustomUpdateGuard updateGuard;
        if (callback && updateGuard) {
            callback(taskbarFrame);
        }
    }

}

bool DrainActiveCallbacks() noexcept {
    const ULONGLONG deadline =
        GetTickCount64() + kDrainTimeoutMilliseconds;

    for (;;) {
        LONG active = ReadActiveCallbackCount();
        if (active != 0) {
            const ULONGLONG now = GetTickCount64();
            if (now >= deadline) {
                return false;
            }
            const DWORD remaining = static_cast<DWORD>(deadline - now);
            WaitOnAddress(
                const_cast<LONG*>(&g_activeCallbacks),
                &active,
                sizeof(active),
                remaining);
            continue;
        }

        LONG zero = 0;
        const ULONGLONG now = GetTickCount64();
        if (now >= deadline) {
            return false;
        }
        const DWORD stableWait = static_cast<DWORD>(
            (deadline - now) < kStableZeroMilliseconds
                ? deadline - now
                : kStableZeroMilliseconds);
        const BOOL changed = WaitOnAddress(
            const_cast<LONG*>(&g_activeCallbacks),
            &zero,
            sizeof(zero),
            stableWait);
        if (!changed && GetLastError() == ERROR_TIMEOUT &&
            ReadActiveCallbackCount() == 0) {
            return true;
        }
    }
}

bool CleanInitializedBackend() noexcept {
    if (!g_minHookInitialized) {
        return true;
    }

    const MH_STATUS status = MH_Uninitialize();
    g_lastMinHookStatus = status;
    if (status != MH_OK) {
        return false;
    }

    g_minHookInitialized = false;
    return true;
}

TaskbarDetourResult RollBackInstallation(
    TaskbarDetourResult failure) noexcept {
    g_callback.store(nullptr, std::memory_order_release);

    if (g_hookCreated) {
        const MH_STATUS disableStatus =
            MH_DisableHook(g_taskbarModule.hookTarget);
        g_lastMinHookStatus = disableStatus;
        if (disableStatus != MH_OK &&
            disableStatus != MH_ERROR_DISABLED) {
            return Quarantine(failure, disableStatus);
        }
        g_hookEnabled = false;

        if (!DrainActiveCallbacks()) {
            return Quarantine(TaskbarDetourResult::DrainTimedOut);
        }

        const MH_STATUS removeStatus =
            MH_RemoveHook(g_taskbarModule.hookTarget);
        g_lastMinHookStatus = removeStatus;
        if (removeStatus != MH_OK) {
            return Quarantine(failure, removeStatus);
        }
        g_hookCreated = false;
    }

    if (!CleanInitializedBackend()) {
        return Quarantine(failure, g_lastMinHookStatus);
    }

    g_original.store(nullptr, std::memory_order_release);
    ReleaseValidatedTaskbarModule(g_taskbarModule);
    g_bootstrapThreadId = 0;
    g_state.store(TaskbarDetourState::Dormant,
                  std::memory_order_release);
    RecordResult(failure);
    return failure;
}

}  // namespace

TaskbarDetourResult StartTaskbarFrameDetour(
    TaskbarFrameLayoutCallback callback) noexcept {
    if (!callback) {
        ExclusiveRuntimeLock lock;
        RecordResult(TaskbarDetourResult::InvalidCallback);
        return TaskbarDetourResult::InvalidCallback;
    }

    ExclusiveRuntimeLock lock;
    const TaskbarDetourState state =
        g_state.load(std::memory_order_acquire);
    if (state == TaskbarDetourState::Active) {
        RecordResult(TaskbarDetourResult::AlreadyActive);
        return TaskbarDetourResult::AlreadyActive;
    }
    if (state == TaskbarDetourState::Validating ||
        state == TaskbarDetourState::Installing ||
        state == TaskbarDetourState::Stopping) {
        RecordResult(TaskbarDetourResult::Busy);
        return TaskbarDetourResult::Busy;
    }
    if (state == TaskbarDetourState::Quarantined) {
        RecordResult(TaskbarDetourResult::Quarantined);
        return TaskbarDetourResult::Quarantined;
    }

    g_state.store(TaskbarDetourState::Validating,
                  std::memory_order_release);
    HostCompatibilityReport compatibilityReport{};
    compatibilityReport.size = sizeof(compatibilityReport);
    compatibilityReport.apiVersion = kHostApiVersion;
    g_compatibility = AcquireValidatedTaskbarModule(
        compatibilityReport,
        g_taskbarModule);
    if (g_compatibility != HostCompatibility::Supported) {
        g_state.store(TaskbarDetourState::Dormant,
                      std::memory_order_release);
        RecordResult(TaskbarDetourResult::CompatibilityRejected);
        return TaskbarDetourResult::CompatibilityRejected;
    }

    if (GetCurrentThreadId() != g_taskbarModule.taskbarThreadId) {
        ReleaseValidatedTaskbarModule(g_taskbarModule);
        g_state.store(TaskbarDetourState::Dormant,
                      std::memory_order_release);
        RecordResult(TaskbarDetourResult::WrongTaskbarThread);
        return TaskbarDetourResult::WrongTaskbarThread;
    }
    g_bootstrapThreadId = GetCurrentThreadId();

    if (!PinHostModule()) {
        ReleaseValidatedTaskbarModule(g_taskbarModule);
        g_bootstrapThreadId = 0;
        g_state.store(TaskbarDetourState::Dormant,
                      std::memory_order_release);
        RecordResult(TaskbarDetourResult::HostPinFailed);
        return TaskbarDetourResult::HostPinFailed;
    }

    g_state.store(TaskbarDetourState::Installing,
                  std::memory_order_release);
    if (!RevalidateTaskbarHookTarget(g_taskbarModule)) {
        return RollBackInstallation(TaskbarDetourResult::TargetChanged);
    }

    MH_STATUS status = MH_Initialize();
    g_lastMinHookStatus = status;
    if (status != MH_OK) {
        return RollBackInstallation(TaskbarDetourResult::InitializeFailed);
    }
    g_minHookInitialized = true;

    if (!RevalidateTaskbarHookTarget(g_taskbarModule)) {
        return RollBackInstallation(TaskbarDetourResult::TargetChanged);
    }

    TaskbarFrameLayoutFunction original = nullptr;
    status = MH_CreateHook(
        g_taskbarModule.hookTarget,
        reinterpret_cast<void*>(&TaskbarFrameLayoutDetour),
        reinterpret_cast<void**>(&original));
    g_lastMinHookStatus = status;
    if (status != MH_OK || !original) {
        return RollBackInstallation(TaskbarDetourResult::CreateFailed);
    }
    g_hookCreated = true;
    g_original.store(original, std::memory_order_release);

    if (!RevalidateTaskbarHookTarget(g_taskbarModule)) {
        return RollBackInstallation(TaskbarDetourResult::TargetChanged);
    }

    g_callback.store(callback, std::memory_order_release);
    status = MH_EnableHook(g_taskbarModule.hookTarget);
    g_lastMinHookStatus = status;
    if (status != MH_OK) {
        return RollBackInstallation(TaskbarDetourResult::EnableFailed);
    }
    g_hookEnabled = true;

    g_state.store(TaskbarDetourState::Active,
                  std::memory_order_release);
    RecordResult(TaskbarDetourResult::Installed);
    return TaskbarDetourResult::Installed;
}

TaskbarDetourResult StopTaskbarFrameDetour(
    TaskbarRestoreCallback restore) noexcept {
    if (!restore) {
        ExclusiveRuntimeLock lock;
        RecordResult(TaskbarDetourResult::InvalidCallback);
        return TaskbarDetourResult::InvalidCallback;
    }
    if (g_detourDepth != 0) {
        return TaskbarDetourResult::Busy;
    }

    ExclusiveRuntimeLock lock;
    const TaskbarDetourState state =
        g_state.load(std::memory_order_acquire);
    if (state == TaskbarDetourState::Dormant ||
        state == TaskbarDetourState::Removed) {
        RecordResult(TaskbarDetourResult::NotActive);
        return TaskbarDetourResult::NotActive;
    }
    if (state == TaskbarDetourState::Quarantined) {
        RecordResult(TaskbarDetourResult::Quarantined);
        return TaskbarDetourResult::Quarantined;
    }
    if (state != TaskbarDetourState::Active ||
        GetCurrentThreadId() != g_bootstrapThreadId) {
        const TaskbarDetourResult result =
            state == TaskbarDetourState::Active
                ? TaskbarDetourResult::WrongTaskbarThread
                : TaskbarDetourResult::Busy;
        RecordResult(result);
        return result;
    }

    g_state.store(TaskbarDetourState::Stopping,
                  std::memory_order_release);
    g_callback.store(nullptr, std::memory_order_release);
    if (!restore()) {
        return Quarantine(TaskbarDetourResult::RestoreFailed);
    }

    if (g_hookEnabled) {
        const MH_STATUS disableStatus =
            MH_DisableHook(g_taskbarModule.hookTarget);
        g_lastMinHookStatus = disableStatus;
        if (disableStatus != MH_OK &&
            disableStatus != MH_ERROR_DISABLED) {
            return Quarantine(
                TaskbarDetourResult::DisableFailed,
                disableStatus);
        }
        g_hookEnabled = false;
    }

    if (!DrainActiveCallbacks()) {
        return Quarantine(TaskbarDetourResult::DrainTimedOut);
    }

    if (g_hookCreated) {
        const MH_STATUS removeStatus =
            MH_RemoveHook(g_taskbarModule.hookTarget);
        g_lastMinHookStatus = removeStatus;
        if (removeStatus != MH_OK) {
            return Quarantine(
                TaskbarDetourResult::RemoveFailed,
                removeStatus);
        }
        g_hookCreated = false;
    }

    if (!CleanInitializedBackend()) {
        return Quarantine(
            TaskbarDetourResult::UninitializeFailed,
            g_lastMinHookStatus);
    }

    g_original.store(nullptr, std::memory_order_release);
    ReleaseValidatedTaskbarModule(g_taskbarModule);
    g_bootstrapThreadId = 0;
    g_state.store(TaskbarDetourState::Removed,
                  std::memory_order_release);
    RecordResult(TaskbarDetourResult::Removed);
    return TaskbarDetourResult::Removed;
}

TaskbarDetourSnapshot GetTaskbarDetourSnapshot() noexcept {
    SharedRuntimeLock lock;
    TaskbarDetourSnapshot snapshot;
    snapshot.state = g_state.load(std::memory_order_acquire);
    snapshot.lastResult = g_lastResult;
    snapshot.compatibility = g_compatibility;
    snapshot.minHookStatus = static_cast<std::int32_t>(
        g_lastMinHookStatus);
    const LONG activeCallbacks = ReadActiveCallbackCount();
    snapshot.activeCallbacks = activeCallbacks < 0
        ? 0
        : static_cast<std::uint32_t>(activeCallbacks);
    snapshot.bootstrapThreadId = g_bootstrapThreadId;
    snapshot.hostPinned = g_hostPinned;
    return snapshot;
}

}  // namespace taskflyout::taskbar
