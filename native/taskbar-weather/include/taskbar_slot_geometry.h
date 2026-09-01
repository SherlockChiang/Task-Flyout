#pragma once

#include <cstddef>
#include <cstdint>
#include <span>

namespace taskflyout::taskbar {

// All coordinates are device-independent pixels in the RootGrid's local
// coordinate space. The strategy deliberately does not query XAML, so the
// caller must prove that the dimensions and blocker snapshot came from the
// same, current taskbar tree before passing them here.
struct TaskbarSlotRectDips {
    double x = 0.0;
    double y = 0.0;
    double width = 0.0;
    double height = 0.0;
};

enum class TaskbarSlotGeometryStatus : std::uint32_t {
    CandidateAvailable = 0,
    CandidateConflicted = 1,
    UnknownStructure = 2,
    FrameGeometryInvalid = 3,
    RootGridGeometryInvalid = 4,
    RootGridExceedsFrame = 5,
    CandidateSizeInvalid = 6,
    CandidateOutOfBounds = 7,
    GapInvalid = 8,
    GapOverflow = 9,
    BlockerListTooLarge = 10,
    BlockerGeometryInvalid = 11,
};

// The strategy has no permissive defaults: both structureKnown and
// blockersKnown must be set by a build-specific, owner-thread probe. An empty
// blocker span is valid only when blockersKnown is true.
struct TaskbarSlotGeometryInput {
    double frameWidthDips = 0.0;
    double frameHeightDips = 0.0;
    double rootGridWidthDips = 0.0;
    double rootGridHeightDips = 0.0;
    double desiredWidthDips = 220.0;
    double desiredHeightDips = 40.0;
    double minimumWidthDips = 112.0;
    double minimumHeightDips = 24.0;
    double gapDips = 4.0;
    bool structureKnown = false;
    bool blockersKnown = false;
    std::span<const TaskbarSlotRectDips> interactiveBlockers{};
};

struct TaskbarSlotGeometryResult {
    TaskbarSlotGeometryStatus status =
        TaskbarSlotGeometryStatus::UnknownStructure;
    // The unexpanded button rectangle. It is populated only after all size
    // and bounds checks pass.
    TaskbarSlotRectDips candidate{};
    // The candidate expanded by gapDips on all sides, clipped to no boundary.
    // This is the rectangle used for conservative blocker collision checks.
    TaskbarSlotRectDips expandedCandidate{};
    std::uint32_t blockersConsidered = 0;
    std::uint32_t blockersIntersecting = 0;
};

inline constexpr std::size_t kMaxTaskbarSlotBlockers = 64;

TaskbarSlotGeometryResult EvaluateTaskbarSlotGeometry(
    const TaskbarSlotGeometryInput& input) noexcept;

const wchar_t* TaskbarSlotGeometryStatusName(
    TaskbarSlotGeometryStatus status) noexcept;

}  // namespace taskflyout::taskbar
