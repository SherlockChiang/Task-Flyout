#include "taskbar_host_controller.h"
#include "taskbar_mount_readiness_state.h"

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

    TaskbarMountReadinessState readiness;
    BeginTaskbarMountReadinessSession(readiness, 17u, 100u);
    passed &= Expect(
        RecordTaskbarMountReadinessObservation(readiness, true, 101u),
        L"the first owner-thread ready proof should change session state");
    const TaskbarMountReadinessSnapshot firstReady =
        SnapshotTaskbarMountReadiness(readiness, 101u, 12u);
    passed &= Expect(
        firstReady.controllerNonce == 17u && firstReady.ready &&
            !firstReady.pendingLost,
        L"a fresh ready proof should retain its controller identity");

    const bool repeatedReadyChanged =
        RecordTaskbarMountReadinessObservation(readiness, true, 102u);
    const TaskbarMountReadinessSnapshot renewedReady =
        SnapshotTaskbarMountReadiness(readiness, 113u, 12u);
    passed &= Expect(
        !repeatedReadyChanged && renewedReady.ready &&
            renewedReady.generation == firstReady.generation &&
            renewedReady.proofSequence > firstReady.proofSequence,
        L"a repeated owner-thread proof should renew ready without changing "
        L"its mount generation");

    const bool lostChanged =
        RecordTaskbarMountReadinessObservation(readiness, false, 114u);
    const bool laterReadyChanged =
        RecordTaskbarMountReadinessObservation(readiness, true, 115u);
    passed &= Expect(
        lostChanged && laterReadyChanged,
        L"a lost and later ready observation should both advance state");
    const TaskbarMountReadinessSnapshot pendingLost =
        SnapshotTaskbarMountReadiness(readiness, 115u, 12u);
    passed &= Expect(
        !pendingLost.ready && pendingLost.pendingLost &&
            pendingLost.generation > firstReady.generation,
        L"a pending lost report should hide the later ready generation");
    const bool wrongControllerAcknowledged = AcknowledgeTaskbarMountLost(
        readiness,
        18u,
        pendingLost.generation);
    const bool lostStillPending =
        SnapshotTaskbarMountReadiness(readiness, 115u, 12u).pendingLost;
    passed &= Expect(
        !wrongControllerAcknowledged && lostStillPending,
        L"an acknowledgement from another controller cannot clear lost");
    passed &= Expect(
        AcknowledgeTaskbarMountLost(
            readiness,
            17u,
            pendingLost.generation),
        L"the exact controller and generation should acknowledge lost");
    const TaskbarMountReadinessSnapshot recoveredReady =
        SnapshotTaskbarMountReadiness(readiness, 115u, 12u);
    passed &= Expect(
        recoveredReady.ready && !recoveredReady.pendingLost &&
            recoveredReady.generation > pendingLost.generation,
        L"ready may be reported only after the lost latch is acknowledged");

    BeginTaskbarMountReadinessSession(readiness, 23u, 200u);
    RecordTaskbarMountReadinessObservation(readiness, true, 201u);
    const TaskbarMountReadinessSnapshot stale =
        SnapshotTaskbarMountReadiness(readiness, 214u, 12u);
    passed &= Expect(
        stale.controllerNonce == 23u && !stale.ready &&
            stale.pendingLost,
        L"an expired owner-thread proof should become a latched lost report");
    BeginTaskbarMountReadinessSession(readiness, 24u, 215u);
    const bool oldSessionAcknowledged = AcknowledgeTaskbarMountLost(
        readiness,
        23u,
        stale.generation);
    const TaskbarMountReadinessSnapshot newSession =
        SnapshotTaskbarMountReadiness(readiness, 215u, 12u);
    passed &= Expect(
        !oldSessionAcknowledged && newSession.controllerNonce == 24u &&
            !newSession.ready && !newSession.pendingLost &&
            newSession.generation > stale.generation,
        L"an old in-flight acknowledgement cannot mutate a new session");

    if (!passed) {
        return 1;
    }
    fputws(L"Taskbar host controller policy tests passed.\n", stdout);
    return 0;
}
