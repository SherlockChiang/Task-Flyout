#pragma once

#include "taskbar_tree_profile.h"

#include <Windows.h>

#include <cstddef>
#include <cstdint>

namespace taskflyout::taskbar {

enum class TaskbarFrameBootstrapStatus : std::uint32_t {
    WindowInvalid = 0,
    WrongOwnerThread = 1,
    HostBoundsInvalid = 2,
    QueryFailed = 3,
    EnumerationOverflow = 4,
    FrameNotObserved = 5,
    FrameAmbiguous = 6,
    TreeProfileMismatch = 7,
    FrameValidated = 8,
    HostQueryFailed = 9,
    EnumerationFailed = 10,
    ClassInspectionFailed = 11,
    IdentityProjectionFailed = 12,
    TreeProbeUnavailable = 13,
};

enum class TaskbarFrameBootstrapFailureStage : std::uint32_t {
    Unknown = 0,
    HostQuery = 1,
    Enumeration = 2,
    ClassInspection = 3,
    IdentityProjection = 4,
};

inline constexpr std::size_t kMaxTaskbarFrameBootstrapElements = 1024;

// Pure policy input used to test the fail-closed ordering without requiring a
// live Explorer XAML island. uniqueFrameCount is the number of distinct
// Taskbar.TaskbarFrame IUnknown identities observed by the bounded query.
struct TaskbarFrameBootstrapPolicyInput {
    bool windowExists = false;
    bool windowClassMatches = false;
    bool windowProcessMatches = false;
    bool ownerThreadMatches = false;
    bool hostBoundsValid = false;
    bool querySucceeded = false;
    bool enumerationOverflow = false;
    std::uint32_t uniqueFrameCount = 0;
    TaskbarTreeProbeStatus treeProfileStatus =
        TaskbarTreeProbeStatus::XamlTreeUnavailable;
};

struct TaskbarFrameBootstrapPointInput {
    double x = 0.0;
    double y = 0.0;
    double width = 0.0;
    double height = 0.0;
};

TaskbarFrameBootstrapStatus EvaluateTaskbarFrameBootstrapPolicy(
    const TaskbarFrameBootstrapPolicyInput& input) noexcept;

TaskbarFrameBootstrapStatus TaskbarFrameBootstrapFailureStatus(
    TaskbarFrameBootstrapFailureStage stage) noexcept;

bool TrySelectTaskbarFrameBootstrapPoint(
    const TaskbarFrameBootstrapPointInput& input,
    winrt::Windows::Foundation::Point& point) noexcept;

// This probe must run synchronously on the Shell_TrayWnd owner thread. It does
// not initialize a COM apartment and retains no XAML reference after return.
TaskbarFrameBootstrapStatus ProbeTaskbarFrameBootstrap(
    HWND taskbarWindow) noexcept;

const wchar_t* TaskbarFrameBootstrapStatusName(
    TaskbarFrameBootstrapStatus status) noexcept;

}  // namespace taskflyout::taskbar
