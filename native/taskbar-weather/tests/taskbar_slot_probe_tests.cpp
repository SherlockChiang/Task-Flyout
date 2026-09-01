#include "taskbar_slot_probe.h"

#include <cstdio>
#include <string_view>

namespace {

bool Expect(bool condition, const wchar_t* message) {
    if (condition) {
        return true;
    }
    fwprintf(stderr, L"FAILED: %ls\n", message);
    return false;
}

taskflyout::taskbar::TaskbarSlotStructureSignature ValidSignature(
    const bool withOwnedChild = false) {
    taskflyout::taskbar::TaskbarSlotStructureSignature signature;
    signature.ownedChildExpected = withOwnedChild;
    signature.rootDirectChildCount = withOwnedChild ? 3u : 2u;
    signature.backgroundExactCount = 1;
    signature.repeaterExactCount = 1;
    signature.ownedExactCount = withOwnedChild ? 1u : 0u;
    return signature;
}

}  // namespace

int wmain() {
    using namespace taskflyout::taskbar;

    bool passed = true;
    passed &= Expect(
        EvaluateTaskbarSlotStructureSignature(ValidSignature()) ==
            TaskbarSlotStructureStatus::Matched,
        L"the exact two-child taskbar structure should match");
    passed &= Expect(
        EvaluateTaskbarSlotStructureSignature(ValidSignature(true)) ==
            TaskbarSlotStructureStatus::Matched,
        L"the exact structure with the owned child should match");

    auto mutated = ValidSignature();
    mutated.unknownDirectChildCount = 1;
    passed &= Expect(
        EvaluateTaskbarSlotStructureSignature(mutated) ==
            TaskbarSlotStructureStatus::UnknownRootChild,
        L"an unknown direct child must fail closed");

    mutated = ValidSignature();
    mutated.repeaterExactCount = 2;
    mutated.rootDirectChildCount = 3;
    passed &= Expect(
        EvaluateTaskbarSlotStructureSignature(mutated) ==
            TaskbarSlotStructureStatus::RepeaterMismatch,
        L"a duplicate repeater identity must fail closed");

    mutated = ValidSignature(true);
    mutated.expectedIdentitiesDistinct = false;
    passed &= Expect(
        EvaluateTaskbarSlotStructureSignature(mutated) ==
            TaskbarSlotStructureStatus::IdentityCollision,
        L"the owned child cannot alias the repeater");

    mutated = ValidSignature();
    mutated.rootDirectChildCount =
        static_cast<std::uint32_t>(kMaxTaskbarSlotRootChildren + 1);
    passed &= Expect(
        EvaluateTaskbarSlotStructureSignature(mutated) ==
            TaskbarSlotStructureStatus::RootChildCountOutOfBounds,
        L"the root walk must remain bounded");

    TaskbarSlotProbeResult result;
    result.status = TaskbarSlotProbeStatus::SnapshotReady;
    result.structureStatus = TaskbarSlotStructureStatus::Matched;
    result.blockersKnown = true;
    result.blockerCount = 1;
    result.blockerStorage[0] = {12.0, 2.0, 32.0, 40.0};
    result.frameWidthDips = 1920.0;
    result.frameHeightDips = 48.0;
    result.rootGridWidthDips = 1920.0;
    result.rootGridHeightDips = 48.0;
    const auto input = result.MakeGeometryInput();
    passed &= Expect(
        input.blockersKnown && input.interactiveBlockers.size() == 1 &&
            input.interactiveBlockers.data() == result.blockerStorage.data(),
        L"the geometry span must refer to result-owned blocker storage");
    passed &= Expect(
        EvaluateTaskbarSlotGeometry(input).status ==
            TaskbarSlotGeometryStatus::CandidateConflicted,
        L"the live blocker snapshot should feed the geometry policy");

    result.blockerCount =
        static_cast<std::uint32_t>(result.blockerStorage.size() + 1);
    const auto invalidInput = result.MakeGeometryInput();
    passed &= Expect(
        !invalidInput.blockersKnown &&
            invalidInput.interactiveBlockers.empty() &&
            EvaluateTaskbarSlotGeometry(invalidInput).status ==
                TaskbarSlotGeometryStatus::UnknownStructure,
        L"an invalid owned blocker count must fail without creating a span");

    passed &= Expect(
        std::wstring_view(TaskbarSlotProbeStatusName(
            TaskbarSlotProbeStatus::RepeaterChildTransformInvalid)) ==
            L"repeater-child-transform-invalid",
        L"probe status should remain diagnosable");

    if (!passed) {
        return 1;
    }
    fputws(L"Taskbar slot probe policy tests passed.\n", stdout);
    return 0;
}
