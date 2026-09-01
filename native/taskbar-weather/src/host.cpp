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

UINT HostControlAcknowledgementMessage() noexcept {
    static const UINT message = RegisterWindowMessageW(
        taskflyout::taskbar::kHostControlAcknowledgementMessageName);
    return message;
}

bool IsSameSessionReplyWindow(const HWND window) noexcept {
    if (!window || !IsWindow(window)) {
        return false;
    }
    DWORD replyProcessId = 0;
    if (GetWindowThreadProcessId(window, &replyProcessId) == 0 ||
        replyProcessId == 0) {
        return false;
    }
    DWORD currentSessionId = 0;
    DWORD replySessionId = 0;
    return ProcessIdToSessionId(GetCurrentProcessId(), &currentSessionId) &&
        ProcessIdToSessionId(replyProcessId, &replySessionId) &&
        currentSessionId == replySessionId;
}

taskflyout::taskbar::HostControlAcknowledgement MapControllerResult(
    const taskflyout::taskbar::TaskbarHostControllerResult result) noexcept {
    using taskflyout::taskbar::HostControlAcknowledgement;
    using taskflyout::taskbar::TaskbarHostControllerResult;
    switch (result) {
        case TaskbarHostControllerResult::Started:
            return HostControlAcknowledgement::Started;
        case TaskbarHostControllerResult::AlreadyStarted:
            return HostControlAcknowledgement::AlreadyStarted;
        case TaskbarHostControllerResult::Stopped:
            return HostControlAcknowledgement::Stopped;
        case TaskbarHostControllerResult::NotStarted:
            return HostControlAcknowledgement::NotStarted;
        case TaskbarHostControllerResult::StartRejected:
            return HostControlAcknowledgement::StartRejected;
        case TaskbarHostControllerResult::StopRejected:
            return HostControlAcknowledgement::StopRejected;
        case TaskbarHostControllerResult::MountReady:
            return HostControlAcknowledgement::MountReady;
        case TaskbarHostControllerResult::MountPending:
            return HostControlAcknowledgement::MountPending;
        case TaskbarHostControllerResult::StatusRejected:
            return HostControlAcknowledgement::StatusRejected;
    }
    return HostControlAcknowledgement::Unknown;
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
                const UINT acknowledgementMessage =
                    HostControlAcknowledgementMessage();
                const auto request =
                    taskflyout::taskbar::DecodeHostControlRequest(
                        static_cast<std::uintptr_t>(message->wParam));
                const HWND acknowledgementWindow =
                    reinterpret_cast<HWND>(message->lParam);
                if (request.nonce != 0 && acknowledgementMessage != 0 &&
                    IsSameSessionReplyWindow(acknowledgementWindow)) {
                    using taskflyout::taskbar::HostControlCommand;
                    using taskflyout::taskbar::HostControlDiagnostic;
                    using taskflyout::taskbar::TaskbarHostControllerResult;
                    TaskbarHostControllerResult result =
                        TaskbarHostControllerResult::StartRejected;
                    HostControlDiagnostic diagnostic =
                        HostControlDiagnostic::None;
                    bool handled = true;
                    if (request.command == HostControlCommand::Start) {
                        result = taskflyout::taskbar::
                            StartTaskbarWeatherController(
                                request.nonce,
                                message->hwnd);
                        diagnostic = taskflyout::taskbar::
                            CurrentTaskbarWeatherControllerDiagnostic();
                    } else if (request.command == HostControlCommand::Stop) {
                        result = taskflyout::taskbar::
                            StopTaskbarWeatherController();
                        diagnostic = taskflyout::taskbar::
                            CurrentTaskbarWeatherControllerDiagnostic();
                    } else if (request.command == HostControlCommand::Status) {
                        const auto status = taskflyout::taskbar::
                            QueryTaskbarWeatherControllerStatusSnapshot();
                        result = status.result;
                        diagnostic = status.diagnostic;
                    } else {
                        handled = false;
                    }
                    if (handled) {
                        PostMessageW(
                            acknowledgementWindow,
                            acknowledgementMessage,
                            static_cast<WPARAM>(request.nonce),
                            static_cast<LPARAM>(
                                taskflyout::taskbar::
                                    EncodeHostControlAcknowledgement(
                                        MapControllerResult(result),
                                        diagnostic)));
                    }
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
