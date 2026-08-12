#include "taskbar_control_transport.h"

#include <Windows.h>

#include <array>
#include <atomic>
#include <string>

namespace taskflyout::taskbar {
namespace {

constexpr DWORD kControlMessageTimeoutMilliseconds = 5000;
constexpr DWORD kAcknowledgementTimeoutMilliseconds = 1000;
constexpr wchar_t kDefaultHostFileName[] = L"TaskFlyout.TaskbarHost.dll";
std::atomic<std::uint32_t> g_controlNonceSequence{0};

using HostApiVersionFunction = std::uint32_t(WINAPI*)();
using HostEntryHookFunction = LRESULT(CALLBACK*)(
    int code,
    WPARAM wParam,
    LPARAM lParam);

std::wstring Win32Detail(
    const wchar_t* prefix,
    const DWORD error) {
    std::wstring detail(prefix ? prefix : L"win32-error");
    detail += L"-";
    detail += std::to_wstring(error);
    return detail;
}

std::wstring CurrentExecutablePath() {
    std::array<wchar_t, 32768> path{};
    const DWORD length = GetModuleFileNameW(
        nullptr,
        path.data(),
        static_cast<DWORD>(path.size()));
    if (length == 0 || length >= path.size()) {
        return {};
    }
    return std::wstring(path.data(), length);
}

std::wstring ResolveHostPath(std::wstring_view requested) {
    if (!requested.empty()) {
        return std::wstring(requested);
    }

    std::wstring executable = CurrentExecutablePath();
    const auto separator = executable.find_last_of(L"\\/");
    if (separator == std::wstring::npos) {
        return {};
    }
    executable.resize(separator + 1);
    executable += kDefaultHostFileName;
    return executable;
}

HWND FindVerifiedTaskbarWindow(
    const ProbeResult& probe,
    HostControlDispatchResult& result) {
    const HWND taskbar = FindWindowW(L"Shell_TrayWnd", nullptr);
    if (!taskbar) {
        result.status = HostControlDispatchStatus::TaskbarWindowChanged;
        result.detail = L"taskbar-window-missing-after-probe";
        return nullptr;
    }

    DWORD processId = 0;
    const DWORD threadId = GetWindowThreadProcessId(taskbar, &processId);
    if (processId == 0 || threadId == 0 ||
        processId != probe.module.processId ||
        threadId != probe.module.threadId) {
        result.status = HostControlDispatchStatus::TaskbarWindowChanged;
        result.detail = L"taskbar-owner-changed-after-probe";
        return nullptr;
    }

    result.processId = processId;
    result.threadId = threadId;
    return taskbar;
}

class LoadedHost final {
public:
    LoadedHost() = default;

    LoadedHost(const LoadedHost&) = delete;
    LoadedHost& operator=(const LoadedHost&) = delete;

    ~LoadedHost() {
        if (module_) {
            FreeLibrary(module_);
        }
    }

    HMODULE Load(const std::wstring& path) noexcept {
        module_ = LoadLibraryW(path.c_str());
        return module_;
    }

    HMODULE get() const noexcept {
        return module_;
    }

    void KeepLoaded() noexcept {
        module_ = nullptr;
    }

private:
    HMODULE module_ = nullptr;
};

class InstalledHook final {
public:
    InstalledHook() = default;

    InstalledHook(const InstalledHook&) = delete;
    InstalledHook& operator=(const InstalledHook&) = delete;

    ~InstalledHook() {
        if (hook_) {
            UnhookWindowsHookEx(hook_);
        }
    }

    HHOOK Install(
        HostEntryHookFunction callback,
        HMODULE module,
        DWORD threadId) noexcept {
        hook_ = SetWindowsHookExW(
            WH_CALLWNDPROC,
            callback,
            module,
            threadId);
        return hook_;
    }

    bool Remove() noexcept {
        if (!hook_) {
            return true;
        }
        const HHOOK hook = hook_;
        hook_ = nullptr;
        return UnhookWindowsHookEx(hook) != FALSE;
    }

private:
    HHOOK hook_ = nullptr;
};

class AcknowledgementWindow final {
public:
    AcknowledgementWindow() = default;

    AcknowledgementWindow(const AcknowledgementWindow&) = delete;
    AcknowledgementWindow& operator=(const AcknowledgementWindow&) = delete;

    ~AcknowledgementWindow() {
        if (window_) {
            DestroyWindow(window_);
        }
    }

    HWND Create() noexcept {
        window_ = CreateWindowExW(
            0,
            L"STATIC",
            L"",
            0,
            0,
            0,
            0,
            0,
            HWND_MESSAGE,
            nullptr,
            GetModuleHandleW(nullptr),
            nullptr);
        return window_;
    }

    HWND get() const noexcept {
        return window_;
    }

private:
    HWND window_ = nullptr;
};

enum class AcknowledgementWaitStatus : std::uint32_t {
    Received,
    TimedOut,
    WaitFailed,
};

std::uint32_t CreateControlNonce() noexcept {
    LARGE_INTEGER counter{};
    QueryPerformanceCounter(&counter);
    const std::uint32_t sequence =
        g_controlNonceSequence.fetch_add(1, std::memory_order_relaxed) + 1u;
    std::uint32_t nonce =
        static_cast<std::uint32_t>(counter.QuadPart) ^
        static_cast<std::uint32_t>(counter.QuadPart >> 32u) ^
        GetCurrentProcessId() ^
        (GetCurrentThreadId() << 16u) ^
        sequence;
    if (nonce == 0) {
        nonce = sequence == 0 ? 1u : sequence;
    }
    return nonce;
}

AcknowledgementWaitStatus WaitForControlAcknowledgement(
    const HWND window,
    const UINT messageId,
    const std::uint32_t nonce,
    HostControlAcknowledgementEnvelope& acknowledgement,
    DWORD& waitError) noexcept {
    const ULONGLONG deadline =
        GetTickCount64() + kAcknowledgementTimeoutMilliseconds;
    for (;;) {
        MSG message{};
        while (PeekMessageW(
                &message,
                window,
                messageId,
                messageId,
                PM_REMOVE)) {
            if (static_cast<std::uint32_t>(message.wParam) != nonce) {
                continue;
            }
            acknowledgement = DecodeHostControlAcknowledgement(
                static_cast<std::uintptr_t>(message.lParam));
            return AcknowledgementWaitStatus::Received;
        }

        const ULONGLONG now = GetTickCount64();
        if (now >= deadline) {
            return AcknowledgementWaitStatus::TimedOut;
        }
        const DWORD wait = MsgWaitForMultipleObjectsEx(
            0,
            nullptr,
            static_cast<DWORD>(deadline - now),
            QS_POSTMESSAGE,
            0);
        if (wait == WAIT_TIMEOUT) {
            return AcknowledgementWaitStatus::TimedOut;
        }
        if (wait == WAIT_FAILED) {
            waitError = GetLastError();
            return AcknowledgementWaitStatus::WaitFailed;
        }
    }
}

}  // namespace

HostControlDispatchStatus EvaluateHostControlAcknowledgement(
    const HostControlCommand command,
    const HostControlAcknowledgement acknowledgement) noexcept {
    if (!IsHostControlAcknowledgementForCommand(
            command,
            acknowledgement)) {
        return HostControlDispatchStatus::AcknowledgementInvalid;
    }
    if (acknowledgement == HostControlAcknowledgement::StartRejected ||
        acknowledgement == HostControlAcknowledgement::StopRejected ||
        acknowledgement == HostControlAcknowledgement::StatusRejected) {
        return HostControlDispatchStatus::ControllerRejected;
    }
    return HostControlDispatchStatus::Acknowledged;
}

HostControlDispatchResult DispatchHostControl(
    const HostControlCommand command,
    const std::wstring_view hostPath) {
    HostControlDispatchResult result;
    result.command = command;
    if (command != HostControlCommand::Start &&
        command != HostControlCommand::Stop &&
        command != HostControlCommand::Status) {
        result.status = HostControlDispatchStatus::InvalidCommand;
        result.detail = L"unsupported-control-command";
        return result;
    }

    const ProbeResult probe = ProbePrimaryTaskbar();
    result.probeStatus = probe.status;
    result.processId = probe.module.processId;
    result.threadId = probe.module.threadId;
    if (probe.status != ProbeStatus::Supported) {
        result.status = HostControlDispatchStatus::ProbeRejected;
        result.detail = probe.detail;
        return result;
    }

    const HWND taskbar = FindVerifiedTaskbarWindow(probe, result);
    if (!taskbar) {
        return result;
    }

    const UINT controlMessage = RegisterWindowMessageW(
        kHostControlMessageName);
    result.messageId = controlMessage;
    if (controlMessage == 0) {
        result.status = HostControlDispatchStatus::MessageRegistrationFailed;
        result.detail = Win32Detail(
            L"register-control-message-failed",
            GetLastError());
        return result;
    }

    const UINT acknowledgementMessage = RegisterWindowMessageW(
        kHostControlAcknowledgementMessageName);
    result.acknowledgementMessageId = acknowledgementMessage;
    if (acknowledgementMessage == 0) {
        result.status = HostControlDispatchStatus::MessageRegistrationFailed;
        result.detail = Win32Detail(
            L"register-acknowledgement-message-failed",
            GetLastError());
        return result;
    }

    AcknowledgementWindow acknowledgementWindow;
    if (!acknowledgementWindow.Create()) {
        result.status =
            HostControlDispatchStatus::AcknowledgementWindowFailed;
        result.detail = Win32Detail(
            L"create-acknowledgement-window-failed",
            GetLastError());
        return result;
    }
    const std::uint32_t nonce = CreateControlNonce();
    result.controlNonce = nonce;

    const std::wstring resolvedHostPath = ResolveHostPath(hostPath);
    if (resolvedHostPath.empty()) {
        result.status = HostControlDispatchStatus::HostPathUnavailable;
        result.detail = L"host-path-unavailable";
        return result;
    }

    LoadedHost host;
    if (!host.Load(resolvedHostPath)) {
        result.status = HostControlDispatchStatus::HostLoadFailed;
        result.detail = Win32Detail(L"load-host-failed", GetLastError());
        return result;
    }

    const auto getApiVersion = reinterpret_cast<HostApiVersionFunction>(
        GetProcAddress(host.get(), "TaskFlyoutTaskbarHost_GetApiVersion"));
    if (!getApiVersion || getApiVersion() != kHostApiVersion) {
        result.status = HostControlDispatchStatus::HostApiMismatch;
        result.detail = L"host-api-version-mismatch";
        return result;
    }

    const auto entryHook = reinterpret_cast<HostEntryHookFunction>(
        GetProcAddress(host.get(), "TaskFlyoutTaskbarHost_EntryHook"));
    if (!entryHook) {
        result.status = HostControlDispatchStatus::EntryHookUnavailable;
        result.detail = L"host-entry-hook-unavailable";
        return result;
    }

    InstalledHook hook;
    if (!hook.Install(entryHook, host.get(), probe.module.threadId)) {
        result.status = HostControlDispatchStatus::HookInstallFailed;
        result.detail = Win32Detail(
            L"install-thread-hook-failed",
            GetLastError());
        return result;
    }

    DWORD_PTR callbackResult = 0;
    if (!SendMessageTimeoutW(
             taskbar,
             controlMessage,
             static_cast<WPARAM>(EncodeHostControlRequest(command, nonce)),
             reinterpret_cast<LPARAM>(acknowledgementWindow.get()),
            SMTO_ABORTIFHUNG | SMTO_BLOCK | SMTO_ERRORONEXIT,
            kControlMessageTimeoutMilliseconds,
            &callbackResult)) {
        host.KeepLoaded();
        result.status = HostControlDispatchStatus::MessageDispatchFailed;
        result.detail = Win32Detail(
            L"send-control-message-failed",
            GetLastError());
        return result;
    }

    if (!hook.Remove()) {
        host.KeepLoaded();
        result.status = HostControlDispatchStatus::HookRemoveFailed;
        result.detail = Win32Detail(
            L"remove-thread-hook-failed",
            GetLastError());
        return result;
    }

    DWORD acknowledgementWaitError = ERROR_SUCCESS;
    HostControlAcknowledgementEnvelope acknowledgement;
    const AcknowledgementWaitStatus acknowledgementWait =
        WaitForControlAcknowledgement(
            acknowledgementWindow.get(),
            acknowledgementMessage,
            nonce,
            acknowledgement,
            acknowledgementWaitError);
    if (acknowledgementWait == AcknowledgementWaitStatus::TimedOut) {
        result.status = HostControlDispatchStatus::AcknowledgementTimedOut;
        result.detail = L"controller-acknowledgement-timed-out";
        return result;
    }
    if (acknowledgementWait == AcknowledgementWaitStatus::WaitFailed) {
        result.status =
            HostControlDispatchStatus::AcknowledgementWaitFailed;
        result.detail = Win32Detail(
            L"controller-acknowledgement-wait-failed",
            acknowledgementWaitError);
        return result;
    }
    result.acknowledgement = acknowledgement.acknowledgement;
    result.diagnostic = acknowledgement.diagnostic;
    result.status = EvaluateHostControlAcknowledgement(
        command,
        result.acknowledgement);
    if (result.status == HostControlDispatchStatus::AcknowledgementInvalid) {
        result.detail = L"controller-acknowledgement-invalid";
        return result;
    }
    if (result.status == HostControlDispatchStatus::ControllerRejected) {
        result.detail = L"controller-command-rejected";
        return result;
    }

    result.detail = L"controller-command-acknowledged";
    return result;
}

const wchar_t* HostControlDispatchStatusName(
    const HostControlDispatchStatus status) noexcept {
    switch (status) {
        case HostControlDispatchStatus::Acknowledged:
            return L"acknowledged";
        case HostControlDispatchStatus::InvalidCommand:
            return L"invalid-command";
        case HostControlDispatchStatus::ProbeRejected:
            return L"probe-rejected";
        case HostControlDispatchStatus::TaskbarWindowChanged:
            return L"taskbar-window-changed";
        case HostControlDispatchStatus::MessageRegistrationFailed:
            return L"message-registration-failed";
        case HostControlDispatchStatus::HostPathUnavailable:
            return L"host-path-unavailable";
        case HostControlDispatchStatus::HostLoadFailed:
            return L"host-load-failed";
        case HostControlDispatchStatus::HostApiMismatch:
            return L"host-api-mismatch";
        case HostControlDispatchStatus::EntryHookUnavailable:
            return L"entry-hook-unavailable";
        case HostControlDispatchStatus::HookInstallFailed:
            return L"hook-install-failed";
        case HostControlDispatchStatus::MessageDispatchFailed:
            return L"message-dispatch-failed";
        case HostControlDispatchStatus::HookRemoveFailed:
            return L"hook-remove-failed";
        case HostControlDispatchStatus::AcknowledgementWindowFailed:
            return L"acknowledgement-window-failed";
        case HostControlDispatchStatus::AcknowledgementTimedOut:
            return L"acknowledgement-timed-out";
        case HostControlDispatchStatus::AcknowledgementWaitFailed:
            return L"acknowledgement-wait-failed";
        case HostControlDispatchStatus::AcknowledgementInvalid:
            return L"acknowledgement-invalid";
        case HostControlDispatchStatus::ControllerRejected:
            return L"controller-rejected";
    }
    return L"unknown";
}

const wchar_t* HostControlAcknowledgementName(
    const HostControlAcknowledgement acknowledgement) noexcept {
    switch (acknowledgement) {
        case HostControlAcknowledgement::Unknown:
            return L"unknown";
        case HostControlAcknowledgement::Started:
            return L"started";
        case HostControlAcknowledgement::AlreadyStarted:
            return L"already-started";
        case HostControlAcknowledgement::Stopped:
            return L"stopped";
        case HostControlAcknowledgement::NotStarted:
            return L"not-started";
        case HostControlAcknowledgement::StartRejected:
            return L"start-rejected";
        case HostControlAcknowledgement::StopRejected:
            return L"stop-rejected";
        case HostControlAcknowledgement::MountReady:
            return L"mount-ready";
        case HostControlAcknowledgement::MountPending:
            return L"mount-pending";
        case HostControlAcknowledgement::StatusRejected:
            return L"status-rejected";
    }
    return L"invalid";
}

const wchar_t* HostControlDiagnosticName(
    const HostControlDiagnostic diagnostic) noexcept {
    switch (diagnostic) {
        case HostControlDiagnostic::None:
            return L"none";
        case HostControlDiagnostic::AwaitingLayout:
            return L"awaiting-layout";
        case HostControlDiagnostic::BridgeUnresolved:
            return L"bridge-unresolved";
        case HostControlDiagnostic::TreeProfileMismatch:
            return L"tree-profile-mismatch";
        case HostControlDiagnostic::LeaseUnavailable:
            return L"lease-unavailable";
        case HostControlDiagnostic::SlotStructureConflict:
            return L"slot-structure-conflict";
        case HostControlDiagnostic::SlotGeometryConflict:
            return L"slot-geometry-conflict";
        case HostControlDiagnostic::MountViewFailed:
            return L"mount-view-failed";
        case HostControlDiagnostic::MountAppendFailed:
            return L"mount-append-failed";
        case HostControlDiagnostic::MountRestoreFailed:
            return L"mount-restore-failed";
        case HostControlDiagnostic::MountedNotReady:
            return L"mounted-not-ready";
        case HostControlDiagnostic::MountReady:
            return L"mount-ready";
        case HostControlDiagnostic::DetourTargetNotObserved:
            return L"detour-target-not-observed";
        case HostControlDiagnostic::DetourInactive:
            return L"detour-inactive";
        case HostControlDiagnostic::CallbackUnavailable:
            return L"callback-unavailable";
        case HostControlDiagnostic::CallbackReentrant:
            return L"callback-reentrant";
        case HostControlDiagnostic::CallbackRecheckRace:
            return L"callback-recheck-race";
    }
    return L"invalid";
}

const wchar_t* HostControlCommandName(
    const HostControlCommand command) noexcept {
    switch (command) {
        case HostControlCommand::Start:
            return L"start";
        case HostControlCommand::Stop:
            return L"stop";
        case HostControlCommand::Status:
            return L"status";
    }
    return L"invalid";
}

}  // namespace taskflyout::taskbar
