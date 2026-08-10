#pragma once

#include <Windows.h>

#include <cstdint>

namespace taskflyout::taskbar {

inline constexpr std::uint32_t kHostApiVersion = 2;

enum class HostCompatibility : std::uint32_t {
    Unknown = 0,
    Supported = 1,
    UnsupportedProcess = 2,
    UnsupportedWindowsBuild = 3,
    TaskbarViewMissing = 4,
    TaskbarViewUnreadable = 5,
    TaskbarViewNotAllowlisted = 6,
    TaskbarHookTargetMismatch = 7,
};

struct HostCompatibilityReport {
    std::uint32_t size;
    std::uint32_t apiVersion;
    HostCompatibility status;
    std::uint32_t windowsBuild;
    std::uint32_t timeDateStamp;
    std::uint32_t sizeOfImage;
    std::uint32_t checksum;
};

}  // namespace taskflyout::taskbar

extern "C" {

#if defined(TASKFLYOUT_TASKBAR_HOST_EXPORTS)
#define TASKFLYOUT_TASKBAR_HOST_API __declspec(dllexport)
#else
#define TASKFLYOUT_TASKBAR_HOST_API __declspec(dllimport)
#endif

TASKFLYOUT_TASKBAR_HOST_API std::uint32_t WINAPI
TaskFlyoutTaskbarHost_GetApiVersion() noexcept;

TASKFLYOUT_TASKBAR_HOST_API BOOL WINAPI TaskFlyoutTaskbarHost_ProbeCurrentProcess(
    taskflyout::taskbar::HostCompatibilityReport* report) noexcept;

TASKFLYOUT_TASKBAR_HOST_API LRESULT CALLBACK TaskFlyoutTaskbarHost_EntryHook(
    int code,
    WPARAM wParam,
    LPARAM lParam) noexcept;

}
