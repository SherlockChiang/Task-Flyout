#include "taskbar_frame_bootstrap.h"

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

taskflyout::taskbar::TaskbarFrameBootstrapPolicyInput ValidInput() {
    taskflyout::taskbar::TaskbarFrameBootstrapPolicyInput input;
    input.windowExists = true;
    input.windowClassMatches = true;
    input.windowProcessMatches = true;
    input.ownerThreadMatches = true;
    input.hostBoundsValid = true;
    input.querySucceeded = true;
    input.uniqueFrameCount = 1;
    input.treeProfileStatus =
        taskflyout::taskbar::TaskbarTreeProbeStatus::LandmarksMatched;
    return input;
}

}  // namespace

int wmain() {
    using namespace taskflyout::taskbar;

    bool passed = true;
    TaskbarFrameBootstrapPolicyInput input;
    passed &= Expect(
        EvaluateTaskbarFrameBootstrapPolicy(input) ==
            TaskbarFrameBootstrapStatus::WindowInvalid,
        L"the default policy must reject an invalid window first");

    input = ValidInput();
    input.windowClassMatches = false;
    input.ownerThreadMatches = false;
    passed &= Expect(
        EvaluateTaskbarFrameBootstrapPolicy(input) ==
            TaskbarFrameBootstrapStatus::WindowInvalid,
        L"the exact Shell_TrayWnd class must precede thread validation");

    input = ValidInput();
    input.windowProcessMatches = false;
    passed &= Expect(
        EvaluateTaskbarFrameBootstrapPolicy(input) ==
            TaskbarFrameBootstrapStatus::WindowInvalid,
        L"a taskbar owned by another process must be rejected");

    input = ValidInput();
    input.ownerThreadMatches = false;
    input.hostBoundsValid = false;
    passed &= Expect(
        EvaluateTaskbarFrameBootstrapPolicy(input) ==
            TaskbarFrameBootstrapStatus::WrongOwnerThread,
        L"the owner thread gate must precede host geometry access");

    input = ValidInput();
    input.hostBoundsValid = false;
    input.querySucceeded = false;
    passed &= Expect(
        EvaluateTaskbarFrameBootstrapPolicy(input) ==
            TaskbarFrameBootstrapStatus::HostBoundsInvalid,
        L"invalid DIP bounds must precede the XAML query");

    input = ValidInput();
    input.querySucceeded = false;
    input.enumerationOverflow = true;
    passed &= Expect(
        EvaluateTaskbarFrameBootstrapPolicy(input) ==
            TaskbarFrameBootstrapStatus::QueryFailed,
        L"a failed query must precede any partial enumeration result");

    input = ValidInput();
    input.enumerationOverflow = true;
    input.uniqueFrameCount = 0;
    passed &= Expect(
        EvaluateTaskbarFrameBootstrapPolicy(input) ==
            TaskbarFrameBootstrapStatus::EnumerationOverflow,
        L"the fixed enumeration bound must fail before candidate selection");

    input = ValidInput();
    input.uniqueFrameCount = 0;
    passed &= Expect(
        EvaluateTaskbarFrameBootstrapPolicy(input) ==
            TaskbarFrameBootstrapStatus::FrameNotObserved,
        L"zero distinct TaskbarFrame identities must fail closed");

    input = ValidInput();
    input.uniqueFrameCount = 2;
    passed &= Expect(
        EvaluateTaskbarFrameBootstrapPolicy(input) ==
            TaskbarFrameBootstrapStatus::FrameAmbiguous,
        L"two distinct TaskbarFrame identities must be ambiguous");

    input = ValidInput();
    input.uniqueFrameCount = std::numeric_limits<std::uint32_t>::max();
    passed &= Expect(
        EvaluateTaskbarFrameBootstrapPolicy(input) ==
            TaskbarFrameBootstrapStatus::FrameAmbiguous,
        L"all candidate counts above one must remain ambiguous");

    input = ValidInput();
    input.treeProfileStatus =
        TaskbarTreeProbeStatus::DispatcherThreadMismatch;
    passed &= Expect(
        EvaluateTaskbarFrameBootstrapPolicy(input) ==
            TaskbarFrameBootstrapStatus::TreeProfileMismatch,
        L"a unique frame must still pass the complete tree profile");

    input = ValidInput();
    passed &= Expect(
        EvaluateTaskbarFrameBootstrapPolicy(input) ==
            TaskbarFrameBootstrapStatus::FrameValidated,
        L"one profiled TaskbarFrame should validate");
    passed &= Expect(
        std::wstring_view(TaskbarFrameBootstrapStatusName(
            TaskbarFrameBootstrapStatus::EnumerationOverflow)) ==
            L"enumeration-overflow",
        L"bootstrap failures should remain diagnosable");
    passed &= Expect(
        kMaxTaskbarFrameBootstrapElements == 1024,
        L"the public enumeration budget must remain fixed and bounded");

    if (!passed) {
        return 1;
    }
    fputws(L"Taskbar frame bootstrap policy tests passed.\n", stdout);
    return 0;
}
