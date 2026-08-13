#include "taskbar_host_controller.h"
#include "taskbar_detour.h"
#include "taskbar_mount_readiness_state.h"

#include <cstdio>
#include <limits>

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

    input = ValidDecision();
    passed &= Expect(
        EvaluateTaskbarHostFrameDiagnostic(input) ==
            HostControlDiagnostic::MountedNotReady,
        L"a validated frame should proceed to live mount readiness");
    input.bridgeStatus = TaskbarFrameBridgeStatus::ProjectionFailed;
    passed &= Expect(
        EvaluateTaskbarHostFrameDiagnostic(input) ==
            HostControlDiagnostic::BridgeUnresolved,
        L"an unresolved bridge should have a fixed diagnostic");
    input = ValidDecision();
    input.treeStatus = TaskbarTreeProbeStatus::RootGridDuplicate;
    passed &= Expect(
        EvaluateTaskbarHostFrameDiagnostic(input) ==
            HostControlDiagnostic::TreeProfileMismatch,
        L"a changed tree profile should have a fixed diagnostic");
    input = ValidDecision();
    input.slotProbeStatus = TaskbarSlotProbeStatus::RootStructureMismatch;
    passed &= Expect(
        EvaluateTaskbarHostFrameDiagnostic(input) ==
            HostControlDiagnostic::SlotStructureConflict,
        L"a rejected slot structure should have a fixed diagnostic");
    input = ValidDecision();
    input.slotProbeStatus = TaskbarSlotProbeStatus::TreeNotReady;
    passed &= Expect(
        EvaluateTaskbarHostFrameDiagnostic(input) ==
            HostControlDiagnostic::TreeProfileMismatch,
        L"a slot probe without a current tree should retain the tree cause");
    input = ValidDecision();
    input.slotProbeStatus =
        TaskbarSlotProbeStatus::RepeaterChildTransformInvalid;
    passed &= Expect(
        EvaluateTaskbarHostFrameDiagnostic(input) ==
            HostControlDiagnostic::SlotGeometryConflict,
        L"an invalid slot transform should retain the geometry cause");
    input = ValidDecision();
    input.geometryStatus = TaskbarSlotGeometryStatus::CandidateConflicted;
    passed &= Expect(
        EvaluateTaskbarHostFrameDiagnostic(input) ==
            HostControlDiagnostic::SlotGeometryConflict,
        L"a blocked slot geometry should have a fixed diagnostic");
    passed &= Expect(
        TaskbarMountStatusDiagnostic(
            TaskbarMountStatus::ViewCreationFailed) ==
                HostControlDiagnostic::MountViewFailed &&
            TaskbarMountStatusDiagnostic(TaskbarMountStatus::AppendFailed) ==
                HostControlDiagnostic::MountAppendFailed &&
            TaskbarMountStatusDiagnostic(TaskbarMountStatus::RestoreFailed) ==
                HostControlDiagnostic::MountRestoreFailed &&
            TaskbarMountStatusDiagnostic(TaskbarMountStatus::Mounted) ==
                HostControlDiagnostic::MountedNotReady,
        L"mount outcomes should map only to bounded diagnostics");

    passed &= Expect(
        TaskbarFrameBootstrapDiagnostic(
            TaskbarFrameBootstrapStatus::WindowInvalid) ==
                HostControlDiagnostic::BootstrapWindowInvalid &&
            TaskbarFrameBootstrapDiagnostic(
                TaskbarFrameBootstrapStatus::WrongOwnerThread) ==
                HostControlDiagnostic::BootstrapOwnerThreadMismatch &&
            TaskbarFrameBootstrapDiagnostic(
                TaskbarFrameBootstrapStatus::HostBoundsInvalid) ==
                HostControlDiagnostic::BootstrapHostBoundsInvalid &&
            TaskbarFrameBootstrapDiagnostic(
                TaskbarFrameBootstrapStatus::QueryFailed) ==
                HostControlDiagnostic::BootstrapQueryFailed &&
            TaskbarFrameBootstrapDiagnostic(
                TaskbarFrameBootstrapStatus::EnumerationOverflow) ==
                HostControlDiagnostic::BootstrapEnumerationOverflow &&
            TaskbarFrameBootstrapDiagnostic(
                TaskbarFrameBootstrapStatus::FrameNotObserved) ==
                HostControlDiagnostic::BootstrapFrameNotObserved &&
            TaskbarFrameBootstrapDiagnostic(
                TaskbarFrameBootstrapStatus::FrameAmbiguous) ==
                HostControlDiagnostic::BootstrapFrameAmbiguous &&
            TaskbarFrameBootstrapDiagnostic(
                TaskbarFrameBootstrapStatus::TreeProfileMismatch) ==
                HostControlDiagnostic::BootstrapTreeProfileMismatch &&
            TaskbarFrameBootstrapDiagnostic(
                TaskbarFrameBootstrapStatus::FrameValidated) ==
                HostControlDiagnostic::BootstrapFrameValidated,
        L"bootstrap outcomes should map only to additive fixed diagnostics");

    passed &= Expect(
        SelectTaskbarHostReportedDiagnostic(
            true,
            TaskbarHostControllerResult::Started,
            HostControlDiagnostic::None,
            HostControlDiagnostic::BootstrapFrameValidated) ==
                HostControlDiagnostic::BootstrapFrameValidated &&
            SelectTaskbarHostReportedDiagnostic(
                true,
                TaskbarHostControllerResult::MountPending,
                HostControlDiagnostic::MountedNotReady,
                HostControlDiagnostic::BootstrapFrameNotObserved) ==
                HostControlDiagnostic::BootstrapFrameNotObserved,
        L"an active read-only lifecycle should report its bootstrap result");
    passed &= Expect(
        SelectTaskbarHostReportedDiagnostic(
            true,
            TaskbarHostControllerResult::StopRejected,
            HostControlDiagnostic::MountRestoreFailed,
            HostControlDiagnostic::BootstrapFrameValidated) ==
                HostControlDiagnostic::MountRestoreFailed &&
            SelectTaskbarHostReportedDiagnostic(
                true,
                TaskbarHostControllerResult::StatusRejected,
                HostControlDiagnostic::MountRestoreFailed,
                HostControlDiagnostic::BootstrapFrameNotObserved) ==
                HostControlDiagnostic::MountRestoreFailed &&
            SelectTaskbarHostReportedDiagnostic(
                true,
                TaskbarHostControllerResult::StartRejected,
                HostControlDiagnostic::None,
                HostControlDiagnostic::BootstrapFrameValidated) ==
                HostControlDiagnostic::None,
        L"rejected lifecycle results must not expose a stale bootstrap result");
    passed &= Expect(
        SelectTaskbarHostReportedDiagnostic(
            false,
            TaskbarHostControllerResult::MountPending,
            HostControlDiagnostic::MountedNotReady,
            HostControlDiagnostic::BootstrapFrameValidated) ==
                HostControlDiagnostic::MountedNotReady,
        L"normal mount mode must ignore a retained read-only bootstrap result");

    TaskbarDetourSnapshot detour;
    detour.entrySequence = 41u;
    detour.customCallbackSequence = 17u;
    passed &= Expect(
        EvaluateAwaitingLayoutDiagnostic(detour, 41u, 17u) ==
            HostControlDiagnostic::DetourTargetNotObserved,
        L"an unchanged detour sequence should report an unobserved target");
    detour.entrySequence = 42u;
    detour.lastSkipReason = TaskbarDetourCallbackSkipReason::Inactive;
    passed &= Expect(
        EvaluateAwaitingLayoutDiagnostic(detour, 41u, 17u) ==
            HostControlDiagnostic::DetourInactive,
        L"an inactive detour skip should retain its fixed reason");
    detour.lastSkipReason =
        TaskbarDetourCallbackSkipReason::CallbackUnavailable;
    passed &= Expect(
        EvaluateAwaitingLayoutDiagnostic(detour, 41u, 17u) ==
            HostControlDiagnostic::CallbackUnavailable,
        L"a missing callback should retain its fixed reason");
    detour.lastSkipReason = TaskbarDetourCallbackSkipReason::Reentrant;
    passed &= Expect(
        EvaluateAwaitingLayoutDiagnostic(detour, 41u, 17u) ==
            HostControlDiagnostic::CallbackReentrant,
        L"a reentrant callback skip should retain its fixed reason");
    detour.lastSkipReason = TaskbarDetourCallbackSkipReason::RecheckRace;
    passed &= Expect(
        EvaluateAwaitingLayoutDiagnostic(detour, 41u, 17u) ==
            HostControlDiagnostic::CallbackRecheckRace,
        L"a callback recheck race should retain its fixed reason");
    detour.customCallbackSequence = 18u;
    passed &= Expect(
        EvaluateAwaitingLayoutDiagnostic(detour, 41u, 17u) ==
            HostControlDiagnostic::MountedNotReady,
        L"an observed custom callback should leave initial-layout diagnosis");

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
    const bool wrongGenerationAcknowledged = AcknowledgeTaskbarMountLost(
        readiness,
        17u,
        pendingLost.generation + 1u);
    const bool lostStillPending =
        SnapshotTaskbarMountReadiness(readiness, 115u, 12u).pendingLost;
    passed &= Expect(
        !wrongControllerAcknowledged && !wrongGenerationAcknowledged &&
            lostStillPending,
        L"another controller or generation cannot clear pending lost");
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
    const TaskbarMountReadinessSnapshot repeatedStale =
        SnapshotTaskbarMountReadiness(readiness, 215u, 12u);
    passed &= Expect(
        repeatedStale.pendingLost &&
            repeatedStale.generation == stale.generation,
        L"re-reading a latched stale proof must not advance generation");
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

    TaskbarMountReadinessState wrapping;
    wrapping.observationGeneration =
        std::numeric_limits<std::uint64_t>::max();
    wrapping.proofSequence = std::numeric_limits<std::uint64_t>::max();
    BeginTaskbarMountReadinessSession(wrapping, 31u, 300u);
    const TaskbarMountReadinessSnapshot wrapped =
        SnapshotTaskbarMountReadiness(wrapping, 300u, 12u);
    passed &= Expect(
        wrapped.generation == 1u && wrapped.proofSequence == 1u,
        L"sequence wrap should preserve non-zero protocol values");

    if (!passed) {
        return 1;
    }
    fputws(L"Taskbar host controller policy tests passed.\n", stdout);
    return 0;
}
