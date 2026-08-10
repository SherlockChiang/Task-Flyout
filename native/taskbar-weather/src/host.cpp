#define TASKFLYOUT_TASKBAR_HOST_EXPORTS

#include "taskbar_host_controller.h"
#include "loaded_taskbar_profile.h"
#include "taskbar_host_contract.h"

using taskflyout::taskbar::HostCompatibilityReport;

namespace {

UINT HostControlMessage() noexcept {
    static const UINT message = RegisterWindowMessageW(
        taskflyout::taskbar::kHostControlMessageName);
    return message;
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

    report->status = taskflyout::taskbar::ProbeLoadedTaskbarHost(*report);
    return TRUE;
}

extern "C" __declspec(dllexport) LRESULT CALLBACK
TaskFlyoutTaskbarHost_EntryHook(
    int code,
    WPARAM wParam,
    LPARAM lParam) noexcept {
    if (code == HC_ACTION && lParam != 0) {
        try {
            const UINT controlMessage = HostControlMessage();
            const auto* message = reinterpret_cast<const CWPSTRUCT*>(lParam);
            if (controlMessage != 0 && message &&
                message->message == controlMessage) {
                const auto command =
                    static_cast<taskflyout::taskbar::HostControlCommand>(
                        message->wParam);
                if (command ==
                    taskflyout::taskbar::HostControlCommand::Start) {
                    taskflyout::taskbar::StartTaskbarWeatherController();
                } else if (command ==
                    taskflyout::taskbar::HostControlCommand::Stop) {
                    taskflyout::taskbar::StopTaskbarWeatherController();
                }
            }
        } catch (...) {
            // Never unwind through a Windows hook callback.
        }
    }
    return CallNextHookEx(nullptr, code, wParam, lParam);
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, void*) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(instance);
    }
    return TRUE;
}
