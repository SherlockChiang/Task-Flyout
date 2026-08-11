#pragma once

#include <cstdint>

namespace taskflyout::taskbar {

// Pure transition state. The Explorer Host serializes every mutation and
// snapshot with its readiness SRW lock; tests can drive the same reducer.
struct TaskbarMountReadinessState {
    std::uint32_t controllerNonce = 0;
    bool observedReady = false;
    std::uint64_t observedAtTicks = 0;
    std::uint64_t observationGeneration = 1;
    std::uint64_t proofSequence = 1;
    bool lostPending = false;
    std::uint64_t pendingLostGeneration = 0;
};

struct TaskbarMountReadinessSnapshot {
    std::uint32_t controllerNonce = 0;
    std::uint64_t generation = 1;
    std::uint64_t proofSequence = 1;
    bool ready = false;
    bool pendingLost = false;
};

namespace mount_readiness_detail {

constexpr void AdvanceNonZeroSequence(std::uint64_t& sequence) noexcept {
    ++sequence;
    if (sequence == 0) {
        sequence = 1;
    }
}

}  // namespace mount_readiness_detail

constexpr bool IsTaskbarMountObservationFresh(
    const bool ready,
    const std::uint64_t observedAtTicks,
    const std::uint64_t nowTicks,
    const std::uint64_t maximumAgeTicks) noexcept {
    return ready && observedAtTicks != 0 && maximumAgeTicks != 0 &&
        nowTicks >= observedAtTicks &&
        nowTicks - observedAtTicks <= maximumAgeTicks;
}

constexpr void BeginTaskbarMountReadinessSession(
    TaskbarMountReadinessState& state,
    const std::uint32_t controllerNonce,
    const std::uint64_t nowTicks) noexcept {
    state.controllerNonce = controllerNonce;
    state.observedReady = false;
    state.observedAtTicks = nowTicks;
    mount_readiness_detail::AdvanceNonZeroSequence(
        state.observationGeneration);
    mount_readiness_detail::AdvanceNonZeroSequence(state.proofSequence);
    state.lostPending = false;
    state.pendingLostGeneration = 0;
}

// Returns true when the observed ready/lost state changed and the reporter
// should be woken immediately. Every call is still a new owner-thread proof.
constexpr bool RecordTaskbarMountReadinessObservation(
    TaskbarMountReadinessState& state,
    const bool ready,
    const std::uint64_t nowTicks) noexcept {
    const bool changed = state.observedReady != ready;
    state.observedReady = ready;
    state.observedAtTicks = nowTicks;
    mount_readiness_detail::AdvanceNonZeroSequence(state.proofSequence);
    if (changed) {
        mount_readiness_detail::AdvanceNonZeroSequence(
            state.observationGeneration);
        if (!ready) {
            state.lostPending = true;
            state.pendingLostGeneration = state.observationGeneration;
        }
    }
    return changed;
}

constexpr TaskbarMountReadinessSnapshot SnapshotTaskbarMountReadiness(
    TaskbarMountReadinessState& state,
    const std::uint64_t nowTicks,
    const std::uint64_t maximumAgeTicks) noexcept {
    if (state.observedReady &&
        !IsTaskbarMountObservationFresh(
            true,
            state.observedAtTicks,
            nowTicks,
            maximumAgeTicks)) {
        state.observedReady = false;
        state.observedAtTicks = nowTicks;
        mount_readiness_detail::AdvanceNonZeroSequence(
            state.observationGeneration);
        state.lostPending = true;
        state.pendingLostGeneration = state.observationGeneration;
    }
    return state.lostPending
        ? TaskbarMountReadinessSnapshot{
            state.controllerNonce,
            state.pendingLostGeneration,
            state.proofSequence,
            false,
            true}
        : TaskbarMountReadinessSnapshot{
            state.controllerNonce,
            state.observationGeneration,
            state.proofSequence,
            state.observedReady,
            false};
}

// Clears only the exact pending lost report from the same controller session.
// The caller can inspect observedReady after a successful clear to decide
// whether a later ready generation should be sent immediately.
constexpr bool AcknowledgeTaskbarMountLost(
    TaskbarMountReadinessState& state,
    const std::uint32_t controllerNonce,
    const std::uint64_t generation) noexcept {
    if (state.controllerNonce != controllerNonce ||
        !state.lostPending ||
        state.pendingLostGeneration != generation) {
        return false;
    }
    state.lostPending = false;
    state.pendingLostGeneration = 0;
    return true;
}

enum class TaskbarMountReportAction : std::uint32_t {
    None = 0,
    Current = 1,
    PendingLost = 2,
};

constexpr TaskbarMountReportAction EvaluateTaskbarMountReportAction(
    const bool pendingLost,
    const bool stateChanged,
    const bool hasNewReadyProof) noexcept {
    if (pendingLost) {
        return TaskbarMountReportAction::PendingLost;
    }
    return stateChanged || hasNewReadyProof
        ? TaskbarMountReportAction::Current
        : TaskbarMountReportAction::None;
}

}  // namespace taskflyout::taskbar
