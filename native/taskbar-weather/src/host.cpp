#define TASKFLYOUT_TASKBAR_HOST_EXPORTS

#include "module_profile.h"
#include "taskbar_host_contract.h"

#include <Psapi.h>

#include <array>
#include <cwchar>

using taskflyout::taskbar::HostCompatibility;
using taskflyout::taskbar::HostCompatibilityReport;
using taskflyout::taskbar::PeFingerprint;

namespace {

HostCompatibility ProbeCurrentProcessImpl(
    HostCompatibilityReport& report) noexcept {
    std::array<wchar_t, MAX_PATH> processPath{};
    const DWORD processLength = GetModuleFileNameW(
        nullptr,
        processPath.data(),
        static_cast<DWORD>(processPath.size()));
    const wchar_t* processName = processLength == 0
        ? L""
        : wcsrchr(processPath.data(), L'\\');
    processName = processName ? processName + 1 : processPath.data();
    if (_wcsicmp(processName, L"explorer.exe") != 0) {
        return HostCompatibility::UnsupportedProcess;
    }

    report.windowsBuild = taskflyout::taskbar::GetWindowsBuildNumber();
    if (report.windowsBuild != taskflyout::taskbar::kValidatedWindowsBuild) {
        return HostCompatibility::UnsupportedWindowsBuild;
    }

    const HMODULE taskbarView = GetModuleHandleW(L"Taskbar.View.dll");
    if (!taskbarView) {
        return HostCompatibility::TaskbarViewMissing;
    }

    std::array<wchar_t, 32768> modulePath{};
    const DWORD moduleLength = GetModuleFileNameW(
        taskbarView,
        modulePath.data(),
        static_cast<DWORD>(modulePath.size()));
    if (moduleLength == 0 || moduleLength >= modulePath.size()) {
        return HostCompatibility::TaskbarViewUnreadable;
    }

    PeFingerprint fingerprint;
    std::wstring detail;
    if (!taskflyout::taskbar::ReadPeFingerprint(
            std::wstring(modulePath.data(), moduleLength),
            fingerprint,
            detail)) {
        return HostCompatibility::TaskbarViewUnreadable;
    }

    report.timeDateStamp = fingerprint.timeDateStamp;
    report.sizeOfImage = fingerprint.sizeOfImage;
    report.checksum = fingerprint.checksum;
    return taskflyout::taskbar::IsAllowlisted(
               report.windowsBuild,
               fingerprint)
        ? HostCompatibility::Supported
        : HostCompatibility::TaskbarViewNotAllowlisted;
}

}  // namespace

extern "C" __declspec(dllexport) std::uint32_t WINAPI
TaskFlyoutTaskbarHost_GetApiVersion() noexcept {
    return taskflyout::taskbar::kHostApiVersion;
}

extern "C" __declspec(dllexport) BOOL WINAPI
TaskFlyoutTaskbarHost_ProbeCurrentProcess(
    HostCompatibilityReport* report) noexcept {
    if (!report || report->size != sizeof(HostCompatibilityReport) ||
        report->apiVersion != taskflyout::taskbar::kHostApiVersion) {
        SetLastError(ERROR_INVALID_PARAMETER);
        return FALSE;
    }

    report->status = ProbeCurrentProcessImpl(*report);
    return TRUE;
}

extern "C" __declspec(dllexport) LRESULT CALLBACK
TaskFlyoutTaskbarHost_EntryHook(
    int code,
    WPARAM wParam,
    LPARAM lParam) noexcept {
    return CallNextHookEx(nullptr, code, wParam, lParam);
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, void*) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(instance);
    }
    return TRUE;
}

