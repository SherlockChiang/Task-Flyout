#include "taskbar_tree_profile.h"

#include <Windows.h>

#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Media.h>

#include <cmath>

namespace taskflyout::taskbar {
namespace {

using winrt::Windows::UI::Xaml::DependencyObject;
using winrt::Windows::UI::Xaml::FrameworkElement;
using winrt::Windows::UI::Xaml::Controls::Grid;
using winrt::Windows::UI::Xaml::Media::VisualTreeHelper;

constexpr wchar_t kTaskbarFrameClass[] = L"Taskbar.TaskbarFrame";
constexpr wchar_t kRootGridClass[] = L"Windows.UI.Xaml.Controls.Grid";
constexpr wchar_t kRootGridName[] = L"RootGrid";
constexpr wchar_t kBackgroundClass[] = L"Taskbar.TaskbarBackground";
constexpr wchar_t kBackgroundName[] = L"BackgroundControl";
constexpr wchar_t kRepeaterClass[] =
    L"Microsoft.UI.Xaml.Controls.ItemsRepeater";
constexpr wchar_t kRepeaterName[] = L"TaskbarFrameRepeater";
constexpr double kGeometryTolerance = 2.0;

bool IsReasonableTaskbarGeometry(
    const FrameworkElement& element) noexcept {
    try {
        const double width = element.ActualWidth();
        const double height = element.ActualHeight();
        return std::isfinite(width) && std::isfinite(height) &&
               width >= 64.0 && width <= 32768.0 &&
               height >= 24.0 && height <= 256.0 &&
               width > height;
    } catch (...) {
        return false;
    }
}

bool IsRootGridGeometryCompatible(
    const FrameworkElement& frame,
    const Grid& rootGrid) noexcept {
    try {
        if (!rootGrid.IsLoaded() ||
            !IsReasonableTaskbarGeometry(rootGrid)) {
            return false;
        }

        const double frameWidth = frame.ActualWidth();
        const double frameHeight = frame.ActualHeight();
        const double rootWidth = rootGrid.ActualWidth();
        const double rootHeight = rootGrid.ActualHeight();
        if (rootWidth > frameWidth + kGeometryTolerance ||
            rootHeight > frameHeight + kGeometryTolerance) {
            return false;
        }

        const auto transform = rootGrid.TransformToVisual(frame);
        if (!transform) {
            return false;
        }
        const auto bounds = transform.TransformBounds(
            winrt::Windows::Foundation::Rect{
                0.0f,
                0.0f,
                static_cast<float>(rootWidth),
                static_cast<float>(rootHeight)});
        const double left = bounds.X;
        const double top = bounds.Y;
        const double right = left + bounds.Width;
        const double bottom = top + bounds.Height;
        return std::isfinite(left) && std::isfinite(top) &&
               std::isfinite(right) && std::isfinite(bottom) &&
               bounds.Width > 0.0f && bounds.Height > 0.0f &&
               left >= -kGeometryTolerance &&
               top >= -kGeometryTolerance &&
               right <= frameWidth + kGeometryTolerance &&
               bottom <= frameHeight + kGeometryTolerance &&
               right > 0.0 && bottom > 0.0;
    } catch (...) {
        return false;
    }
}

}  // namespace

TaskbarTreeProbeStatus EvaluateTaskbarTreeSignature(
    const TaskbarTreeSignature& signature) noexcept {
    if (!signature.frameClassMatches) {
        return TaskbarTreeProbeStatus::FrameTypeMismatch;
    }
    if (!signature.frameLoaded) {
        return TaskbarTreeProbeStatus::FrameNotLoaded;
    }
    if (!signature.frameGeometryValid) {
        return TaskbarTreeProbeStatus::FrameGeometryInvalid;
    }
    if (signature.rootGridDirectCount == 0) {
        return TaskbarTreeProbeStatus::RootGridMissing;
    }
    if (signature.rootGridDirectCount > 1) {
        return TaskbarTreeProbeStatus::RootGridDuplicate;
    }
    if (!signature.rootGridTypeMatches) {
        return TaskbarTreeProbeStatus::RootGridTypeMismatch;
    }
    if (!signature.rootGridLoaded) {
        return TaskbarTreeProbeStatus::RootGridNotLoaded;
    }
    if (!signature.rootGridGeometryValid) {
        return TaskbarTreeProbeStatus::RootGridGeometryInvalid;
    }
    if (!signature.rootGridSharesXamlRoot) {
        return TaskbarTreeProbeStatus::RootGridXamlRootMismatch;
    }
    if (signature.backgroundDirectCount == 0) {
        return TaskbarTreeProbeStatus::BackgroundMissing;
    }
    if (signature.backgroundDirectCount > 1) {
        return TaskbarTreeProbeStatus::BackgroundDuplicate;
    }
    if (signature.repeaterDirectCount == 0) {
        return TaskbarTreeProbeStatus::RepeaterMissing;
    }
    if (signature.repeaterDirectCount > 1) {
        return TaskbarTreeProbeStatus::RepeaterDuplicate;
    }
    return TaskbarTreeProbeStatus::LandmarksMatched;
}

TaskbarTreeProfile ProbeTaskbarFrameTree(
    const DependencyObject& frame) noexcept {
    TaskbarTreeProfile profile;
    profile.ownerThreadId = GetCurrentThreadId();
    try {
        profile.frame = frame.try_as<FrameworkElement>();
        if (!profile.frame ||
            winrt::get_class_name(profile.frame) != kTaskbarFrameClass) {
            profile.signature.frameClassMatches = false;
            profile.status = EvaluateTaskbarTreeSignature(profile.signature);
            profile.detail = L"frame-class-mismatch";
            return profile;
        }
        profile.signature.frameClassMatches = true;
        profile.signature.frameLoaded = profile.frame.IsLoaded();
        profile.signature.frameGeometryValid =
            IsReasonableTaskbarGeometry(profile.frame);

        if (!profile.signature.frameLoaded ||
            !profile.signature.frameGeometryValid) {
            profile.status = EvaluateTaskbarTreeSignature(profile.signature);
            profile.detail = profile.signature.frameLoaded
                ? L"frame-geometry-invalid"
                : L"frame-not-loaded";
            return profile;
        }

        const int directChildCount = VisualTreeHelper::GetChildrenCount(frame);
        for (int index = 0; index < directChildCount; ++index) {
            const auto child = VisualTreeHelper::GetChild(frame, index);
            const auto childElement = child.try_as<FrameworkElement>();
            if (!childElement) {
                continue;
            }

            const auto className = winrt::get_class_name(childElement);
            if (className == kRootGridClass &&
                childElement.Name() == kRootGridName) {
                ++profile.signature.rootGridDirectCount;
                if (!profile.rootGrid) {
                    profile.rootGrid = child.try_as<Grid>();
                }
            }
        }

        if (profile.signature.rootGridDirectCount != 1 ||
            !profile.rootGrid) {
            profile.signature.rootGridTypeMatches =
                profile.signature.rootGridDirectCount == 1 &&
                profile.rootGrid;
            profile.status = EvaluateTaskbarTreeSignature(profile.signature);
            profile.detail = TaskbarTreeProbeStatusName(profile.status);
            return profile;
        }

        profile.signature.rootGridTypeMatches = true;
        profile.signature.rootGridLoaded = profile.rootGrid.IsLoaded();
        profile.signature.rootGridGeometryValid =
            IsRootGridGeometryCompatible(profile.frame, profile.rootGrid);
        try {
            const auto frameXamlRoot = profile.frame.XamlRoot();
            const auto rootGridXamlRoot = profile.rootGrid.XamlRoot();
            profile.signature.rootGridSharesXamlRoot =
                frameXamlRoot && rootGridXamlRoot &&
                frameXamlRoot == rootGridXamlRoot;
        } catch (...) {
            profile.signature.rootGridSharesXamlRoot = false;
        }

        const int rootChildCount =
            VisualTreeHelper::GetChildrenCount(profile.rootGrid);
        profile.signature.rootGridChildCount =
            rootChildCount < 0
                ? 0
                : static_cast<std::uint32_t>(rootChildCount);
        for (int index = 0; index < rootChildCount; ++index) {
            const auto child =
                VisualTreeHelper::GetChild(profile.rootGrid, index);
            const auto childElement = child.try_as<FrameworkElement>();
            if (!childElement) {
                continue;
            }

            const auto className = winrt::get_class_name(childElement);
            const auto name = childElement.Name();
            if (className == kBackgroundClass &&
                name == kBackgroundName) {
                ++profile.signature.backgroundDirectCount;
            }
            if (className == kRepeaterClass &&
                name == kRepeaterName) {
                ++profile.signature.repeaterDirectCount;
                if (!profile.repeater) {
                    profile.repeater = childElement;
                }
            }
        }

        profile.status = EvaluateTaskbarTreeSignature(profile.signature);
        profile.detail = TaskbarTreeProbeStatusName(profile.status);
        if (profile.status != TaskbarTreeProbeStatus::LandmarksMatched) {
            profile.rootGrid = nullptr;
            profile.repeater = nullptr;
        }
        return profile;
    } catch (...) {
        profile.status = TaskbarTreeProbeStatus::XamlTreeUnavailable;
        profile.detail = L"xaml-tree-unavailable";
        profile.rootGrid = nullptr;
        profile.repeater = nullptr;
        return profile;
    }
}

const wchar_t* TaskbarTreeProbeStatusName(
    TaskbarTreeProbeStatus status) noexcept {
    switch (status) {
        case TaskbarTreeProbeStatus::LandmarksMatched:
            return L"landmarks-matched";
        case TaskbarTreeProbeStatus::FrameTypeMismatch:
            return L"frame-type-mismatch";
        case TaskbarTreeProbeStatus::FrameNotLoaded:
            return L"frame-not-loaded";
        case TaskbarTreeProbeStatus::FrameGeometryInvalid:
            return L"frame-geometry-invalid";
        case TaskbarTreeProbeStatus::RootGridMissing:
            return L"root-grid-missing";
        case TaskbarTreeProbeStatus::RootGridDuplicate:
            return L"root-grid-duplicate";
        case TaskbarTreeProbeStatus::RootGridTypeMismatch:
            return L"root-grid-type-mismatch";
        case TaskbarTreeProbeStatus::RootGridNotLoaded:
            return L"root-grid-not-loaded";
        case TaskbarTreeProbeStatus::RootGridGeometryInvalid:
            return L"root-grid-geometry-invalid";
        case TaskbarTreeProbeStatus::RootGridXamlRootMismatch:
            return L"root-grid-xaml-root-mismatch";
        case TaskbarTreeProbeStatus::BackgroundMissing:
            return L"background-missing";
        case TaskbarTreeProbeStatus::BackgroundDuplicate:
            return L"background-duplicate";
        case TaskbarTreeProbeStatus::RepeaterMissing:
            return L"repeater-missing";
        case TaskbarTreeProbeStatus::RepeaterDuplicate:
            return L"repeater-duplicate";
        case TaskbarTreeProbeStatus::XamlTreeUnavailable:
            return L"xaml-tree-unavailable";
    }
    return L"unknown";
}

}  // namespace taskflyout::taskbar
