#include "taskbar_weather_mount.h"

#include <Windows.h>

#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.UI.Core.h>
#include <winrt/Windows.UI.Xaml.Automation.h>
#include <winrt/Windows.UI.Xaml.Controls.h>

namespace taskflyout::taskbar {
namespace {

using winrt::Windows::UI::Xaml::Controls::Button;
using winrt::Windows::UI::Xaml::Controls::Grid;
using winrt::Windows::UI::Xaml::HorizontalAlignment;
using winrt::Windows::UI::Xaml::Thickness;
using winrt::Windows::UI::Xaml::VerticalAlignment;

constexpr double kButtonWidth = 220.0;
constexpr double kButtonMinWidth = 112.0;
constexpr double kButtonHeight = 40.0;

bool IsOnOwnerThread(const TaskbarWeatherMountState& state) noexcept {
    if (state.ownerThreadId == 0 ||
        state.ownerThreadId != GetCurrentThreadId()) {
        return false;
    }
    try {
        const auto button = state.button.get();
        return button && button.Dispatcher() &&
               button.Dispatcher().HasThreadAccess();
    } catch (...) {
        return false;
    }
}

enum class ExactChildRemoval : std::uint32_t {
    Removed,
    Absent,
    Failed,
};

ExactChildRemoval RemoveExactChildIfPresent(
    const Grid& rootGrid,
    const Button& button) noexcept {
    try {
        if (!rootGrid || !button) {
            return ExactChildRemoval::Absent;
        }
        auto children = rootGrid.Children();
        for (std::uint32_t index = 0; index < children.Size(); ++index) {
            if (children.GetAt(index) == button) {
                children.RemoveAt(index);
                return ExactChildRemoval::Removed;
            }
        }
        return ExactChildRemoval::Absent;
    } catch (...) {
        return ExactChildRemoval::Failed;
    }
}

bool ContainsExactChild(
    const Grid& rootGrid,
    const Button& button) noexcept {
    try {
        if (!rootGrid || !button) {
            return false;
        }
        const auto children = rootGrid.Children();
        for (std::uint32_t index = 0; index < children.Size(); ++index) {
            if (children.GetAt(index) == button) {
                return true;
            }
        }
    } catch (...) {
    }
    return false;
}

bool IsOwnedButton(
    const Grid& rootGrid,
    const Button& button,
    const Grid& viewRoot) noexcept {
    try {
        return rootGrid && button && viewRoot &&
               button.Name() == kWeatherButtonName &&
               winrt::Windows::UI::Xaml::Automation::AutomationProperties::
                       GetAutomationId(button) ==
                   kWeatherButtonAutomationId &&
               button.Content().try_as<Grid>() == viewRoot &&
               ContainsExactChild(rootGrid, button) &&
               viewRoot.Name() == kWeatherHostName;
    } catch (...) {
        return false;
    }
}

Button CreateWeatherButton(
    const WeatherXamlView& view) {
    if (!ValidateWeatherXamlView(view)) {
        throw winrt::hresult_error(E_INVALIDARG);
    }

    Button button;
    button.Name(kWeatherButtonName);
    winrt::Windows::UI::Xaml::Automation::AutomationProperties::SetAutomationId(
        button,
        kWeatherButtonAutomationId);
    winrt::Windows::UI::Xaml::Automation::AutomationProperties::SetName(
        button,
        L"Task Flyout weather");
    button.Content(view.root);
    button.Width(kButtonWidth);
    button.MinWidth(kButtonMinWidth);
    button.MaxWidth(kButtonWidth);
    button.Height(kButtonHeight);
    button.Margin(Thickness{8.0, 0.0, 0.0, 0.0});
    button.HorizontalAlignment(HorizontalAlignment::Left);
    button.VerticalAlignment(VerticalAlignment::Center);
    button.HorizontalContentAlignment(HorizontalAlignment::Stretch);
    button.VerticalContentAlignment(VerticalAlignment::Center);
    return button;
}

}  // namespace

TaskbarMountStatus EvaluateTaskbarMountGate(
    const TaskbarMountGateInput& input) noexcept {
    if (input.treeStatus != TaskbarTreeProbeStatus::LandmarksMatched) {
        return TaskbarMountStatus::TreeNotReady;
    }
    if (!input.structureAllowlisted) {
        return TaskbarMountStatus::StructureNotAllowlisted;
    }
    if (!input.leftSlotAvailable) {
        return TaskbarMountStatus::LeftSlotUnavailable;
    }
    if (!input.ownerThread) {
        return TaskbarMountStatus::WrongOwnerThread;
    }
    return TaskbarMountStatus::Mounted;
}

TaskbarMountStatus MountWeatherButton(
    const TaskbarTreeProfile& profile,
    const WeatherViewModel& model,
    bool structureAllowlisted,
    bool leftSlotAvailable,
    TaskbarWeatherMountState& state) noexcept {
    const TaskbarMountGateInput gate{
        profile.status,
        structureAllowlisted,
        leftSlotAvailable,
        profile.ownerThreadId != 0 &&
            profile.ownerThreadId == GetCurrentThreadId()};
    const TaskbarMountStatus gateStatus =
        EvaluateTaskbarMountGate(gate);
    if (gateStatus != TaskbarMountStatus::Mounted) {
        return gateStatus;
    }
    if (!profile.rootGrid || !profile.frame) {
        return TaskbarMountStatus::TreeNotReady;
    }
    try {
        if (!profile.frame.Dispatcher() ||
            !profile.frame.Dispatcher().HasThreadAccess()) {
            return TaskbarMountStatus::WrongOwnerThread;
        }
    } catch (...) {
        return TaskbarMountStatus::WrongOwnerThread;
    }
    if (state.mounted) {
        const auto stateRoot = state.rootGrid.get();
        const auto stateButton = state.button.get();
        if (!stateRoot || !stateButton) {
            if (state.ownerThreadId != GetCurrentThreadId()) {
                return TaskbarMountStatus::WrongOwnerThread;
            }
            state = {};
        } else if (stateRoot == profile.rootGrid &&
                   IsOnOwnerThread(state)) {
            const auto updateStatus = UpdateWeatherButton(model, state);
            return updateStatus == TaskbarMountStatus::Updated
                ? TaskbarMountStatus::AlreadyMounted
                : updateStatus;
        } else {
            return TaskbarMountStatus::InvalidState;
        }
    }

    Button button{nullptr};
    WeatherXamlView view;
    try {
        view = CreateWeatherXamlView(model);
    } catch (...) {
        return TaskbarMountStatus::ViewCreationFailed;
    }

    try {
        button = CreateWeatherButton(view);
        profile.rootGrid.Children().Append(button);

        state.rootGrid = profile.rootGrid;
        state.button = button;
        state.viewRoot = view.root;
        state.icon = view.icon;
        state.temperature = view.temperature;
        state.condition = view.condition;
        state.ownerThreadId = GetCurrentThreadId();
        state.mounted = true;
        return TaskbarMountStatus::Mounted;
    } catch (...) {
        // If append succeeded but state publication failed, the button is
        // still discoverable by its exact object identity in the root grid.
        bool rollbackComplete = true;
        try {
            if (profile.rootGrid && button) {
                const auto removal = RemoveExactChildIfPresent(
                    profile.rootGrid, button);
                rollbackComplete = removal != ExactChildRemoval::Failed;
            }
        } catch (...) {
            rollbackComplete = false;
        }
        return rollbackComplete
            ? TaskbarMountStatus::AppendFailed
            : TaskbarMountStatus::RollbackFailed;
    }
}

TaskbarMountStatus UpdateWeatherButton(
    const WeatherViewModel& model,
    TaskbarWeatherMountState& state) noexcept {
    if (!state.mounted || !IsOnOwnerThread(state)) {
        return state.mounted
            ? TaskbarMountStatus::WrongOwnerThread
            : TaskbarMountStatus::InvalidState;
    }
    try {
        const auto rootGrid = state.rootGrid.get();
        const auto button = state.button.get();
        const WeatherXamlView view{
            state.viewRoot.get(),
            state.icon.get(),
            state.temperature.get(),
            state.condition.get()};
        if (!rootGrid || !button || !ValidateWeatherXamlView(view) ||
            !IsOwnedButton(rootGrid, button, view.root)) {
            return TaskbarMountStatus::InvalidState;
        }
        return UpdateWeatherXamlView(view, model)
            ? TaskbarMountStatus::Updated
            : TaskbarMountStatus::InvalidState;
    } catch (...) {
        return TaskbarMountStatus::InvalidState;
    }
}

TaskbarMountStatus RestoreWeatherButton(
    TaskbarWeatherMountState& state) noexcept {
    if (!state.mounted) {
        return TaskbarMountStatus::Restored;
    }
    if (state.ownerThreadId == 0 ||
        state.ownerThreadId != GetCurrentThreadId()) {
        return TaskbarMountStatus::WrongOwnerThread;
    }

    const auto rootGrid = state.rootGrid.get();
    const auto button = state.button.get();
    if (!rootGrid || !button) {
        state = {};
        return TaskbarMountStatus::Restored;
    }
    if (!IsOnOwnerThread(state)) {
        return TaskbarMountStatus::WrongOwnerThread;
    }
    // The saved COM identity is the ownership token. Name, automation, and
    // Content are intentionally not required for restore because Explorer or
    // an accessibility/theme component may have changed those mutable values.
    // Update remains strict and stops before touching a changed element.
    switch (RemoveExactChildIfPresent(rootGrid, button)) {
        case ExactChildRemoval::Removed:
            state = {};
            return TaskbarMountStatus::Restored;
        case ExactChildRemoval::Absent:
            // An unloaded/rebuilt RootGrid has already discarded our child.
            // A loaded grid with an absent child is treated as an external
            // mutation and retained for an explicit retry.
            try {
                if (!rootGrid.IsLoaded()) {
                    state = {};
                    return TaskbarMountStatus::Restored;
                }
            } catch (...) {
            }
            return TaskbarMountStatus::RestoreFailed;
        case ExactChildRemoval::Failed:
            return TaskbarMountStatus::RestoreFailed;
    }

    return TaskbarMountStatus::RestoreFailed;
}

const wchar_t* TaskbarMountStatusName(
    TaskbarMountStatus status) noexcept {
    switch (status) {
        case TaskbarMountStatus::Mounted:
            return L"mounted";
        case TaskbarMountStatus::AlreadyMounted:
            return L"already-mounted";
        case TaskbarMountStatus::Updated:
            return L"updated";
        case TaskbarMountStatus::Restored:
            return L"restored";
        case TaskbarMountStatus::TreeNotReady:
            return L"tree-not-ready";
        case TaskbarMountStatus::StructureNotAllowlisted:
            return L"structure-not-allowlisted";
        case TaskbarMountStatus::LeftSlotUnavailable:
            return L"left-slot-unavailable";
        case TaskbarMountStatus::WrongOwnerThread:
            return L"wrong-owner-thread";
        case TaskbarMountStatus::InvalidState:
            return L"invalid-state";
        case TaskbarMountStatus::ViewCreationFailed:
            return L"view-creation-failed";
        case TaskbarMountStatus::AppendFailed:
            return L"append-failed";
        case TaskbarMountStatus::RestoreFailed:
            return L"restore-failed";
        case TaskbarMountStatus::RollbackFailed:
            return L"rollback-failed";
    }
    return L"unknown";
}

}  // namespace taskflyout::taskbar
