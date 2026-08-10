#include "taskbar_slot_probe.h"

#include <Windows.h>

#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.UI.Core.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Media.h>

#include <cmath>
#include <limits>

namespace taskflyout::taskbar {
namespace {

using winrt::Windows::UI::Xaml::DependencyObject;
using winrt::Windows::UI::Xaml::FrameworkElement;
using winrt::Windows::UI::Xaml::Controls::Grid;
using winrt::Windows::UI::Xaml::Media::VisualTreeHelper;

constexpr wchar_t kBackgroundClass[] = L"Taskbar.TaskbarBackground";
constexpr wchar_t kBackgroundName[] = L"BackgroundControl";
constexpr wchar_t kRepeaterClass[] =
    L"Microsoft.UI.Xaml.Controls.ItemsRepeater";
constexpr wchar_t kRepeaterName[] = L"TaskbarFrameRepeater";
constexpr double kMaximumDips = 65536.0;
constexpr double kGeometryEpsilon = 0.0001;

bool IsSameObject(
    const DependencyObject& first,
    const DependencyObject& second) noexcept {
    try {
        return first && second && first == second;
    } catch (...) {
        return false;
    }
}

bool IsBackgroundElement(const FrameworkElement& element) noexcept {
    try {
        return element &&
               winrt::get_class_name(element) == kBackgroundClass &&
               element.Name() == kBackgroundName;
    } catch (...) {
        return false;
    }
}

bool IsRepeaterElement(const FrameworkElement& element) noexcept {
    try {
        return element &&
               winrt::get_class_name(element) == kRepeaterClass &&
               element.Name() == kRepeaterName;
    } catch (...) {
        return false;
    }
}

bool IsFiniteBounded(const double value) noexcept {
    return std::isfinite(value) && std::abs(value) <= kMaximumDips;
}

bool IsFiniteEndpoint(
    const double origin,
    const double extent,
    double& endpoint) noexcept {
    endpoint = origin + extent;
    return std::isfinite(endpoint) && std::abs(endpoint) <= kMaximumDips;
}

bool IsValidTransformedRect(
    const winrt::Windows::Foundation::Rect& rect,
    TaskbarSlotRectDips& output) noexcept {
    const double x = rect.X;
    const double y = rect.Y;
    const double width = rect.Width;
    const double height = rect.Height;
    if (!IsFiniteBounded(x) || !IsFiniteBounded(y) ||
        !std::isfinite(width) || !std::isfinite(height) ||
        width <= kGeometryEpsilon || height <= kGeometryEpsilon ||
        width > kMaximumDips || height > kMaximumDips) {
        return false;
    }

    double right = 0.0;
    double bottom = 0.0;
    if (!IsFiniteEndpoint(x, width, right) ||
        !IsFiniteEndpoint(y, height, bottom)) {
        return false;
    }

    output = TaskbarSlotRectDips{x, y, width, height};
    return true;
}

bool IsDispatcherReady(const FrameworkElement& element) noexcept {
    try {
        const auto dispatcher = element.Dispatcher();
        return dispatcher && dispatcher.HasThreadAccess();
    } catch (...) {
        return false;
    }
}

bool IsLoadedAndOnSharedXamlRoot(
    const FrameworkElement& element,
    const Grid& rootGrid,
    bool& loaded,
    bool& visible) noexcept {
    try {
        loaded = element.IsLoaded();
        visible = element.Visibility() ==
            winrt::Windows::UI::Xaml::Visibility::Visible;
        if (!loaded || !visible) {
            return true;
        }
        const auto elementRoot = element.XamlRoot();
        const auto root = rootGrid.XamlRoot();
        return elementRoot && root && elementRoot == root;
    } catch (...) {
        return false;
    }
}

}  // namespace

TaskbarSlotStructureStatus EvaluateTaskbarSlotStructureSignature(
    const TaskbarSlotStructureSignature& signature) noexcept {
    if (!signature.expectedIdentitiesDistinct) {
        return TaskbarSlotStructureStatus::IdentityCollision;
    }
    if (signature.rootDirectChildCount > kMaxTaskbarSlotRootChildren) {
        return TaskbarSlotStructureStatus::RootChildCountOutOfBounds;
    }
    if (signature.unknownDirectChildCount != 0) {
        return TaskbarSlotStructureStatus::UnknownRootChild;
    }
    if (signature.backgroundExactCount != 1) {
        return TaskbarSlotStructureStatus::BackgroundMismatch;
    }
    if (signature.repeaterExactCount != 1) {
        return TaskbarSlotStructureStatus::RepeaterMismatch;
    }
    if (signature.ownedChildExpected
            ? signature.ownedExactCount != 1
            : signature.ownedExactCount != 0) {
        return TaskbarSlotStructureStatus::OwnedChildMismatch;
    }

    const std::uint32_t expected =
        2u + (signature.ownedChildExpected ? 1u : 0u);
    if (signature.rootDirectChildCount != expected) {
        return TaskbarSlotStructureStatus::RootChildCountMismatch;
    }

    const std::uint64_t observed =
        static_cast<std::uint64_t>(signature.backgroundExactCount) +
        static_cast<std::uint64_t>(signature.repeaterExactCount) +
        static_cast<std::uint64_t>(signature.ownedExactCount) +
        static_cast<std::uint64_t>(signature.unknownDirectChildCount);
    if (observed != signature.rootDirectChildCount) {
        return TaskbarSlotStructureStatus::SignatureInconsistent;
    }
    return TaskbarSlotStructureStatus::Matched;
}

const wchar_t* TaskbarSlotStructureStatusName(
    const TaskbarSlotStructureStatus status) noexcept {
    switch (status) {
        case TaskbarSlotStructureStatus::Matched:
            return L"matched";
        case TaskbarSlotStructureStatus::RootChildCountOutOfBounds:
            return L"root-child-count-out-of-bounds";
        case TaskbarSlotStructureStatus::RootChildCountMismatch:
            return L"root-child-count-mismatch";
        case TaskbarSlotStructureStatus::UnknownRootChild:
            return L"unknown-root-child";
        case TaskbarSlotStructureStatus::BackgroundMismatch:
            return L"background-mismatch";
        case TaskbarSlotStructureStatus::RepeaterMismatch:
            return L"repeater-mismatch";
        case TaskbarSlotStructureStatus::OwnedChildMismatch:
            return L"owned-child-mismatch";
        case TaskbarSlotStructureStatus::IdentityCollision:
            return L"identity-collision";
        case TaskbarSlotStructureStatus::SignatureInconsistent:
            return L"signature-inconsistent";
    }
    return L"unknown";
}

TaskbarSlotGeometryInput TaskbarSlotProbeResult::MakeGeometryInput()
    const noexcept {
    TaskbarSlotGeometryInput input;
    input.frameWidthDips = frameWidthDips;
    input.frameHeightDips = frameHeightDips;
    input.rootGridWidthDips = rootGridWidthDips;
    input.rootGridHeightDips = rootGridHeightDips;
    input.desiredWidthDips = options.desiredWidthDips;
    input.desiredHeightDips = options.desiredHeightDips;
    input.minimumWidthDips = options.minimumWidthDips;
    input.minimumHeightDips = options.minimumHeightDips;
    input.gapDips = options.gapDips;
    input.structureKnown =
        structureStatus == TaskbarSlotStructureStatus::Matched;
    input.blockersKnown = blockersKnown;
    input.interactiveBlockers = blockersKnown
        ? std::span<const TaskbarSlotRectDips>(
              blockerStorage.data(), blockerCount)
        : std::span<const TaskbarSlotRectDips>{};
    return input;
}

TaskbarSlotProbeResult ProbeTaskbarSlot(
    const TaskbarTreeProfile& profile,
    const TaskbarSlotProbeOptions& options,
    const DependencyObject& ownedRootChild) noexcept {
    TaskbarSlotProbeResult result;
    result.options = options;

    if (profile.status != TaskbarTreeProbeStatus::LandmarksMatched ||
        !profile.frame || !profile.rootGrid || !profile.repeater) {
        result.status = TaskbarSlotProbeStatus::TreeNotReady;
        return result;
    }
    if (profile.ownerThreadId == 0 ||
        profile.ownerThreadId != GetCurrentThreadId()) {
        result.status = TaskbarSlotProbeStatus::WrongOwnerThread;
        return result;
    }

    try {
        const auto frameDispatcher = profile.frame.Dispatcher();
        if (!frameDispatcher) {
            result.status = TaskbarSlotProbeStatus::DispatcherUnavailable;
            return result;
        }
        if (!frameDispatcher.HasThreadAccess()) {
            result.status = TaskbarSlotProbeStatus::DispatcherThreadMismatch;
            return result;
        }
        if (!IsDispatcherReady(profile.rootGrid) ||
            !IsDispatcherReady(profile.repeater)) {
            result.status = TaskbarSlotProbeStatus::DispatcherThreadMismatch;
            return result;
        }
        if (ownedRootChild) {
            const auto ownedElement = ownedRootChild.try_as<FrameworkElement>();
            if (!ownedElement) {
                result.status = TaskbarSlotProbeStatus::RootStructureMismatch;
                return result;
            }
            if (!IsDispatcherReady(ownedElement)) {
                result.status = TaskbarSlotProbeStatus::DispatcherThreadMismatch;
                return result;
            }
        }

        result.frameWidthDips = profile.frame.ActualWidth();
        result.frameHeightDips = profile.frame.ActualHeight();
        result.rootGridWidthDips = profile.rootGrid.ActualWidth();
        result.rootGridHeightDips = profile.rootGrid.ActualHeight();

        if (!profile.frame.IsLoaded() || !profile.rootGrid.IsLoaded() ||
            !profile.repeater.IsLoaded()) {
            result.status = TaskbarSlotProbeStatus::TreeNotReady;
            return result;
        }

        const auto frameXamlRoot = profile.frame.XamlRoot();
        const auto rootXamlRoot = profile.rootGrid.XamlRoot();
        const auto repeaterXamlRoot = profile.repeater.XamlRoot();
        if (!frameXamlRoot || !rootXamlRoot || !repeaterXamlRoot ||
            frameXamlRoot != rootXamlRoot ||
            frameXamlRoot != repeaterXamlRoot) {
            result.status = TaskbarSlotProbeStatus::TreeNotReady;
            return result;
        }

        const auto repeaterObject =
            profile.repeater.try_as<DependencyObject>();
        if (!repeaterObject) {
            result.status = TaskbarSlotProbeStatus::RootStructureMismatch;
            return result;
        }
        const auto ownedElement = ownedRootChild
            ? ownedRootChild.try_as<FrameworkElement>()
            : FrameworkElement{nullptr};
        const bool ownedExpected = !!ownedRootChild;
        result.structure.ownedChildExpected = ownedExpected;
        result.structure.expectedIdentitiesDistinct =
            !ownedExpected || !IsSameObject(ownedRootChild, repeaterObject);

        const int rootChildCount =
            VisualTreeHelper::GetChildrenCount(profile.rootGrid);
        if (rootChildCount < 0 ||
            rootChildCount > static_cast<int>(kMaxTaskbarSlotRootChildren)) {
            result.structure.rootDirectChildCount =
                rootChildCount < 0 ? 0u : static_cast<std::uint32_t>(rootChildCount);
            result.status = TaskbarSlotProbeStatus::RootChildCountOutOfBounds;
            return result;
        }
        result.structure.rootDirectChildCount =
            static_cast<std::uint32_t>(rootChildCount);

        for (int index = 0; index < rootChildCount; ++index) {
            const auto child = VisualTreeHelper::GetChild(
                profile.rootGrid, index);
            const auto childElement = child.try_as<FrameworkElement>();
            if (!childElement) {
                ++result.structure.unknownDirectChildCount;
                continue;
            }

            if (IsSameObject(child, repeaterObject) &&
                IsRepeaterElement(childElement)) {
                ++result.structure.repeaterExactCount;
                continue;
            }
            if (ownedExpected && IsSameObject(child, ownedRootChild)) {
                ++result.structure.ownedExactCount;
                continue;
            }
            if (IsBackgroundElement(childElement)) {
                ++result.structure.backgroundExactCount;
                continue;
            }
            ++result.structure.unknownDirectChildCount;
        }

        result.structureStatus = EvaluateTaskbarSlotStructureSignature(
            result.structure);
        if (result.structureStatus != TaskbarSlotStructureStatus::Matched) {
            result.status = TaskbarSlotProbeStatus::RootStructureMismatch;
            return result;
        }

        const int repeaterChildCount =
            VisualTreeHelper::GetChildrenCount(profile.repeater);
        if (repeaterChildCount <= 0 ||
            repeaterChildCount >
                static_cast<int>(kMaxTaskbarSlotRealizedChildren)) {
            result.status = TaskbarSlotProbeStatus::RepeaterChildCountOutOfBounds;
            return result;
        }

        for (int index = 0; index < repeaterChildCount; ++index) {
            const auto child = VisualTreeHelper::GetChild(
                profile.repeater, index);
            const auto childElement = child.try_as<FrameworkElement>();
            if (!childElement) {
                result.status =
                    TaskbarSlotProbeStatus::RepeaterChildNotFrameworkElement;
                return result;
            }

            bool loaded = false;
            bool visible = false;
            if (!IsLoadedAndOnSharedXamlRoot(
                    childElement, profile.rootGrid, loaded, visible)) {
                result.status =
                    TaskbarSlotProbeStatus::RepeaterChildStateUnavailable;
                return result;
            }
            // Collapsed or not-yet-loaded realized elements occupy no current
            // interactive bounds. They are intentionally omitted from this
            // synchronous snapshot; any subsequent layout callback must
            // probe again before using the result.
            if (!visible) {
                continue;
            }
            if (!loaded) {
                result.status =
                    TaskbarSlotProbeStatus::RepeaterChildStateUnavailable;
                return result;
            }

            const double width = childElement.ActualWidth();
            const double height = childElement.ActualHeight();
            if (!std::isfinite(width) || !std::isfinite(height) ||
                width < 0.0 || height < 0.0 ||
                width > kMaximumDips || height > kMaximumDips) {
                result.status =
                    TaskbarSlotProbeStatus::RepeaterChildGeometryInvalid;
                return result;
            }
            if (width <= kGeometryEpsilon || height <= kGeometryEpsilon) {
                result.status =
                    TaskbarSlotProbeStatus::RepeaterChildGeometryInvalid;
                return result;
            }

            const auto transform = childElement.TransformToVisual(
                profile.rootGrid);
            if (!transform) {
                result.status =
                    TaskbarSlotProbeStatus::RepeaterChildTransformUnavailable;
                return result;
            }
            const auto transformed = transform.TransformBounds(
                winrt::Windows::Foundation::Rect{
                    0.0f,
                    0.0f,
                    static_cast<float>(width),
                    static_cast<float>(height)});
            TaskbarSlotRectDips blocker;
            if (!IsValidTransformedRect(transformed, blocker)) {
                result.status =
                    TaskbarSlotProbeStatus::RepeaterChildTransformInvalid;
                return result;
            }
            if (result.blockerCount >= kMaxTaskbarSlotBlockers) {
                result.status = TaskbarSlotProbeStatus::RepeaterChildCountOutOfBounds;
                return result;
            }
            result.blockerStorage[result.blockerCount++] = blocker;
        }

        result.blockersKnown = true;
        const auto input = result.MakeGeometryInput();
        result.geometry = EvaluateTaskbarSlotGeometry(input);
        result.status =
            result.geometry.status ==
                    TaskbarSlotGeometryStatus::CandidateAvailable ||
                result.geometry.status ==
                    TaskbarSlotGeometryStatus::CandidateConflicted
            ? TaskbarSlotProbeStatus::SnapshotReady
            : TaskbarSlotProbeStatus::GeometryEvaluationFailed;
        return result;
    } catch (...) {
        result.status = TaskbarSlotProbeStatus::XamlTreeUnavailable;
        result.blockersKnown = false;
        result.blockerCount = 0;
        return result;
    }
}

const wchar_t* TaskbarSlotProbeStatusName(
    const TaskbarSlotProbeStatus status) noexcept {
    switch (status) {
        case TaskbarSlotProbeStatus::SnapshotReady:
            return L"snapshot-ready";
        case TaskbarSlotProbeStatus::TreeNotReady:
            return L"tree-not-ready";
        case TaskbarSlotProbeStatus::WrongOwnerThread:
            return L"wrong-owner-thread";
        case TaskbarSlotProbeStatus::DispatcherUnavailable:
            return L"dispatcher-unavailable";
        case TaskbarSlotProbeStatus::DispatcherThreadMismatch:
            return L"dispatcher-thread-mismatch";
        case TaskbarSlotProbeStatus::RootChildCountOutOfBounds:
            return L"root-child-count-out-of-bounds";
        case TaskbarSlotProbeStatus::RootStructureMismatch:
            return L"root-structure-mismatch";
        case TaskbarSlotProbeStatus::RepeaterChildCountOutOfBounds:
            return L"repeater-child-count-out-of-bounds";
        case TaskbarSlotProbeStatus::RepeaterChildNotFrameworkElement:
            return L"repeater-child-not-framework-element";
        case TaskbarSlotProbeStatus::RepeaterChildStateUnavailable:
            return L"repeater-child-state-unavailable";
        case TaskbarSlotProbeStatus::RepeaterChildGeometryInvalid:
            return L"repeater-child-geometry-invalid";
        case TaskbarSlotProbeStatus::RepeaterChildTransformUnavailable:
            return L"repeater-child-transform-unavailable";
        case TaskbarSlotProbeStatus::RepeaterChildTransformInvalid:
            return L"repeater-child-transform-invalid";
        case TaskbarSlotProbeStatus::GeometryEvaluationFailed:
            return L"geometry-evaluation-failed";
        case TaskbarSlotProbeStatus::XamlTreeUnavailable:
            return L"xaml-tree-unavailable";
    }
    return L"unknown";
}

}  // namespace taskflyout::taskbar
