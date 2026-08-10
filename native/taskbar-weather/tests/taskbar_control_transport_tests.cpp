#include "taskbar_control_transport.h"

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
    const auto invalid = DispatchHostControl(
        static_cast<HostControlCommand>(99));
    passed &= Expect(
        invalid.status == HostControlDispatchStatus::InvalidCommand,
        L"the broker must reject unknown control commands before probing");
    passed &= Expect(
        std::wstring_view(HostControlDispatchStatusName(
            HostControlDispatchStatus::MessageDispatchFailed)) ==
            L"message-dispatch-failed",
        L"control failures should remain diagnosable");

    if (!passed) {
        return 1;
    }
    fputws(L"Taskbar control transport policy tests passed.\n", stdout);
    return 0;
}
