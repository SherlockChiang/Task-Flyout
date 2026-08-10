#define TASKFLYOUT_TASKBAR_HOST_EXPORTS

#include "loaded_taskbar_profile.h"
#include "taskbar_host_contract.h"

using taskflyout::taskbar::HostCompatibilityReport;

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

    report->status = taskflyout::taskbar::ProbeLoadedTaskbarHost(*report);
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
