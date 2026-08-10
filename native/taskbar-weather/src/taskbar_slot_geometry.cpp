#include "taskbar_slot_geometry.h"

#include <algorithm>
#include <cmath>

namespace taskflyout::taskbar {
namespace {

constexpr double kMinimumFrameWidthDips = 64.0;
constexpr double kMinimumFrameHeightDips = 24.0;
constexpr double kMaximumFrameWidthDips = 32768.0;
constexpr double kMaximumFrameHeightDips = 256.0;
constexpr double kMinimumSlotWidthDips = 112.0;
constexpr double kMinimumSlotHeightDips = 24.0;
constexpr double kMaximumCoordinateDips = 65536.0;
constexpr double kMaximumGapDips = 4096.0;
constexpr double kBoundsToleranceDips = 0.01;

bool IsFiniteInRange(
    const double value,
    const double minimum,
    const double maximum) noexcept {
    return std::isfinite(value) && value >= minimum && value <= maximum;
}

bool IsFiniteNonNegative(const double value, const double maximum) noexcept {
    return IsFiniteInRange(value, 0.0, maximum);
}

bool AddFiniteBounded(
    const double left,
    const double right,
    double& result) noexcept {
    result = left + right;
    return std::isfinite(result) &&
           std::abs(result) <= kMaximumCoordinateDips;
}

bool IsValidBlocker(const TaskbarSlotRectDips& blocker) noexcept {
    if (!IsFiniteInRange(
            blocker.x,
            -kMaximumCoordinateDips,
            kMaximumCoordinateDips) ||
        !IsFiniteInRange(
            blocker.y,
            -kMaximumCoordinateDips,
            kMaximumCoordinateDips) ||
        !IsFiniteInRange(
            blocker.width,
            kBoundsToleranceDips,
            kMaximumCoordinateDips) ||
        !IsFiniteInRange(
            blocker.height,
            kBoundsToleranceDips,
            kMaximumCoordinateDips)) {
        return false;
    }

    double right = 0.0;
    double bottom = 0.0;
    return AddFiniteBounded(blocker.x, blocker.width, right) &&
           AddFiniteBounded(blocker.y, blocker.height, bottom);
}

bool IsValidCandidate(const TaskbarSlotRectDips& candidate) noexcept {
    if (!IsFiniteNonNegative(candidate.x, kMaximumCoordinateDips) ||
        !IsFiniteNonNegative(candidate.y, kMaximumCoordinateDips) ||
        !IsFiniteInRange(
            candidate.width,
            kMinimumSlotWidthDips,
            kMaximumCoordinateDips) ||
        !IsFiniteInRange(
            candidate.height,
            kMinimumSlotHeightDips,
            kMaximumCoordinateDips)) {
        return false;
    }

    double right = 0.0;
    double bottom = 0.0;
    return AddFiniteBounded(candidate.x, candidate.width, right) &&
           AddFiniteBounded(candidate.y, candidate.height, bottom);
}

bool Intersects(
    const TaskbarSlotRectDips& first,
    const TaskbarSlotRectDips& second) noexcept {
    double firstRight = 0.0;
    double firstBottom = 0.0;
    double secondRight = 0.0;
    double secondBottom = 0.0;
    if (!AddFiniteBounded(first.x, first.width, firstRight) ||
        !AddFiniteBounded(first.y, first.height, firstBottom) ||
        !AddFiniteBounded(second.x, second.width, secondRight) ||
        !AddFiniteBounded(second.y, second.height, secondBottom)) {
        return true;
    }

    const double left = std::max(first.x, second.x);
    const double top = std::max(first.y, second.y);
    const double right = std::min(firstRight, secondRight);
    const double bottom = std::min(firstBottom, secondBottom);
    return right > left && bottom > top;
}

}  // namespace

TaskbarSlotGeometryResult EvaluateTaskbarSlotGeometry(
    const TaskbarSlotGeometryInput& input) noexcept {
    TaskbarSlotGeometryResult result;

    // A caller must establish that the visual-tree shape and blocker snapshot
    // belong to the same allowlisted taskbar build. No geometry is trusted
    // before those owner-thread observations are available.
    if (!input.structureKnown || !input.blockersKnown) {
        result.status = TaskbarSlotGeometryStatus::UnknownStructure;
        return result;
    }

    if (input.interactiveBlockers.size() > kMaxTaskbarSlotBlockers) {
        result.status = TaskbarSlotGeometryStatus::BlockerListTooLarge;
        return result;
    }

    if (!IsFiniteInRange(
            input.frameWidthDips,
            kMinimumFrameWidthDips,
            kMaximumFrameWidthDips) ||
        !IsFiniteInRange(
            input.frameHeightDips,
            kMinimumFrameHeightDips,
            kMaximumFrameHeightDips)) {
        result.status = TaskbarSlotGeometryStatus::FrameGeometryInvalid;
        return result;
    }

    if (!IsFiniteInRange(
            input.rootGridWidthDips,
            kMinimumFrameWidthDips,
            kMaximumFrameWidthDips) ||
        !IsFiniteInRange(
            input.rootGridHeightDips,
            kMinimumFrameHeightDips,
            kMaximumFrameHeightDips)) {
        result.status = TaskbarSlotGeometryStatus::RootGridGeometryInvalid;
        return result;
    }

    if (input.rootGridWidthDips >
            input.frameWidthDips + kBoundsToleranceDips ||
        input.rootGridHeightDips >
            input.frameHeightDips + kBoundsToleranceDips) {
        result.status = TaskbarSlotGeometryStatus::RootGridExceedsFrame;
        return result;
    }

    if (!IsFiniteInRange(
            input.minimumWidthDips,
            kMinimumSlotWidthDips,
            kMaximumCoordinateDips) ||
        !IsFiniteInRange(
            input.minimumHeightDips,
            kMinimumSlotHeightDips,
            kMaximumFrameHeightDips) ||
        !IsFiniteInRange(
            input.desiredWidthDips,
            input.minimumWidthDips,
            kMaximumCoordinateDips) ||
        !IsFiniteInRange(
            input.desiredHeightDips,
            input.minimumHeightDips,
            kMaximumFrameHeightDips)) {
        result.status = TaskbarSlotGeometryStatus::CandidateSizeInvalid;
        return result;
    }

    if (!IsFiniteInRange(input.gapDips, 0.0, kMaximumGapDips)) {
        result.status = TaskbarSlotGeometryStatus::GapInvalid;
        return result;
    }

    if (input.desiredWidthDips > input.rootGridWidthDips ||
        input.desiredHeightDips > input.rootGridHeightDips) {
        result.status = TaskbarSlotGeometryStatus::CandidateOutOfBounds;
        return result;
    }

    result.candidate = TaskbarSlotRectDips{
        0.0,
        (input.rootGridHeightDips - input.desiredHeightDips) / 2.0,
        input.desiredWidthDips,
        input.desiredHeightDips};
    if (!IsValidCandidate(result.candidate)) {
        result.status = TaskbarSlotGeometryStatus::CandidateSizeInvalid;
        result.candidate = {};
        return result;
    }

    const double expandedWidth =
        input.desiredWidthDips + (input.gapDips * 2.0);
    const double expandedHeight =
        input.desiredHeightDips + (input.gapDips * 2.0);
    result.expandedCandidate = TaskbarSlotRectDips{
        -input.gapDips,
        result.candidate.y - input.gapDips,
        expandedWidth,
        expandedHeight};
    if (!std::isfinite(expandedWidth) ||
        !std::isfinite(expandedHeight) ||
        !std::isfinite(result.expandedCandidate.x) ||
        !std::isfinite(result.expandedCandidate.y) ||
        !IsFiniteInRange(
            std::abs(result.expandedCandidate.x),
            0.0,
            kMaximumCoordinateDips) ||
        !IsFiniteInRange(
            std::abs(result.expandedCandidate.y),
            0.0,
            kMaximumCoordinateDips) ||
        !IsFiniteInRange(
            expandedWidth,
            kBoundsToleranceDips,
            kMaximumCoordinateDips) ||
        !IsFiniteInRange(
            expandedHeight,
            kBoundsToleranceDips,
            kMaximumCoordinateDips)) {
        result.status = TaskbarSlotGeometryStatus::GapOverflow;
        result.expandedCandidate = {};
        return result;
    }

    for (const auto& blocker : input.interactiveBlockers) {
        if (!IsValidBlocker(blocker)) {
            result.status = TaskbarSlotGeometryStatus::BlockerGeometryInvalid;
            result.candidate = {};
            result.expandedCandidate = {};
            return result;
        }

        ++result.blockersConsidered;
        if (Intersects(result.expandedCandidate, blocker)) {
            ++result.blockersIntersecting;
        }
    }

    result.status = result.blockersIntersecting == 0
        ? TaskbarSlotGeometryStatus::CandidateAvailable
        : TaskbarSlotGeometryStatus::CandidateConflicted;
    return result;
}

const wchar_t* TaskbarSlotGeometryStatusName(
    const TaskbarSlotGeometryStatus status) noexcept {
    switch (status) {
        case TaskbarSlotGeometryStatus::CandidateAvailable:
            return L"candidate-available";
        case TaskbarSlotGeometryStatus::CandidateConflicted:
            return L"candidate-conflicted";
        case TaskbarSlotGeometryStatus::UnknownStructure:
            return L"unknown-structure";
        case TaskbarSlotGeometryStatus::FrameGeometryInvalid:
            return L"frame-geometry-invalid";
        case TaskbarSlotGeometryStatus::RootGridGeometryInvalid:
            return L"root-grid-geometry-invalid";
        case TaskbarSlotGeometryStatus::RootGridExceedsFrame:
            return L"root-grid-exceeds-frame";
        case TaskbarSlotGeometryStatus::CandidateSizeInvalid:
            return L"candidate-size-invalid";
        case TaskbarSlotGeometryStatus::CandidateOutOfBounds:
            return L"candidate-out-of-bounds";
        case TaskbarSlotGeometryStatus::GapInvalid:
            return L"gap-invalid";
        case TaskbarSlotGeometryStatus::GapOverflow:
            return L"gap-overflow";
        case TaskbarSlotGeometryStatus::BlockerListTooLarge:
            return L"blocker-list-too-large";
        case TaskbarSlotGeometryStatus::BlockerGeometryInvalid:
            return L"blocker-geometry-invalid";
    }
    return L"unknown";
}

}  // namespace taskflyout::taskbar
