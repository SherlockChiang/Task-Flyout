#include "taskbar_control_transport.h"

#include <Windows.h>

#include <array>
#include <string>

namespace taskflyout::taskbar {
namespace {

constexpr DWORD kControlMessageTimeoutMilliseconds = 5000;
constexpr wchar_t kDefaultHostFileName[] = L"TaskFlyout.TaskbarHost.dll";

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

}  // namespace

HostControlDispatchResult DispatchHostControl(
    const HostControlCommand command,
    const std::wstring_view hostPath) {
    HostControlDispatchResult result;
    result.command = command;
    if (command != HostControlCommand::Start &&
        command != HostControlCommand::Stop) {
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
            static_cast<WPARAM>(command),
            0,
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

    result.status = HostControlDispatchStatus::Dispatched;
    result.detail = L"control-message-dispatched";
    return result;
}

const wchar_t* HostControlDispatchStatusName(
    const HostControlDispatchStatus status) noexcept {
    switch (status) {
        case HostControlDispatchStatus::Dispatched:
            return L"dispatched";
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
    }
    return L"unknown";
}

}  // namespace taskflyout::taskbar
