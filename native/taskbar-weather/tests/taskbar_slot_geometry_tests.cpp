#include "taskbar_slot_geometry.h"

#include <array>
#include <cmath>
#include <cstdio>
#include <limits>
#include <string_view>

namespace {

bool Expect(bool condition, const wchar_t* message) {
    if (condition) {
        return true;
    }
    fwprintf(stderr, L"FAILED: %ls\n", message);
    return false;
}

bool ExpectNear(
    const double actual,
    const double expected,
    const wchar_t* message) {
    return Expect(std::abs(actual - expected) < 0.0001, message);
}

taskflyout::taskbar::TaskbarSlotGeometryInput ValidInput() {
    taskflyout::taskbar::TaskbarSlotGeometryInput input;
    input.frameWidthDips = 1920.0;
    input.frameHeightDips = 48.0;
    input.rootGridWidthDips = 1920.0;
    input.rootGridHeightDips = 48.0;
    input.structureKnown = true;
    input.blockersKnown = true;
    return input;
}

}  // namespace

int wmain() {
    using namespace taskflyout::taskbar;

    bool passed = true;

    TaskbarSlotGeometryInput input;
    auto result = EvaluateTaskbarSlotGeometry(input);
    passed &= Expect(
        result.status == TaskbarSlotGeometryStatus::UnknownStructure,
        L"the default input must fail closed as an unknown structure");

    input = ValidInput();
    input.blockersKnown = false;
    result = EvaluateTaskbarSlotGeometry(input);
    passed &= Expect(
        result.status == TaskbarSlotGeometryStatus::UnknownStructure,
        L"a missing blocker snapshot must fail closed");

    input = ValidInput();
    result = EvaluateTaskbarSlotGeometry(input);
    passed &= Expect(
        result.status == TaskbarSlotGeometryStatus::CandidateAvailable,
        L"a known empty left slot should produce a candidate");
    passed &= ExpectNear(result.candidate.x, 0.0, L"candidate x");
    passed &= ExpectNear(result.candidate.y, 4.0, L"candidate y");
    passed &= ExpectNear(result.candidate.width, 220.0, L"candidate width");
    passed &= ExpectNear(result.candidate.height, 40.0, L"candidate height");
    passed &= ExpectNear(
        result.expandedCandidate.x,
        -4.0,
        L"expanded candidate x");
    passed &= ExpectNear(
        result.expandedCandidate.y,
        0.0,
        L"expanded candidate y");
    passed &= ExpectNear(
        result.expandedCandidate.width,
        228.0,
        L"expanded candidate width");
    passed &= ExpectNear(
        result.expandedCandidate.height,
        48.0,
        L"expanded candidate height");

    const std::array<TaskbarSlotRectDips, 1> outsideBlockers{{
        {300.0, 0.0, 40.0, 48.0},
    }};
    input.interactiveBlockers = outsideBlockers;
    result = EvaluateTaskbarSlotGeometry(input);
    passed &= Expect(
        result.status == TaskbarSlotGeometryStatus::CandidateAvailable,
        L"a blocker beyond the gap must not conflict");
    passed &= Expect(
        result.blockersConsidered == 1 &&
            result.blockersIntersecting == 0,
        L"a nonintersecting blocker should be counted");

    const std::array<TaskbarSlotRectDips, 1> touchingBlockers{{
        {224.0, 0.0, 10.0, 48.0},
    }};
    input.interactiveBlockers = touchingBlockers;
    result = EvaluateTaskbarSlotGeometry(input);
    passed &= Expect(
        result.status == TaskbarSlotGeometryStatus::CandidateAvailable,
        L"edge contact with the expanded slot has no overlapping area");

    const std::array<TaskbarSlotRectDips, 1> gapBlockers{{
        {222.0, 0.0, 10.0, 48.0},
    }};
    input.interactiveBlockers = gapBlockers;
    result = EvaluateTaskbarSlotGeometry(input);
    passed &= Expect(
        result.status == TaskbarSlotGeometryStatus::CandidateConflicted,
        L"the configured safety gap must turn a near blocker into a conflict");
    passed &= Expect(
        result.blockersIntersecting == 1,
        L"the intersecting blocker should be reported");

    const std::array<TaskbarSlotRectDips, 2> overlappingBlockers{{
        {10.0, 8.0, 20.0, 20.0},
        {50.0, 4.0, 20.0, 40.0},
    }};
    input.interactiveBlockers = overlappingBlockers;
    result = EvaluateTaskbarSlotGeometry(input);
    passed &= Expect(
        result.status == TaskbarSlotGeometryStatus::CandidateConflicted &&
            result.blockersConsidered == 2 &&
            result.blockersIntersecting == 2,
        L"all bounded conflicts should be counted deterministically");

    input = ValidInput();
    input.frameWidthDips = std::numeric_limits<double>::quiet_NaN();
    result = EvaluateTaskbarSlotGeometry(input);
    passed &= Expect(
        result.status == TaskbarSlotGeometryStatus::FrameGeometryInvalid,
        L"NaN frame dimensions must be rejected");

    input = ValidInput();
    input.rootGridHeightDips = std::numeric_limits<double>::infinity();
    result = EvaluateTaskbarSlotGeometry(input);
    passed &= Expect(
        result.status == TaskbarSlotGeometryStatus::RootGridGeometryInvalid,
        L"infinite root dimensions must be rejected");

    input = ValidInput();
    input.rootGridWidthDips = 1920.02;
    result = EvaluateTaskbarSlotGeometry(input);
    passed &= Expect(
        result.status == TaskbarSlotGeometryStatus::RootGridExceedsFrame,
        L"a root outside the frame tolerance must be rejected");

    input = ValidInput();
    input.minimumWidthDips = 111.0;
    input.desiredWidthDips = 111.0;
    result = EvaluateTaskbarSlotGeometry(input);
    passed &= Expect(
        result.status == TaskbarSlotGeometryStatus::CandidateSizeInvalid,
        L"the policy minimum width cannot be bypassed by the caller");

    input = ValidInput();
    input.rootGridWidthDips = 200.0;
    input.frameWidthDips = 200.0;
    result = EvaluateTaskbarSlotGeometry(input);
    passed &= Expect(
        result.status == TaskbarSlotGeometryStatus::CandidateOutOfBounds,
        L"an undersized root must not produce a clipped candidate");

    input = ValidInput();
    input.gapDips = -1.0;
    result = EvaluateTaskbarSlotGeometry(input);
    passed &= Expect(
        result.status == TaskbarSlotGeometryStatus::GapInvalid,
        L"negative collision gaps must be rejected");

    input = ValidInput();
    const std::array<TaskbarSlotRectDips, 1> overflowingBlockers{{
        {65530.0, 0.0, 10.0, 10.0},
    }};
    input.interactiveBlockers = overflowingBlockers;
    result = EvaluateTaskbarSlotGeometry(input);
    passed &= Expect(
        result.status == TaskbarSlotGeometryStatus::BlockerGeometryInvalid,
        L"blocker endpoint overflow must fail closed");
    passed &= Expect(
        result.candidate.width == 0.0 &&
            result.expandedCandidate.width == 0.0,
        L"invalid blocker geometry must not expose an actionable candidate");

    input = ValidInput();
    const std::array<
        TaskbarSlotRectDips,
        kMaxTaskbarSlotBlockers + 1> tooManyBlockers{};
    input.interactiveBlockers = tooManyBlockers;
    result = EvaluateTaskbarSlotGeometry(input);
    passed &= Expect(
        result.status == TaskbarSlotGeometryStatus::BlockerListTooLarge,
        L"the blocker walk must remain bounded");

    passed &= Expect(
        std::wstring_view(TaskbarSlotGeometryStatusName(
            TaskbarSlotGeometryStatus::CandidateConflicted)) ==
            L"candidate-conflicted",
        L"slot status should remain diagnosable");

    if (!passed) {
        return 1;
    }
    fputws(L"Taskbar slot geometry policy tests passed.\n", stdout);
    return 0;
}
