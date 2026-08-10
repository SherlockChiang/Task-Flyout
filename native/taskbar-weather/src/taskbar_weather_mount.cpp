#include "taskbar_weather_mount.h"

#include <Windows.h>

#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.UI.Core.h>
#include <winrt/Windows.UI.Xaml.Automation.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Controls.Primitives.h>

namespace taskflyout::taskbar {
namespace {

using winrt::Windows::UI::Xaml::Controls::Button;
using winrt::Windows::UI::Xaml::Controls::Grid;
using winrt::Windows::UI::Xaml::Controls::Primitives::IButtonBase;
using winrt::Windows::UI::Xaml::HorizontalAlignment;
using winrt::Windows::UI::Xaml::Thickness;
using winrt::Windows::UI::Xaml::VerticalAlignment;

constexpr double kButtonWidth = 220.0;
constexpr double kButtonMinWidth = 112.0;
constexpr double kButtonHeight = 40.0;
constexpr std::uint32_t kMaxRootGridDefinitions = 16;

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
    const WeatherXamlView& view,
    const TaskbarSlotRectDips& candidate,
    const WeatherButtonActivationCallback activationCallback,
    winrt::event_token& clickToken,
    bool& clickAttached) {
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
    button.Width(candidate.width);
    button.MinWidth(kButtonMinWidth);
    button.MaxWidth(candidate.width);
    button.Height(candidate.height);
    button.Margin(Thickness{
        candidate.x,
        candidate.y,
        0.0,
        0.0});
    button.HorizontalAlignment(HorizontalAlignment::Left);
    button.VerticalAlignment(VerticalAlignment::Top);
    button.HorizontalContentAlignment(HorizontalAlignment::Stretch);
    button.VerticalContentAlignment(VerticalAlignment::Center);
    if (activationCallback) {
        clickToken = button.Click(
            [activationCallback](
                const winrt::Windows::Foundation::IInspectable&,
                const winrt::Windows::UI::Xaml::RoutedEventArgs&) noexcept {
                activationCallback();
            });
        clickAttached = true;
    }
    return button;
}

bool DetachWeatherButtonClick(
    const Button& button,
    winrt::event_token& clickToken,
    bool& clickAttached) noexcept {
    if (!clickAttached) {
        return true;
    }
    try {
        if (!button) {
            // The event source has already been destroyed. There is no live
            // handler left to revoke, so dropping the token is safe.
            clickAttached = false;
            clickToken = {};
            return true;
        }
        const auto buttonBase = button.as<IButtonBase>();
        auto* buttonAbi = reinterpret_cast<winrt::impl::abi_t<IButtonBase>*>(
            winrt::get_abi(buttonBase));
        if (!buttonAbi || FAILED(static_cast<HRESULT>(
                buttonAbi->remove_Click(clickToken)))) {
            return false;
        }
        clickAttached = false;
        clickToken = {};
        return true;
    } catch (...) {
        return false;
    }
}

}  // namespace

TaskbarMountStatus EvaluateTaskbarMountGate(
    const TaskbarMountGateInput& input) noexcept {
    if (input.treeStatus != TaskbarTreeProbeStatus::LandmarksMatched) {
        return TaskbarMountStatus::TreeNotReady;
    }
    if (input.slotStatus == TaskbarSlotGeometryStatus::UnknownStructure) {
        return TaskbarMountStatus::StructureNotAllowlisted;
    }
    if (input.slotStatus == TaskbarSlotGeometryStatus::CandidateConflicted) {
        return TaskbarMountStatus::LeftSlotUnavailable;
    }
    if (input.slotStatus != TaskbarSlotGeometryStatus::CandidateAvailable) {
        return TaskbarMountStatus::SlotGeometryInvalid;
    }
    if (!input.ownerThread) {
        return TaskbarMountStatus::WrongOwnerThread;
    }
    return TaskbarMountStatus::Mounted;
}

TaskbarMountCleanupObligations EvaluateTaskbarMountRollback(
    const bool clickAttached,
    const bool childRemovalFailed) noexcept {
    return TaskbarMountCleanupObligations{
        clickAttached,
        childRemovalFailed};
}

bool HasTaskbarMountCleanupObligations(
    const TaskbarMountCleanupObligations& obligations) noexcept {
    return obligations.clickAttached || obligations.childPresent;
}

TaskbarMountStatus MountWeatherButton(
    const TaskbarTreeProfile& profile,
    const WeatherViewModel& model,
    const TaskbarSlotProbeResult& slotProbe,
    const WeatherButtonActivationCallback activationCallback,
    TaskbarWeatherMountState& state) noexcept {
    if (!activationCallback) {
        return TaskbarMountStatus::InvalidState;
    }
    if (profile.status != TaskbarTreeProbeStatus::LandmarksMatched ||
        !profile.rootGrid || !profile.frame) {
        return TaskbarMountStatus::TreeNotReady;
    }
    if (profile.ownerThreadId == 0 ||
        profile.ownerThreadId != GetCurrentThreadId()) {
        return TaskbarMountStatus::WrongOwnerThread;
    }
    try {
        if (!profile.frame.Dispatcher() ||
            !profile.frame.Dispatcher().HasThreadAccess()) {
            return TaskbarMountStatus::WrongOwnerThread;
        }
    } catch (...) {
        return TaskbarMountStatus::WrongOwnerThread;
    }

    if (slotProbe.status != TaskbarSlotProbeStatus::SnapshotReady ||
        slotProbe.structureStatus != TaskbarSlotStructureStatus::Matched ||
        !slotProbe.blockersKnown ||
        slotProbe.blockerCount > slotProbe.blockerStorage.size()) {
        return TaskbarMountStatus::StructureNotAllowlisted;
    }
    if (slotProbe.geometry.status ==
        TaskbarSlotGeometryStatus::CandidateConflicted) {
        return TaskbarMountStatus::LeftSlotUnavailable;
    }
    if (slotProbe.geometry.status !=
        TaskbarSlotGeometryStatus::CandidateAvailable) {
        return TaskbarMountStatus::SlotGeometryInvalid;
    }

    TaskbarSlotGeometryInput verifiedGeometry =
        slotProbe.MakeGeometryInput();
    TaskbarSlotGeometryResult geometry;
    try {
        if (!profile.signature.frameGeometryValid ||
            !profile.signature.rootGridGeometryValid) {
            return TaskbarMountStatus::TreeNotReady;
        }
        // Dimensions come from the current XAML tree, never from a stale
        // probe snapshot. The blocker span and structure proof must come from
        // the owner-thread scanner for this exact tree.
        verifiedGeometry.frameWidthDips = profile.frame.ActualWidth();
        verifiedGeometry.frameHeightDips = profile.frame.ActualHeight();
        verifiedGeometry.rootGridWidthDips = profile.rootGrid.ActualWidth();
        verifiedGeometry.rootGridHeightDips = profile.rootGrid.ActualHeight();
        verifiedGeometry.desiredWidthDips = kButtonWidth;
        verifiedGeometry.desiredHeightDips = kButtonHeight;
        verifiedGeometry.minimumWidthDips = kButtonMinWidth;
        verifiedGeometry.minimumHeightDips = 24.0;
        verifiedGeometry.gapDips = 4.0;
        geometry = EvaluateTaskbarSlotGeometry(verifiedGeometry);
    } catch (...) {
        return TaskbarMountStatus::SlotGeometryInvalid;
    }
    const TaskbarMountGateInput gate{
        profile.status,
        geometry.status,
        profile.ownerThreadId != 0 &&
            profile.ownerThreadId == GetCurrentThreadId()};
    const TaskbarMountStatus gateStatus =
        EvaluateTaskbarMountGate(gate);
    if (gateStatus != TaskbarMountStatus::Mounted) {
        return gateStatus;
    }
    if (state.mounted) {
        Grid stateRoot{nullptr};
        Button stateButton{nullptr};
        try {
            stateRoot = state.rootGrid.get();
            stateButton = state.button.get();
        } catch (...) {
            return TaskbarMountStatus::InvalidState;
        }
        if (!stateRoot || !stateButton) {
            const TaskbarMountStatus restoreStatus =
                RestoreWeatherButton(state);
            if (restoreStatus != TaskbarMountStatus::Restored) {
                return restoreStatus;
            }
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
    winrt::event_token clickToken{};
    bool clickAttached = false;
    try {
        view = CreateWeatherXamlView(model);
    } catch (...) {
        return TaskbarMountStatus::ViewCreationFailed;
    }

    try {
        button = CreateWeatherButton(
            view,
            geometry.candidate,
            activationCallback,
            clickToken,
            clickAttached);
        const std::uint32_t rowCount =
            profile.rootGrid.RowDefinitions().Size();
        const std::uint32_t columnCount =
            profile.rootGrid.ColumnDefinitions().Size();
        if (rowCount > kMaxRootGridDefinitions ||
            columnCount > kMaxRootGridDefinitions) {
            return TaskbarMountStatus::SlotGeometryInvalid;
        }
        Grid::SetRow(button, 0);
        Grid::SetColumn(button, 0);
        Grid::SetRowSpan(button, rowCount == 0 ? 1 : rowCount);
        Grid::SetColumnSpan(button, columnCount == 0 ? 1 : columnCount);
        // Publish all recovery metadata before append. If append succeeds but
        // a later operation fails, quarantine/stop can still identify the
        // exact Button and retry cleanup on this owner thread.
        state.rootGrid = profile.rootGrid;
        state.button = button;
        state.viewRoot = view.root;
        state.icon = view.icon;
        state.temperature = view.temperature;
        state.condition = view.condition;
        state.clickToken = clickToken;
        state.ownerThreadId = GetCurrentThreadId();
        state.clickAttached = clickAttached;
        state.childPresent = false;
        state.mounted = false;

        profile.rootGrid.Children().Append(button);
        state.childPresent = true;
        state.mounted = true;
        return TaskbarMountStatus::Mounted;
    } catch (...) {
        // If append partially succeeded, the button is still discoverable by
        // its exact object identity in the root grid.
        DetachWeatherButtonClick(
            button,
            clickToken,
            clickAttached);
        bool childRemovalFailed = false;
        try {
            if (profile.rootGrid && button) {
                const auto removal = RemoveExactChildIfPresent(
                    profile.rootGrid, button);
                childRemovalFailed =
                    removal == ExactChildRemoval::Failed;
            }
        } catch (...) {
            childRemovalFailed = true;
        }
        const TaskbarMountCleanupObligations obligations =
            EvaluateTaskbarMountRollback(
                clickAttached,
                childRemovalFailed);
        const bool rollbackComplete =
            !HasTaskbarMountCleanupObligations(obligations);
        if (rollbackComplete) {
            state = {};
        } else {
            // Token revocation and exact-child removal are independent cleanup
            // obligations. Preserve only the obligations which remain so a
            // later restore cannot mistake an already removed child for an
            // external mutation.
            state.clickToken = clickToken;
            state.clickAttached = obligations.clickAttached;
            state.childPresent = obligations.childPresent;
            state.mounted = true;
        }
        return rollbackComplete
            ? TaskbarMountStatus::AppendFailed
            : TaskbarMountStatus::RollbackFailed;
    }
}

TaskbarMountStatus UpdateWeatherButton(
    const WeatherViewModel& model,
    TaskbarWeatherMountState& state) noexcept {
    if (!state.mounted || !state.childPresent) {
        return TaskbarMountStatus::InvalidState;
    }
    if (!IsOnOwnerThread(state)) {
        return TaskbarMountStatus::WrongOwnerThread;
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

    Grid rootGrid{nullptr};
    Button button{nullptr};
    try {
        rootGrid = state.rootGrid.get();
        button = state.button.get();
    } catch (...) {
        return TaskbarMountStatus::RestoreFailed;
    }
    if (!button) {
        // A dead event source cannot retain the callback.
        state = {};
        return TaskbarMountStatus::Restored;
    }
    if (!IsOnOwnerThread(state)) {
        return TaskbarMountStatus::WrongOwnerThread;
    }
    if (!DetachWeatherButtonClick(
            button,
            state.clickToken,
            state.clickAttached)) {
        return TaskbarMountStatus::RestoreFailed;
    }
    if (!state.childPresent) {
        state = {};
        return TaskbarMountStatus::Restored;
    }
    if (!rootGrid) {
        state = {};
        return TaskbarMountStatus::Restored;
    }
    // The saved COM identity is the ownership token. Name, automation, and
    // Content are intentionally not required for restore because Explorer or
    // an accessibility/theme component may have changed those mutable values.
    // Update remains strict and stops before touching a changed element.
    switch (RemoveExactChildIfPresent(rootGrid, button)) {
        case ExactChildRemoval::Removed:
            state.childPresent = false;
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
        case TaskbarMountStatus::SlotGeometryInvalid:
            return L"slot-geometry-invalid";
    }
    return L"unknown";
}

}  // namespace taskflyout::taskbar
