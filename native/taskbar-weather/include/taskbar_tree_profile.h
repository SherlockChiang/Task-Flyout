#pragma once

#include <winrt/Windows.UI.Xaml.Controls.h>

#include <cstdint>

namespace taskflyout::taskbar {

enum class TaskbarTreeProbeStatus : std::uint32_t {
    LandmarksMatched = 0,
    FrameTypeMismatch = 1,
    FrameNotLoaded = 2,
    FrameGeometryInvalid = 3,
    RootGridMissing = 4,
    RootGridDuplicate = 5,
    RootGridTypeMismatch = 6,
    RootGridNotLoaded = 7,
    RootGridGeometryInvalid = 8,
    RootGridXamlRootMismatch = 9,
    BackgroundMissing = 10,
    BackgroundDuplicate = 11,
    RepeaterMissing = 12,
    RepeaterDuplicate = 13,
    XamlTreeUnavailable = 14,
};

struct TaskbarTreeSignature {
    bool frameClassMatches = false;
    bool frameLoaded = false;
    bool frameGeometryValid = false;
    std::uint32_t rootGridDirectCount = 0;
    bool rootGridTypeMatches = false;
    bool rootGridLoaded = false;
    bool rootGridGeometryValid = false;
    bool rootGridSharesXamlRoot = false;
    std::uint32_t rootGridChildCount = 0;
    std::uint32_t backgroundDirectCount = 0;
    std::uint32_t repeaterDirectCount = 0;
};

// This result owns apartment-affine XAML references. Create, consume, and
// destroy it synchronously on the TaskbarFrame owner thread. Persistent mount
// state must keep weak references instead.
struct TaskbarTreeProfile {
    TaskbarTreeProbeStatus status =
        TaskbarTreeProbeStatus::XamlTreeUnavailable;
    const wchar_t* detail = L"xaml-tree-unavailable";
    TaskbarTreeSignature signature;
    std::uint32_t ownerThreadId = 0;
    winrt::Windows::UI::Xaml::FrameworkElement frame{nullptr};
    winrt::Windows::UI::Xaml::Controls::Grid rootGrid{nullptr};
    winrt::Windows::UI::Xaml::FrameworkElement repeater{nullptr};
};

TaskbarTreeProbeStatus EvaluateTaskbarTreeSignature(
    const TaskbarTreeSignature& signature) noexcept;

TaskbarTreeProfile ProbeTaskbarFrameTree(
    const winrt::Windows::UI::Xaml::DependencyObject& frame) noexcept;

const wchar_t* TaskbarTreeProbeStatusName(
    TaskbarTreeProbeStatus status) noexcept;

}  // namespace taskflyout::taskbar
