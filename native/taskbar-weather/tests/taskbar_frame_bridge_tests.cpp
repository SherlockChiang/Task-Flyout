#include "taskbar_frame_bridge.h"

#include <cstdio>
#include <string_view>

namespace {

bool Expect(bool condition, const wchar_t* message) {
    if (condition) {
        return true;
    }
    fwprintf(stderr, L"FAILED: %ls\n", message);
    return false;
}

taskflyout::taskbar::TaskbarFrameBridgeGateInput ValidGate() {
    taskflyout::taskbar::TaskbarFrameBridgeGateInput input;
    input.privateObjectPresent = true;
    input.compatibilitySupported = true;
    input.detourActive = true;
    input.callbackScopeActive = true;
    input.ownerThread = true;
    input.inspectableSlotReadable = true;
    input.inspectablePointerPresent = true;
    input.inspectableObjectReadable = true;
    input.inspectableVtableReadable = true;
    input.inspectableMethodsValid = true;
    input.projectionSucceeded = true;
    input.frameTypeMatches = true;
    input.dispatcherAvailable = true;
    input.dispatcherHasThreadAccess = true;
    return input;
}

}  // namespace

int wmain() {
    using namespace taskflyout::taskbar;

    bool passed = true;
    TaskbarFrameBridgeGateInput input;
    passed &= Expect(
        EvaluateTaskbarFrameBridgeGate(input) ==
            TaskbarFrameBridgeStatus::NullPrivateObject,
        L"a null private object must fail first");

    input = ValidGate();
    input.compatibilitySupported = false;
    passed &= Expect(
        EvaluateTaskbarFrameBridgeGate(input) ==
            TaskbarFrameBridgeStatus::CompatibilityRejected,
        L"the exact PE profile is mandatory");
    input = ValidGate();
    input.detourActive = false;
    passed &= Expect(
        EvaluateTaskbarFrameBridgeGate(input) ==
            TaskbarFrameBridgeStatus::DetourInactive,
        L"the private bridge is valid only inside the active detour");
    input = ValidGate();
    input.callbackScopeActive = false;
    passed &= Expect(
        EvaluateTaskbarFrameBridgeGate(input) ==
            TaskbarFrameBridgeStatus::CallbackScopeInactive,
        L"the private pointer must be used only inside its callback scope");
    input = ValidGate();
    input.ownerThread = false;
    passed &= Expect(
        EvaluateTaskbarFrameBridgeGate(input) ==
            TaskbarFrameBridgeStatus::WrongOwnerThread,
        L"a foreign thread must fail before private memory is read");
    input = ValidGate();
    input.inspectableSlotReadable = false;
    passed &= Expect(
        EvaluateTaskbarFrameBridgeGate(input) ==
            TaskbarFrameBridgeStatus::InspectableSlotUnreadable,
        L"an unreadable private slot must fail closed");
    input = ValidGate();
    input.inspectableMethodsValid = false;
    passed &= Expect(
        EvaluateTaskbarFrameBridgeGate(input) ==
            TaskbarFrameBridgeStatus::InspectableMethodInvalid,
        L"IUnknown methods must point at executable memory");
    input = ValidGate();
    input.projectionSucceeded = false;
    passed &= Expect(
        EvaluateTaskbarFrameBridgeGate(input) ==
            TaskbarFrameBridgeStatus::ProjectionFailed,
        L"an unprojectable interface must fail closed");
    input = ValidGate();
    input.frameTypeMatches = false;
    passed &= Expect(
        EvaluateTaskbarFrameBridgeGate(input) ==
            TaskbarFrameBridgeStatus::FrameTypeMismatch,
        L"the runtime XAML class must match TaskbarFrame");
    input = ValidGate();
    input.dispatcherHasThreadAccess = false;
    passed &= Expect(
        EvaluateTaskbarFrameBridgeGate(input) ==
            TaskbarFrameBridgeStatus::DispatcherThreadMismatch,
        L"the XAML dispatcher must confirm thread access");
    input = ValidGate();
    passed &= Expect(
        EvaluateTaskbarFrameBridgeGate(input) ==
            TaskbarFrameBridgeStatus::Resolved,
        L"all private ABI gates should resolve the frame");
    passed &= Expect(
        std::wstring_view(TaskbarFrameBridgeStatusName(
            TaskbarFrameBridgeStatus::InspectableMethodInvalid)) ==
            L"inspectable-method-invalid",
        L"bridge failures should remain diagnosable");

    if (!passed) {
        return 1;
    }
    fputws(L"Taskbar frame bridge gate tests passed.\n", stdout);
    return 0;
}
