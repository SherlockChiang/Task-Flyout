#include "taskbar_detour.h"

#include <cstdio>

namespace {

void WINAPI NoopFrameCallback(void*) noexcept {}

bool WINAPI RestoreNothing() noexcept {
    return true;
}

bool Expect(bool condition, const wchar_t* message) {
    if (condition) {
        return true;
    }
    fwprintf(stderr, L"FAILED: %ls\n", message);
    return false;
}

}  // namespace

int wmain() {
    using taskflyout::taskbar::ClassifyTaskbarDetourCallbackSkip;
    using taskflyout::taskbar::TaskbarDetourCallbackSkipReason;
    using taskflyout::taskbar::TaskbarDetourState;

    bool passed = true;
    passed &= Expect(
        static_cast<std::uint32_t>(
            TaskbarDetourCallbackSkipReason::None) == 0 &&
            static_cast<std::uint32_t>(
                TaskbarDetourCallbackSkipReason::Inactive) == 1 &&
            static_cast<std::uint32_t>(
                TaskbarDetourCallbackSkipReason::CallbackUnavailable) == 2 &&
            static_cast<std::uint32_t>(
                TaskbarDetourCallbackSkipReason::Reentrant) == 3 &&
            static_cast<std::uint32_t>(
                TaskbarDetourCallbackSkipReason::RecheckRace) == 4,
        L"detour callback skip reasons must remain fixed POD values");
    passed &= Expect(
        ClassifyTaskbarDetourCallbackSkip(
            TaskbarDetourState::Dormant,
            false,
            true,
            false) == TaskbarDetourCallbackSkipReason::Inactive,
        L"inactive detour must take precedence over later callback gates");
    passed &= Expect(
        ClassifyTaskbarDetourCallbackSkip(
            TaskbarDetourState::Active,
            false,
            true,
            false) ==
            TaskbarDetourCallbackSkipReason::CallbackUnavailable,
        L"missing callback must take precedence over reentrancy");
    passed &= Expect(
        ClassifyTaskbarDetourCallbackSkip(
            TaskbarDetourState::Active,
            true,
            true,
            false) == TaskbarDetourCallbackSkipReason::Reentrant,
        L"reentrant callbacks must be classified before the final recheck");
    passed &= Expect(
        ClassifyTaskbarDetourCallbackSkip(
            TaskbarDetourState::Active,
            true,
            false,
            false) == TaskbarDetourCallbackSkipReason::RecheckRace,
        L"a changed callback gate must be classified as a recheck race");
    passed &= Expect(
        ClassifyTaskbarDetourCallbackSkip(
            TaskbarDetourState::Active,
            true,
            false,
            true) == TaskbarDetourCallbackSkipReason::None,
        L"a fully open callback gate must not report a skip");
    passed &= Expect(
        taskflyout::taskbar::PlanTaskbarDetourStop(
            taskflyout::taskbar::TaskbarDetourState::Quarantined,
            true) ==
            taskflyout::taskbar::TaskbarDetourStopAction::RestoreOnly,
        L"quarantined owner-thread stop must retry XAML restoration");
    passed &= Expect(
        taskflyout::taskbar::PlanTaskbarDetourStop(
            taskflyout::taskbar::TaskbarDetourState::Quarantined,
            false) ==
            taskflyout::taskbar::TaskbarDetourStopAction::WrongTaskbarThread,
        L"quarantined restore must remain on the bootstrap thread");
    passed &= Expect(
        !taskflyout::taskbar::IsInsideTaskbarFrameCallback(
            reinterpret_cast<void*>(1)),
        L"private frame lifetime token must be false outside callback");
    const auto initial =
        taskflyout::taskbar::GetTaskbarDetourSnapshot();
    passed &= Expect(
        initial.state == taskflyout::taskbar::TaskbarDetourState::Dormant,
        L"detour runtime should start dormant");
    passed &= Expect(
        initial.entrySequence == 0 &&
            initial.customCallbackSequence == 0 &&
            initial.lastSkipReason ==
                TaskbarDetourCallbackSkipReason::None,
        L"detour callback telemetry should start empty");
    passed &= Expect(
        taskflyout::taskbar::StartTaskbarFrameDetour(nullptr) ==
            taskflyout::taskbar::TaskbarDetourResult::InvalidCallback,
        L"detour should reject a null callback");

    const auto startResult =
        taskflyout::taskbar::StartTaskbarFrameDetour(NoopFrameCallback);
    passed &= Expect(
        startResult ==
            taskflyout::taskbar::TaskbarDetourResult::CompatibilityRejected,
        L"detour should fail closed outside the Explorer taskbar owner");

    const auto rejected =
        taskflyout::taskbar::GetTaskbarDetourSnapshot();
    passed &= Expect(
        rejected.state == taskflyout::taskbar::TaskbarDetourState::Dormant,
        L"compatibility rejection should leave the runtime dormant");
    passed &= Expect(
        rejected.activeCallbacks == 0,
        L"compatibility rejection should not enter a callback");
    passed &= Expect(
        rejected.activeCustomCallbacks == 0,
        L"compatibility rejection should not enter a custom callback");
    passed &= Expect(
        taskflyout::taskbar::StopTaskbarFrameDetour(RestoreNothing) ==
            taskflyout::taskbar::TaskbarDetourResult::NotActive,
        L"stopping a rejected detour should be idempotent");

    if (!passed) {
        return 1;
    }

    fputws(L"Taskbar detour state tests passed.\n", stdout);
    return 0;
}
