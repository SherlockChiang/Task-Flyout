#pragma once

#include <Windows.h>

#include <cstdint>
#include <string>

namespace taskflyout::taskbar {

inline constexpr std::uint32_t kValidatedWindowsBuild = 26200;
inline constexpr std::uint32_t kValidatedTaskbarViewTimestamp = 0x6A3CD591;
inline constexpr std::uint32_t kValidatedTaskbarViewImageSize = 0x0098B000;
inline constexpr std::uint32_t kValidatedTaskbarViewChecksum = 0x00985653;

struct PeFingerprint {
    std::uint16_t machine = 0;
    std::uint32_t timeDateStamp = 0;
    std::uint32_t sizeOfImage = 0;
    std::uint32_t checksum = 0;
};

struct TaskbarModuleInfo {
    DWORD processId = 0;
    DWORD threadId = 0;
    DWORD sessionId = 0;
    std::wstring explorerPath;
    std::wstring taskbarViewPath;
    PeFingerprint fingerprint;
};

enum class ProbeStatus {
    Supported,
    TaskbarWindowMissing,
    TaskbarOwnerUnavailable,
    TaskbarOwnerNotExplorer,
    SessionMismatch,
    TaskbarViewMissing,
    TaskbarViewUnreadable,
    UnsupportedWindowsBuild,
    TaskbarViewNotAllowlisted,
};

struct ProbeResult {
    ProbeStatus status = ProbeStatus::TaskbarWindowMissing;
    std::uint32_t windowsBuild = 0;
    TaskbarModuleInfo module;
    std::wstring detail;
};

std::uint32_t GetWindowsBuildNumber() noexcept;
bool ReadPeFingerprint(
    const std::wstring& path,
    PeFingerprint& fingerprint,
    std::wstring& detail) noexcept;
bool IsAllowlisted(
    std::uint32_t windowsBuild,
    const PeFingerprint& fingerprint) noexcept;
ProbeResult ProbePrimaryTaskbar() noexcept;
const wchar_t* ProbeStatusName(ProbeStatus status) noexcept;

}  // namespace taskflyout::taskbar

