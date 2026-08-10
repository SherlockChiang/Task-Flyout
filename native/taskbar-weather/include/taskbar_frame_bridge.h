#pragma once

#include <winrt/Windows.UI.Xaml.Controls.h>

#include <cstdint>

namespace taskflyout::taskbar {

enum class TaskbarFrameBridgeStatus : std::uint32_t {
    Resolved = 0,
    NullPrivateObject = 1,
    CompatibilityRejected = 2,
    DetourInactive = 3,
    WrongOwnerThread = 4,
    InspectableSlotUnreadable = 5,
    InspectablePointerNull = 6,
    InspectableObjectUnreadable = 7,
    InspectableVtableUnreadable = 8,
    InspectableMethodInvalid = 9,
    ProjectionFailed = 10,
    FrameTypeMismatch = 11,
    DispatcherUnavailable = 12,
    DispatcherThreadMismatch = 13,
};

// Pure gate used to test the fail-closed ordering without manufacturing a
// private Taskbar.View object in the test process.
struct TaskbarFrameBridgeGateInput {
    bool privateObjectPresent = false;
    bool compatibilitySupported = false;
    bool detourActive = false;
    bool ownerThread = false;
    bool inspectableSlotReadable = false;
    bool inspectablePointerPresent = false;
    bool inspectableObjectReadable = false;
    bool inspectableVtableReadable = false;
    bool inspectableMethodsValid = false;
    bool projectionSucceeded = false;
    bool frameTypeMatches = false;
    bool dispatcherAvailable = false;
    bool dispatcherHasThreadAccess = false;
};

TaskbarFrameBridgeStatus EvaluateTaskbarFrameBridgeGate(
    const TaskbarFrameBridgeGateInput& input) noexcept;

// The returned FrameworkElement is apartment-affine. Resolve, consume, and
// release it synchronously inside the active TaskbarFrame detour callback.
struct TaskbarFrameBridgeResult {
    TaskbarFrameBridgeStatus status =
        TaskbarFrameBridgeStatus::NullPrivateObject;
    winrt::Windows::UI::Xaml::FrameworkElement frame{nullptr};
};

TaskbarFrameBridgeResult ResolveTaskbarFrameFromPrivateAbi(
    void* privateTaskbarFrame) noexcept;

const wchar_t* TaskbarFrameBridgeStatusName(
    TaskbarFrameBridgeStatus status) noexcept;

}  // namespace taskflyout::taskbar
