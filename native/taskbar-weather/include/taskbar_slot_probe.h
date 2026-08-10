#pragma once

#include "taskbar_slot_geometry.h"
#include "taskbar_tree_profile.h"

#include <winrt/Windows.UI.Xaml.h>

#include <array>
#include <cstddef>
#include <cstdint>
#include <span>

namespace taskflyout::taskbar {

// The slot probe deliberately accepts only a very small, direct-child
// fingerprint.  The signature is kept independent of XAML so that changes to
// the private taskbar tree can be tested without constructing an Explorer
// apartment in the unit-test process.
enum class TaskbarSlotStructureStatus : std::uint32_t {
    Matched = 0,
    RootChildCountOutOfBounds = 1,
    RootChildCountMismatch = 2,
    UnknownRootChild = 3,
    BackgroundMismatch = 4,
    RepeaterMismatch = 5,
    OwnedChildMismatch = 6,
    IdentityCollision = 7,
    SignatureInconsistent = 8,
};

struct TaskbarSlotStructureSignature {
    bool ownedChildExpected = false;
    bool expectedIdentitiesDistinct = true;
    std::uint32_t rootDirectChildCount = 0;
    std::uint32_t backgroundExactCount = 0;
    std::uint32_t repeaterExactCount = 0;
    std::uint32_t ownedExactCount = 0;
    std::uint32_t unknownDirectChildCount = 0;
};

inline constexpr std::size_t kMaxTaskbarSlotRootChildren = 8;
inline constexpr std::size_t kMaxTaskbarSlotRealizedChildren =
    kMaxTaskbarSlotBlockers;

TaskbarSlotStructureStatus EvaluateTaskbarSlotStructureSignature(
    const TaskbarSlotStructureSignature& signature) noexcept;

const wchar_t* TaskbarSlotStructureStatusName(
    TaskbarSlotStructureStatus status) noexcept;

enum class TaskbarSlotProbeStatus : std::uint32_t {
    SnapshotReady = 0,
    TreeNotReady = 1,
    WrongOwnerThread = 2,
    DispatcherUnavailable = 3,
    DispatcherThreadMismatch = 4,
    RootChildCountOutOfBounds = 5,
    RootStructureMismatch = 6,
    RepeaterChildCountOutOfBounds = 7,
    RepeaterChildNotFrameworkElement = 8,
    RepeaterChildStateUnavailable = 9,
    RepeaterChildGeometryInvalid = 10,
    RepeaterChildTransformUnavailable = 11,
    RepeaterChildTransformInvalid = 12,
    GeometryEvaluationFailed = 13,
    XamlTreeUnavailable = 14,
};

const wchar_t* TaskbarSlotProbeStatusName(
    TaskbarSlotProbeStatus status) noexcept;

struct TaskbarSlotProbeOptions {
    double desiredWidthDips = 220.0;
    double desiredHeightDips = 40.0;
    double minimumWidthDips = 112.0;
    double minimumHeightDips = 24.0;
    double gapDips = 4.0;
};

// This result owns only the bounded blocker snapshot and scalar observations.
// It intentionally does not retain XAML references after ProbeTaskbarSlot
// returns.  The span returned by MakeGeometryInput() points into this result
// and is valid until the result is destroyed or moved.
struct TaskbarSlotProbeResult {
    TaskbarSlotProbeStatus status = TaskbarSlotProbeStatus::XamlTreeUnavailable;
    TaskbarSlotStructureStatus structureStatus =
        TaskbarSlotStructureStatus::SignatureInconsistent;
    TaskbarSlotStructureSignature structure{};
    TaskbarSlotGeometryResult geometry{};
    TaskbarSlotProbeOptions options{};
    std::array<TaskbarSlotRectDips, kMaxTaskbarSlotBlockers>
        blockerStorage{};
    std::uint32_t blockerCount = 0;
    double frameWidthDips = 0.0;
    double frameHeightDips = 0.0;
    double rootGridWidthDips = 0.0;
    double rootGridHeightDips = 0.0;
    bool blockersKnown = false;

    TaskbarSlotGeometryInput MakeGeometryInput() const & noexcept;
    TaskbarSlotGeometryInput MakeGeometryInput() const && = delete;
};

// Probe one current taskbar visual-tree snapshot.  All XAML access, including
// creation and destruction of the temporary projection references, must occur
// on profile.ownerThreadId.  `ownedRootChild` is optional and, when supplied,
// must be the exact object previously appended by this process.
TaskbarSlotProbeResult ProbeTaskbarSlot(
    const TaskbarTreeProfile& profile,
    const TaskbarSlotProbeOptions& options = {},
    const winrt::Windows::UI::Xaml::DependencyObject& ownedRootChild =
        nullptr) noexcept;

}  // namespace taskflyout::taskbar
