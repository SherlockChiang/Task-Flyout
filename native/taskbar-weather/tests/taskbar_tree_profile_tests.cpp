#include "taskbar_tree_profile.h"

#include <cstdio>

namespace {

bool Expect(bool condition, const wchar_t* message) {
    if (condition) {
        return true;
    }
    fwprintf(stderr, L"FAILED: %ls\n", message);
    return false;
}

taskflyout::taskbar::TaskbarTreeSignature LandmarkSignature() {
    taskflyout::taskbar::TaskbarTreeSignature signature;
    signature.frameClassMatches = true;
    signature.frameLoaded = true;
    signature.frameGeometryValid = true;
    signature.rootGridTypeMatches = true;
    signature.rootGridLoaded = true;
    signature.rootGridGeometryValid = true;
    signature.rootGridSharesXamlRoot = true;
    signature.dispatcherAvailable = true;
    signature.dispatcherHasThreadAccess = true;
    signature.rootGridDirectCount = 1;
    signature.backgroundDirectCount = 1;
    signature.repeaterDirectCount = 1;
    return signature;
}

}  // namespace

int wmain() {
    using taskflyout::taskbar::EvaluateTaskbarTreeSignature;
    using taskflyout::taskbar::TaskbarTreeProbeStatus;

    bool passed = true;
    const auto supported = LandmarkSignature();
    passed &= Expect(
        EvaluateTaskbarTreeSignature(supported) ==
            TaskbarTreeProbeStatus::LandmarksMatched,
        L"complete taskbar landmark signature should be accepted");

    auto mutated = supported;
    mutated.frameClassMatches = false;
    passed &= Expect(
        EvaluateTaskbarTreeSignature(mutated) ==
            TaskbarTreeProbeStatus::FrameTypeMismatch,
        L"unexpected frame class should fail closed");
    mutated = supported;
    mutated.frameLoaded = false;
    passed &= Expect(
        EvaluateTaskbarTreeSignature(mutated) ==
            TaskbarTreeProbeStatus::FrameNotLoaded,
        L"unloaded frame should fail closed");
    mutated = supported;
    mutated.frameGeometryValid = false;
    passed &= Expect(
        EvaluateTaskbarTreeSignature(mutated) ==
            TaskbarTreeProbeStatus::FrameGeometryInvalid,
        L"invalid frame geometry should fail closed");
    mutated = supported;
    mutated.rootGridDirectCount = 0;
    passed &= Expect(
        EvaluateTaskbarTreeSignature(mutated) ==
            TaskbarTreeProbeStatus::RootGridMissing,
        L"missing RootGrid should fail closed");
    mutated = supported;
    mutated.rootGridDirectCount = 2;
    passed &= Expect(
        EvaluateTaskbarTreeSignature(mutated) ==
            TaskbarTreeProbeStatus::RootGridDuplicate,
        L"duplicate RootGrid should fail closed");
    mutated = supported;
    mutated.rootGridTypeMatches = false;
    passed &= Expect(
        EvaluateTaskbarTreeSignature(mutated) ==
            TaskbarTreeProbeStatus::RootGridTypeMismatch,
        L"unexpected RootGrid type should fail closed");
    mutated = supported;
    mutated.rootGridLoaded = false;
    passed &= Expect(
        EvaluateTaskbarTreeSignature(mutated) ==
            TaskbarTreeProbeStatus::RootGridNotLoaded,
        L"unloaded RootGrid should fail closed");
    mutated = supported;
    mutated.rootGridGeometryValid = false;
    passed &= Expect(
        EvaluateTaskbarTreeSignature(mutated) ==
            TaskbarTreeProbeStatus::RootGridGeometryInvalid,
        L"invalid RootGrid geometry should fail closed");
    mutated = supported;
    mutated.rootGridSharesXamlRoot = false;
    passed &= Expect(
        EvaluateTaskbarTreeSignature(mutated) ==
            TaskbarTreeProbeStatus::RootGridXamlRootMismatch,
        L"RootGrid on another XamlRoot should fail closed");
    mutated = supported;
    mutated.dispatcherAvailable = false;
    passed &= Expect(
        EvaluateTaskbarTreeSignature(mutated) ==
            TaskbarTreeProbeStatus::DispatcherUnavailable,
        L"missing XAML dispatcher should fail closed");
    mutated = supported;
    mutated.dispatcherHasThreadAccess = false;
    passed &= Expect(
        EvaluateTaskbarTreeSignature(mutated) ==
            TaskbarTreeProbeStatus::DispatcherThreadMismatch,
        L"wrong XAML dispatcher thread should fail closed");
    mutated = supported;
    mutated.backgroundDirectCount = 0;
    passed &= Expect(
        EvaluateTaskbarTreeSignature(mutated) ==
            TaskbarTreeProbeStatus::BackgroundMissing,
        L"missing taskbar background should fail closed");
    mutated = supported;
    mutated.backgroundDirectCount = 2;
    passed &= Expect(
        EvaluateTaskbarTreeSignature(mutated) ==
            TaskbarTreeProbeStatus::BackgroundDuplicate,
        L"duplicate taskbar background should fail closed");
    mutated = supported;
    mutated.repeaterDirectCount = 2;
    passed &= Expect(
        EvaluateTaskbarTreeSignature(mutated) ==
            TaskbarTreeProbeStatus::RepeaterDuplicate,
        L"duplicate taskbar repeater should fail closed");
    mutated = supported;
    mutated.repeaterDirectCount = 0;
    passed &= Expect(
        EvaluateTaskbarTreeSignature(mutated) ==
            TaskbarTreeProbeStatus::RepeaterMissing,
        L"missing direct taskbar repeater should fail closed");

    if (!passed) {
        return 1;
    }
    fputws(L"Taskbar tree profile tests passed.\n", stdout);
    return 0;
}
