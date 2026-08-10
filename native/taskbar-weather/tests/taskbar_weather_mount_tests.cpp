#include "taskbar_weather_mount.h"

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

}  // namespace

int wmain() {
    using namespace taskflyout::taskbar;

    bool passed = true;
    TaskbarMountGateInput input;
    input.treeStatus = TaskbarTreeProbeStatus::XamlTreeUnavailable;
    input.slotStatus = TaskbarSlotGeometryStatus::CandidateAvailable;
    input.ownerThread = true;
    passed &= Expect(
        EvaluateTaskbarMountGate(input) ==
            TaskbarMountStatus::TreeNotReady,
        L"unavailable taskbar tree must not be mounted");

    input.treeStatus = TaskbarTreeProbeStatus::LandmarksMatched;
    input.slotStatus = TaskbarSlotGeometryStatus::UnknownStructure;
    passed &= Expect(
        EvaluateTaskbarMountGate(input) ==
            TaskbarMountStatus::StructureNotAllowlisted,
        L"landmarks alone must not permit mutation");

    input.slotStatus = TaskbarSlotGeometryStatus::CandidateConflicted;
    passed &= Expect(
        EvaluateTaskbarMountGate(input) ==
            TaskbarMountStatus::LeftSlotUnavailable,
        L"an occupied left slot must fail closed");

    input.slotStatus = TaskbarSlotGeometryStatus::CandidateAvailable;
    input.ownerThread = false;
    passed &= Expect(
        EvaluateTaskbarMountGate(input) ==
            TaskbarMountStatus::WrongOwnerThread,
        L"cross-thread XAML mutation must fail closed");

    input.ownerThread = true;
    passed &= Expect(
        EvaluateTaskbarMountGate(input) ==
            TaskbarMountStatus::Mounted,
        L"all explicit mount gates should allow the lease");
    input.slotStatus = TaskbarSlotGeometryStatus::GapInvalid;
    passed &= Expect(
        EvaluateTaskbarMountGate(input) ==
            TaskbarMountStatus::SlotGeometryInvalid,
        L"invalid slot geometry must fail closed");
    passed &= Expect(
        TaskbarMountStatusName(TaskbarMountStatus::RollbackFailed) ==
            std::wstring_view(L"rollback-failed"),
        L"rollback failure should remain diagnosable");

    if (!passed) {
        return 1;
    }
    fputws(L"Taskbar weather mount policy tests passed.\n", stdout);
    return 0;
}
