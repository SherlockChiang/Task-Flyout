#pragma once

#include "taskbar_tree_profile.h"
#include "taskbar_slot_geometry.h"
#include "weather_view_model.h"
#include "weather_xaml_view.h"

#include <winrt/Windows.UI.Xaml.Controls.h>

#include <cstdint>

namespace taskflyout::taskbar {

inline constexpr wchar_t kWeatherButtonName[] =
    L"TaskFlyoutNativeWeatherButton";
inline constexpr wchar_t kWeatherButtonAutomationId[] =
    L"TaskFlyoutNativeWeatherButton";

enum class TaskbarMountStatus : std::uint32_t {
    Mounted = 0,
    AlreadyMounted = 1,
    Updated = 2,
    Restored = 3,
    TreeNotReady = 4,
    StructureNotAllowlisted = 5,
    LeftSlotUnavailable = 6,
    WrongOwnerThread = 7,
    InvalidState = 8,
    ViewCreationFailed = 9,
    AppendFailed = 10,
    RestoreFailed = 11,
    RollbackFailed = 12,
    SlotGeometryInvalid = 13,
};

struct TaskbarMountGateInput {
    TaskbarTreeProbeStatus treeStatus =
        TaskbarTreeProbeStatus::XamlTreeUnavailable;
    TaskbarSlotGeometryStatus slotStatus =
        TaskbarSlotGeometryStatus::UnknownStructure;
    bool ownerThread = false;
};

TaskbarMountStatus EvaluateTaskbarMountGate(
    const TaskbarMountGateInput& input) noexcept;

// This state contains weak apartment-affine references. Create, update,
// restore, and destroy it on the same TaskbarFrame XAML owner thread. It is
// intentionally not a worker thread hand-off object and does not pin an old
// TaskbarFrame across visual-tree rebuilds.
struct TaskbarWeatherMountState {
    winrt::weak_ref<winrt::Windows::UI::Xaml::Controls::Grid> rootGrid;
    winrt::weak_ref<winrt::Windows::UI::Xaml::Controls::Button> button;
    winrt::weak_ref<winrt::Windows::UI::Xaml::Controls::Grid> viewRoot;
    winrt::weak_ref<winrt::Windows::UI::Xaml::Controls::TextBlock> icon;
    winrt::weak_ref<winrt::Windows::UI::Xaml::Controls::TextBlock>
        temperature;
    winrt::weak_ref<winrt::Windows::UI::Xaml::Controls::TextBlock> condition;
    std::uint32_t ownerThreadId = 0;
    bool mounted = false;
};

TaskbarMountStatus MountWeatherButton(
    const TaskbarTreeProfile& profile,
    const WeatherViewModel& model,
    const TaskbarSlotGeometryInput& geometryInput,
    TaskbarWeatherMountState& state) noexcept;

TaskbarMountStatus UpdateWeatherButton(
    const WeatherViewModel& model,
    TaskbarWeatherMountState& state) noexcept;

TaskbarMountStatus RestoreWeatherButton(
    TaskbarWeatherMountState& state) noexcept;

const wchar_t* TaskbarMountStatusName(
    TaskbarMountStatus status) noexcept;

}  // namespace taskflyout::taskbar
