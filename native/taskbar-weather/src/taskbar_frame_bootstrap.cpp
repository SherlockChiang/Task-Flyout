#include "taskbar_frame_bootstrap.h"

#include <Windows.h>
#include <CoreWindow.h>

#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <winrt/base.h>

#include <array>
#include <cmath>
#include <cstdint>
#include <limits>
#include <string_view>

namespace taskflyout::taskbar {
namespace {

using winrt::Windows::Foundation::IUnknown;
using winrt::Windows::Foundation::Point;
using winrt::Windows::Foundation::Rect;
using winrt::Windows::Foundation::Collections::IIterable;
using winrt::Windows::Foundation::Collections::IIterator;
using winrt::Windows::UI::Xaml::UIElement;
using winrt::Windows::UI::Xaml::Window;
using winrt::Windows::UI::Xaml::Media::VisualTreeHelper;

constexpr wchar_t kTaskbarWindowClass[] = L"Shell_TrayWnd";
constexpr wchar_t kTaskbarFrameClass[] = L"Taskbar.TaskbarFrame";
constexpr double kDefaultDpi = 96.0;
constexpr ULONGLONG kEnumerationBudgetMilliseconds = 250;

bool IsExactTaskbarWindowClass(HWND window) noexcept {
    std::array<wchar_t, 64> className{};
    const int length = GetClassNameW(
        window,
        className.data(),
        static_cast<int>(className.size()));
    return length == static_cast<int>(std::size(kTaskbarWindowClass) - 1) &&
           std::wstring_view(
               className.data(),
               static_cast<std::size_t>(length)) == kTaskbarWindowClass;
}

bool TryGetHostBoundsInDips(HWND window, Rect& bounds) noexcept {
    bounds = {};
    RECT clientBounds{};
    if (GetClientRect(window, &clientBounds) == FALSE) {
        return false;
    }

    const std::int64_t widthPixels =
        static_cast<std::int64_t>(clientBounds.right) -
        static_cast<std::int64_t>(clientBounds.left);
    const std::int64_t heightPixels =
        static_cast<std::int64_t>(clientBounds.bottom) -
        static_cast<std::int64_t>(clientBounds.top);
    const UINT dpi = GetDpiForWindow(window);
    if (widthPixels <= 0 || heightPixels <= 0 || dpi == 0) {
        return false;
    }

    const double scale = kDefaultDpi / static_cast<double>(dpi);
    const double x = static_cast<double>(clientBounds.left) * scale;
    const double y = static_cast<double>(clientBounds.top) * scale;
    const double width = static_cast<double>(widthPixels) * scale;
    const double height = static_cast<double>(heightPixels) * scale;
    const double right = x + width;
    const double bottom = y + height;
    if (!std::isfinite(x) || !std::isfinite(y) ||
        !std::isfinite(width) || !std::isfinite(height) ||
        !std::isfinite(right) || !std::isfinite(bottom) ||
        width <= 0.0 || height <= 0.0 ||
        width > static_cast<double>(std::numeric_limits<float>::max()) ||
        height > static_cast<double>(std::numeric_limits<float>::max())) {
        return false;
    }

    bounds = Rect{
        static_cast<float>(x),
        static_cast<float>(y),
        static_cast<float>(width),
        static_cast<float>(height)};
    return std::isfinite(bounds.X) && std::isfinite(bounds.Y) &&
           std::isfinite(bounds.Width) && std::isfinite(bounds.Height) &&
           bounds.Width > 0.0f && bounds.Height > 0.0f;
}

bool TryGetCurrentWindowRoot(
    HWND taskbarWindow,
    UIElement& root,
    bool& associationKnown,
    bool& associatedWithTaskbar) noexcept {
    root = nullptr;
    associationKnown = false;
    associatedWithTaskbar = false;
    const Window currentWindow = Window::Current();
    if (!currentWindow) {
        return false;
    }

    root = currentWindow.Content();
    if (!root) {
        return false;
    }

    const auto coreWindow = currentWindow.CoreWindow();
    if (!coreWindow) {
        return true;
    }
    const auto interop = coreWindow.try_as<ICoreWindowInterop>();
    if (!interop) {
        return true;
    }
    HWND publicRootWindow = nullptr;
    if (FAILED(interop->get_WindowHandle(&publicRootWindow))) {
        return true;
    }
    associationKnown = true;
    associatedWithTaskbar = publicRootWindow == taskbarWindow;
    return true;
}

}  // namespace

TaskbarFrameBootstrapStatus EvaluateTaskbarFrameBootstrapPolicy(
    const TaskbarFrameBootstrapPolicyInput& input) noexcept {
    if (!input.windowExists || !input.windowClassMatches ||
        !input.windowProcessMatches) {
        return TaskbarFrameBootstrapStatus::WindowInvalid;
    }
    if (!input.ownerThreadMatches) {
        return TaskbarFrameBootstrapStatus::WrongOwnerThread;
    }
    if (!input.hostBoundsValid) {
        return TaskbarFrameBootstrapStatus::HostBoundsInvalid;
    }
    if (!input.querySucceeded) {
        return TaskbarFrameBootstrapStatus::QueryFailed;
    }
    if (input.enumerationOverflow) {
        return TaskbarFrameBootstrapStatus::EnumerationOverflow;
    }
    if (input.uniqueFrameCount == 0) {
        return TaskbarFrameBootstrapStatus::FrameNotObserved;
    }
    if (input.uniqueFrameCount > 1) {
        return TaskbarFrameBootstrapStatus::FrameAmbiguous;
    }
    if (input.treeProfileStatus ==
        TaskbarTreeProbeStatus::XamlTreeUnavailable) {
        return TaskbarFrameBootstrapStatus::TreeProbeUnavailable;
    }
    if (input.treeProfileStatus != TaskbarTreeProbeStatus::LandmarksMatched) {
        return TaskbarFrameBootstrapStatus::TreeProfileMismatch;
    }
    return TaskbarFrameBootstrapStatus::FrameValidated;
}

TaskbarFrameBootstrapStatus TaskbarFrameBootstrapFailureStatus(
    const TaskbarFrameBootstrapFailureStage stage) noexcept {
    switch (stage) {
        case TaskbarFrameBootstrapFailureStage::HostQuery:
            return TaskbarFrameBootstrapStatus::HostQueryFailed;
        case TaskbarFrameBootstrapFailureStage::Enumeration:
            return TaskbarFrameBootstrapStatus::EnumerationFailed;
        case TaskbarFrameBootstrapFailureStage::ClassInspection:
            return TaskbarFrameBootstrapStatus::ClassInspectionFailed;
        case TaskbarFrameBootstrapFailureStage::IdentityProjection:
            return TaskbarFrameBootstrapStatus::IdentityProjectionFailed;
        case TaskbarFrameBootstrapFailureStage::Unknown:
            return TaskbarFrameBootstrapStatus::QueryFailed;
    }
    return TaskbarFrameBootstrapStatus::QueryFailed;
}

bool TrySelectTaskbarFrameBootstrapPoint(
    const TaskbarFrameBootstrapPointInput& input,
    Point& point) noexcept {
    point = {};
    if (!std::isfinite(input.x) || !std::isfinite(input.y) ||
        !std::isfinite(input.width) || !std::isfinite(input.height) ||
        input.width <= 0.0 || input.height <= 0.0) {
        return false;
    }

    const double centerX = input.x + input.width / 2.0;
    const double centerY = input.y + input.height / 2.0;
    if (!std::isfinite(centerX) || !std::isfinite(centerY) ||
        centerX < -static_cast<double>(std::numeric_limits<float>::max()) ||
        centerX > static_cast<double>(std::numeric_limits<float>::max()) ||
        centerY < -static_cast<double>(std::numeric_limits<float>::max()) ||
        centerY > static_cast<double>(std::numeric_limits<float>::max())) {
        return false;
    }

    point = Point{
        static_cast<float>(centerX),
        static_cast<float>(centerY)};
    return std::isfinite(point.X) && std::isfinite(point.Y);
}

TaskbarFrameBootstrapStatus EvaluateTaskbarPublicRootBootstrapPolicy(
    const TaskbarPublicRootBootstrapPolicyInput& input) noexcept {
    if (!input.windowExists || !input.windowClassMatches ||
        !input.windowProcessMatches) {
        return TaskbarFrameBootstrapStatus::WindowInvalid;
    }
    if (!input.ownerThreadMatches) {
        return TaskbarFrameBootstrapStatus::WrongOwnerThread;
    }
    if (!input.hostBoundsValid) {
        return TaskbarFrameBootstrapStatus::HostBoundsInvalid;
    }
    if (!input.rootLookupSucceeded) {
        return TaskbarFrameBootstrapStatus::RootQueryFailed;
    }
    if (!input.publicRootAvailable) {
        return TaskbarFrameBootstrapStatus::RootUnavailable;
    }
    if (!input.publicRootAssociationKnown) {
        return TaskbarFrameBootstrapStatus::RootQueryFailed;
    }
    if (!input.publicRootAssociatedWithTaskbar) {
        return TaskbarFrameBootstrapStatus::RootNotAssociatedWithTaskbar;
    }
    if (!input.subtreeQuerySucceeded) {
        return TaskbarFrameBootstrapStatus::RootQueryFailed;
    }
    if (input.enumerationOverflow) {
        return TaskbarFrameBootstrapStatus::RootEnumerationOverflow;
    }
    if (input.uniqueFrameCount == 0) {
        return TaskbarFrameBootstrapStatus::RootFrameNotObserved;
    }
    if (input.uniqueFrameCount > 1) {
        return TaskbarFrameBootstrapStatus::RootFrameAmbiguous;
    }
    if (input.treeProfileStatus !=
        TaskbarTreeProbeStatus::LandmarksMatched) {
        return TaskbarFrameBootstrapStatus::RootTreeProfileMismatch;
    }
    return TaskbarFrameBootstrapStatus::RootBootstrapValidated;
}

TaskbarFrameBootstrapStatus ProbeTaskbarFrameBootstrap(
    HWND taskbarWindow) noexcept {
    TaskbarPublicRootBootstrapPolicyInput input;
    input.windowExists = taskbarWindow && IsWindow(taskbarWindow) != FALSE;
    if (!input.windowExists) {
        return EvaluateTaskbarPublicRootBootstrapPolicy(input);
    }

    input.windowClassMatches =
        IsExactTaskbarWindowClass(taskbarWindow);
    DWORD windowProcessId = 0;
    const DWORD ownerThreadId =
        GetWindowThreadProcessId(taskbarWindow, &windowProcessId);
    input.windowProcessMatches = ownerThreadId != 0 &&
        windowProcessId == GetCurrentProcessId();
    input.ownerThreadMatches = ownerThreadId != 0 &&
        ownerThreadId == GetCurrentThreadId();
    TaskbarFrameBootstrapStatus status =
        EvaluateTaskbarPublicRootBootstrapPolicy(input);
    if (status == TaskbarFrameBootstrapStatus::WindowInvalid ||
        status == TaskbarFrameBootstrapStatus::WrongOwnerThread) {
        return status;
    }

    Rect hostBounds{};
    input.hostBoundsValid =
        TryGetHostBoundsInDips(taskbarWindow, hostBounds);
    status = EvaluateTaskbarPublicRootBootstrapPolicy(input);
    if (status == TaskbarFrameBootstrapStatus::HostBoundsInvalid) {
        return status;
    }
    Point hostPoint{};
    input.hostBoundsValid = TrySelectTaskbarFrameBootstrapPoint(
        {hostBounds.X, hostBounds.Y, hostBounds.Width, hostBounds.Height},
        hostPoint);
    if (!input.hostBoundsValid) {
        return TaskbarFrameBootstrapStatus::HostBoundsInvalid;
    }

    // Every projected object below is scoped to this owner-thread block. The
    // probe does not create or initialize a XAML Window, call init_apartment,
    // mutate Content, or retain any XAML reference after return.
    try {
        UIElement publicRoot{nullptr};
        try {
            input.publicRootAvailable = TryGetCurrentWindowRoot(
                taskbarWindow,
                publicRoot,
                input.publicRootAssociationKnown,
                input.publicRootAssociatedWithTaskbar);
            input.rootLookupSucceeded = true;
        } catch (...) {
            return TaskbarFrameBootstrapStatus::RootQueryFailed;
        }
        status = EvaluateTaskbarPublicRootBootstrapPolicy(input);
        if (status == TaskbarFrameBootstrapStatus::RootUnavailable ||
            status == TaskbarFrameBootstrapStatus::
                RootNotAssociatedWithTaskbar) {
            return status;
        }

        IIterable<UIElement> elements{nullptr};
        try {
            elements = VisualTreeHelper::FindElementsInHostCoordinates(
                hostPoint,
                publicRoot,
                true);
            input.subtreeQuerySucceeded = true;
        } catch (...) {
            return TaskbarFrameBootstrapStatus::RootQueryFailed;
        }

        IIterator<UIElement> iterator{nullptr};
        try {
            iterator = elements.First();
        } catch (...) {
            return TaskbarFrameBootstrapStatus::RootQueryFailed;
        }
        std::array<IUnknown, kMaxTaskbarFrameBootstrapElements>
            frameIdentities{};
        UIElement uniqueFrame{nullptr};
        std::size_t enumeratedCount = 0;
        std::size_t uniqueFrameCount = 0;
        const ULONGLONG enumerationStarted = GetTickCount64();

        for (;;) {
            bool hasCurrent = false;
            try {
                hasCurrent = iterator.HasCurrent();
            } catch (...) {
                return TaskbarFrameBootstrapStatus::RootQueryFailed;
            }
            if (GetTickCount64() - enumerationStarted >=
                    kEnumerationBudgetMilliseconds ||
                enumeratedCount >=
                    kMaxTaskbarFrameBootstrapElements) {
                input.enumerationOverflow = true;
                break;
            }
            if (!hasCurrent) {
                break;
            }

            UIElement element{nullptr};
            try {
                element = iterator.Current();
            } catch (...) {
                return TaskbarFrameBootstrapStatus::RootQueryFailed;
            }
            ++enumeratedCount;
            if (element &&
                winrt::get_class_name(element) == kTaskbarFrameClass) {
                const IUnknown identity = element.as<IUnknown>();
                const void* const identityAddress = winrt::get_abi(identity);
                bool alreadyObserved = false;
                for (std::size_t index = 0;
                     index < uniqueFrameCount;
                     ++index) {
                    if (winrt::get_abi(frameIdentities[index]) ==
                        identityAddress) {
                        alreadyObserved = true;
                        break;
                    }
                }
                if (!alreadyObserved) {
                    frameIdentities[uniqueFrameCount++] = identity;
                    if (uniqueFrameCount == 1) {
                        uniqueFrame = element;
                    }
                    if (uniqueFrameCount > 1) {
                        break;
                    }
                }
            }
            try {
                iterator.MoveNext();
            } catch (...) {
                return TaskbarFrameBootstrapStatus::RootQueryFailed;
            }
        }

        input.uniqueFrameCount =
            static_cast<std::uint32_t>(uniqueFrameCount);
        if (!input.enumerationOverflow && uniqueFrameCount == 1) {
            const TaskbarTreeProfile profile =
                ProbeTaskbarFrameTree(uniqueFrame);
            input.treeProfileStatus = profile.status;
        }
    } catch (...) {
        return TaskbarFrameBootstrapStatus::RootQueryFailed;
    }

    return EvaluateTaskbarPublicRootBootstrapPolicy(input);
}

const wchar_t* TaskbarFrameBootstrapStatusName(
    const TaskbarFrameBootstrapStatus status) noexcept {
    switch (status) {
        case TaskbarFrameBootstrapStatus::WindowInvalid:
            return L"window-invalid";
        case TaskbarFrameBootstrapStatus::WrongOwnerThread:
            return L"wrong-owner-thread";
        case TaskbarFrameBootstrapStatus::HostBoundsInvalid:
            return L"host-bounds-invalid";
        case TaskbarFrameBootstrapStatus::QueryFailed:
            return L"query-failed";
        case TaskbarFrameBootstrapStatus::EnumerationOverflow:
            return L"enumeration-overflow";
        case TaskbarFrameBootstrapStatus::FrameNotObserved:
            return L"frame-not-observed";
        case TaskbarFrameBootstrapStatus::FrameAmbiguous:
            return L"frame-ambiguous";
        case TaskbarFrameBootstrapStatus::TreeProfileMismatch:
            return L"tree-profile-mismatch";
        case TaskbarFrameBootstrapStatus::FrameValidated:
            return L"frame-validated";
        case TaskbarFrameBootstrapStatus::HostQueryFailed:
            return L"host-query-failed";
        case TaskbarFrameBootstrapStatus::EnumerationFailed:
            return L"enumeration-failed";
        case TaskbarFrameBootstrapStatus::ClassInspectionFailed:
            return L"class-inspection-failed";
        case TaskbarFrameBootstrapStatus::IdentityProjectionFailed:
            return L"identity-projection-failed";
        case TaskbarFrameBootstrapStatus::TreeProbeUnavailable:
            return L"tree-probe-unavailable";
        case TaskbarFrameBootstrapStatus::RootUnavailable:
            return L"root-unavailable";
        case TaskbarFrameBootstrapStatus::RootQueryFailed:
            return L"root-query-failed";
        case TaskbarFrameBootstrapStatus::RootNotAssociatedWithTaskbar:
            return L"root-not-associated-with-taskbar";
        case TaskbarFrameBootstrapStatus::RootFrameNotObserved:
            return L"root-frame-not-observed";
        case TaskbarFrameBootstrapStatus::RootFrameAmbiguous:
            return L"root-frame-ambiguous";
        case TaskbarFrameBootstrapStatus::RootTreeProfileMismatch:
            return L"root-tree-profile-mismatch";
        case TaskbarFrameBootstrapStatus::RootBootstrapValidated:
            return L"root-bootstrap-validated";
        case TaskbarFrameBootstrapStatus::RootEnumerationOverflow:
            return L"root-enumeration-overflow";
    }
    return L"unknown";
}

}  // namespace taskflyout::taskbar
