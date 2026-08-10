#pragma once

#include "module_profile.h"
#include "taskbar_host_contract.h"

#include <cstdint>
#include <string>
#include <string_view>

namespace taskflyout::taskbar {

enum class HostControlDispatchStatus : std::uint32_t {
    Dispatched = 0,
    InvalidCommand = 1,
    ProbeRejected = 2,
    TaskbarWindowChanged = 3,
    MessageRegistrationFailed = 4,
    HostPathUnavailable = 5,
    HostLoadFailed = 6,
    HostApiMismatch = 7,
    EntryHookUnavailable = 8,
    HookInstallFailed = 9,
    MessageDispatchFailed = 10,
    HookRemoveFailed = 11,
};

struct HostControlDispatchResult {
    HostControlDispatchStatus status =
        HostControlDispatchStatus::InvalidCommand;
    HostControlCommand command = HostControlCommand::Start;
    ProbeStatus probeStatus = ProbeStatus::TaskbarWindowMissing;
    DWORD processId = 0;
    DWORD threadId = 0;
    UINT messageId = 0;
    std::wstring detail;
};

// Inject the host for one control message, dispatch start/stop on the verified
// taskbar owner thread, and remove the temporary hook before returning. The
// host pins itself when start succeeds, so the broker does not need to remain
// resident after a successful dispatch.
HostControlDispatchResult DispatchHostControl(
    HostControlCommand command,
    std::wstring_view hostPath = {});

const wchar_t* HostControlDispatchStatusName(
    HostControlDispatchStatus status) noexcept;

}  // namespace taskflyout::taskbar
