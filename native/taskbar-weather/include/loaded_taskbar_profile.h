#pragma once

#include "taskbar_host_contract.h"

#include <Windows.h>

namespace taskflyout::taskbar {

struct ValidatedTaskbarModule {
    HMODULE module = nullptr;
    void* hookTarget = nullptr;
    DWORD taskbarThreadId = 0;
};

HostCompatibility AcquireValidatedTaskbarModule(
    HostCompatibilityReport& report,
    ValidatedTaskbarModule& module) noexcept;

bool RevalidateTaskbarHookTarget(
    const ValidatedTaskbarModule& module) noexcept;

void ReleaseValidatedTaskbarModule(
    ValidatedTaskbarModule& module) noexcept;

HostCompatibility ProbeLoadedTaskbarHost(
    HostCompatibilityReport& report) noexcept;

}  // namespace taskflyout::taskbar
