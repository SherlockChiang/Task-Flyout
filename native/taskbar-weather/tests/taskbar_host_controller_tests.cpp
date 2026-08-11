#include "taskbar_host_controller.h"

#include <cstdio>

namespace {

bool Expect(bool condition, const wchar_t* message) {
    if (condition) {
        return true;
    }
    fwprintf(stderr, L"FAILED: %ls\n", message);
    return false;
}

taskflyout::taskbar::TaskbarHostFrameDecisionInput ValidDecision() {
    taskflyout::taskbar::TaskbarHostFrameDecisionInput input;
    input.bridgeStatus =
        taskflyout::taskbar::TaskbarFrameBridgeStatus::Resolved;
    input.treeStatus =
        taskflyout::taskbar::TaskbarTreeProbeStatus::LandmarksMatched;
    input.slotProbeStatus =
        taskflyout::taskbar::TaskbarSlotProbeStatus::SnapshotReady;
    input.geometryStatus =
        taskflyout::taskbar::TaskbarSlotGeometryStatus::CandidateAvailable;
    return input;
}

}  // namespace

int wmain() {
    using namespace taskflyout::taskbar;

    bool passed = true;
    auto input = ValidDecision();
    passed &= Expect(
        EvaluateTaskbarHostFrameAction(input) ==
            TaskbarHostFrameAction::MountOrUpdate,
        L"a fully validated free slot should mount or update");

    input = ValidDecision();
    input.geometryStatus =
        TaskbarSlotGeometryStatus::CandidateConflicted;
    passed &= Expect(
        EvaluateTaskbarHostFrameAction(input) ==
            TaskbarHostFrameAction::NoChange,
        L"an unmounted conflict should remain untouched");
    input.mounted = true;
    passed &= Expect(
        EvaluateTaskbarHostFrameAction(input) ==
            TaskbarHostFrameAction::Restore,
        L"a newly conflicted mounted slot should restore");

    input = ValidDecision();
    input.slotProbeStatus =
        TaskbarSlotProbeStatus::RootStructureMismatch;
    input.mounted = true;
    passed &= Expect(
        EvaluateTaskbarHostFrameAction(input) ==
            TaskbarHostFrameAction::Restore,
        L"a changed live tree should remove the owned button");

    input = ValidDecision();
    input.bridgeStatus =
        TaskbarFrameBridgeStatus::InspectableSlotUnreadable;
    input.mounted = true;
    passed &= Expect(
        EvaluateTaskbarHostFrameAction(input) ==
            TaskbarHostFrameAction::NoChange,
        L"an unresolved private pointer cannot identify a lease to restore");

    passed &= Expect(
        IsTaskbarMountObservationFresh(true, 100u, 112u, 12u),
        L"a ready observation remains fresh at the lease boundary");
    passed &= Expect(
        !IsTaskbarMountObservationFresh(true, 100u, 113u, 12u) &&
            !IsTaskbarMountObservationFresh(false, 100u, 101u, 12u) &&
            !IsTaskbarMountObservationFresh(true, 0u, 101u, 12u) &&
            !IsTaskbarMountObservationFresh(true, 102u, 101u, 12u),
        L"stale, lost, missing, and future observations fail closed");
    passed &= Expect(
        EvaluateTaskbarMountReportAction(true, false, true) ==
                TaskbarMountReportAction::PendingLost &&
            EvaluateTaskbarMountReportAction(false, true, false) ==
                TaskbarMountReportAction::Current &&
            EvaluateTaskbarMountReportAction(false, false, true) ==
                TaskbarMountReportAction::Current &&
            EvaluateTaskbarMountReportAction(false, false, false) ==
                TaskbarMountReportAction::None,
        L"a pending lost report must win over a later ready proof");

    if (!passed) {
        return 1;
    }
    fputws(L"Taskbar host controller policy tests passed.\n", stdout);
    return 0;
}
