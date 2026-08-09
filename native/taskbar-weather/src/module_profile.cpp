#include "module_profile.h"

#include <TlHelp32.h>

#include <array>
#include <cwchar>
#include <memory>
#include <string_view>

namespace taskflyout::taskbar {
namespace {

struct HandleCloser {
    void operator()(HANDLE handle) const noexcept {
        if (handle && handle != INVALID_HANDLE_VALUE) {
            CloseHandle(handle);
        }
    }
};

using UniqueHandle = std::unique_ptr<void, HandleCloser>;

struct RtlOsVersionInfo {
    ULONG size;
    ULONG majorVersion;
    ULONG minorVersion;
    ULONG buildNumber;
    ULONG platformId;
    WCHAR servicePack[128];
};

std::wstring GetProcessPath(DWORD processId) {
    UniqueHandle process(
        OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, processId));
    if (!process) {
        return {};
    }

    std::array<wchar_t, 32768> path{};
    DWORD length = static_cast<DWORD>(path.size());
    if (!QueryFullProcessImageNameW(process.get(), 0, path.data(), &length)) {
        return {};
    }

    return std::wstring(path.data(), length);
}

bool IsWindowsExplorerPath(const std::wstring& path) {
    std::array<wchar_t, MAX_PATH> windowsDirectory{};
    const UINT length = GetWindowsDirectoryW(
        windowsDirectory.data(),
        static_cast<UINT>(windowsDirectory.size()));
    if (length == 0 || length >= windowsDirectory.size()) {
        return false;
    }

    std::wstring expected(windowsDirectory.data(), length);
    expected += L"\\explorer.exe";
    return _wcsicmp(expected.c_str(), path.c_str()) == 0;
}

bool FindTaskbarViewModule(
    DWORD processId,
    std::wstring& path,
    std::wstring& detail) {
    UniqueHandle snapshot(CreateToolhelp32Snapshot(
        TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32,
        processId));
    if (!snapshot || snapshot.get() == INVALID_HANDLE_VALUE) {
        detail = L"module-snapshot-failed";
        return false;
    }

    MODULEENTRY32W module{};
    module.dwSize = sizeof(module);
    if (!Module32FirstW(snapshot.get(), &module)) {
        detail = L"module-enumeration-failed";
        return false;
    }

    do {
        if (_wcsicmp(module.szModule, L"Taskbar.View.dll") == 0) {
            path = module.szExePath;
            return true;
        }
    } while (Module32NextW(snapshot.get(), &module));

    detail = L"taskbar-view-not-loaded";
    return false;
}

}  // namespace

std::uint32_t GetWindowsBuildNumber() noexcept {
    const HMODULE ntdll = GetModuleHandleW(L"ntdll.dll");
    if (!ntdll) {
        return 0;
    }

    using RtlGetVersion = LONG(WINAPI*)(RtlOsVersionInfo*);
    const auto rtlGetVersion = reinterpret_cast<RtlGetVersion>(
        GetProcAddress(ntdll, "RtlGetVersion"));
    if (!rtlGetVersion) {
        return 0;
    }

    RtlOsVersionInfo version{};
    version.size = sizeof(version);
    if (rtlGetVersion(&version) < 0) {
        return 0;
    }

    return version.buildNumber;
}

bool ReadPeFingerprint(
    const std::wstring& path,
    PeFingerprint& fingerprint,
    std::wstring& detail) noexcept {
    fingerprint = {};

    UniqueHandle file(CreateFileW(
        path.c_str(),
        GENERIC_READ,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        nullptr,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        nullptr));
    if (!file || file.get() == INVALID_HANDLE_VALUE) {
        detail = L"open-pe-failed";
        return false;
    }

    IMAGE_DOS_HEADER dos{};
    DWORD bytesRead = 0;
    if (!ReadFile(file.get(), &dos, sizeof(dos), &bytesRead, nullptr) ||
        bytesRead != sizeof(dos) || dos.e_magic != IMAGE_DOS_SIGNATURE ||
        dos.e_lfanew <= 0) {
        detail = L"invalid-dos-header";
        return false;
    }

    LARGE_INTEGER peOffset{};
    peOffset.QuadPart = dos.e_lfanew;
    if (!SetFilePointerEx(file.get(), peOffset, nullptr, FILE_BEGIN)) {
        detail = L"seek-pe-failed";
        return false;
    }

    IMAGE_NT_HEADERS64 headers{};
    if (!ReadFile(file.get(), &headers, sizeof(headers), &bytesRead, nullptr) ||
        bytesRead != sizeof(headers) ||
        headers.Signature != IMAGE_NT_SIGNATURE ||
        headers.OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR64_MAGIC) {
        detail = L"invalid-pe-header";
        return false;
    }

    fingerprint.machine = headers.FileHeader.Machine;
    fingerprint.timeDateStamp = headers.FileHeader.TimeDateStamp;
    fingerprint.sizeOfImage = headers.OptionalHeader.SizeOfImage;
    fingerprint.checksum = headers.OptionalHeader.CheckSum;
    detail = L"ok";
    return true;
}

bool IsAllowlisted(
    std::uint32_t windowsBuild,
    const PeFingerprint& fingerprint) noexcept {
    return windowsBuild == kValidatedWindowsBuild &&
           fingerprint.machine == IMAGE_FILE_MACHINE_AMD64 &&
           fingerprint.timeDateStamp == kValidatedTaskbarViewTimestamp &&
           fingerprint.sizeOfImage == kValidatedTaskbarViewImageSize &&
           fingerprint.checksum == kValidatedTaskbarViewChecksum;
}

ProbeResult ProbePrimaryTaskbar() noexcept {
    ProbeResult result;
    result.windowsBuild = GetWindowsBuildNumber();

    const HWND taskbar = FindWindowW(L"Shell_TrayWnd", nullptr);
    if (!taskbar) {
        result.status = ProbeStatus::TaskbarWindowMissing;
        result.detail = L"primary-taskbar-window-missing";
        return result;
    }

    result.module.threadId = GetWindowThreadProcessId(
        taskbar,
        &result.module.processId);
    if (result.module.threadId == 0 || result.module.processId == 0) {
        result.status = ProbeStatus::TaskbarOwnerUnavailable;
        result.detail = L"taskbar-owner-unavailable";
        return result;
    }

    result.module.explorerPath = GetProcessPath(result.module.processId);
    if (result.module.explorerPath.empty() ||
        !IsWindowsExplorerPath(result.module.explorerPath)) {
        result.status = ProbeStatus::TaskbarOwnerNotExplorer;
        result.detail = L"taskbar-owner-is-not-windows-explorer";
        return result;
    }

    DWORD currentSession = 0;
    DWORD explorerSession = 0;
    if (!ProcessIdToSessionId(GetCurrentProcessId(), &currentSession) ||
        !ProcessIdToSessionId(result.module.processId, &explorerSession) ||
        currentSession != explorerSession) {
        result.status = ProbeStatus::SessionMismatch;
        result.detail = L"explorer-session-mismatch";
        return result;
    }
    result.module.sessionId = explorerSession;

    if (!FindTaskbarViewModule(
            result.module.processId,
            result.module.taskbarViewPath,
            result.detail)) {
        result.status = ProbeStatus::TaskbarViewMissing;
        return result;
    }

    if (!ReadPeFingerprint(
            result.module.taskbarViewPath,
            result.module.fingerprint,
            result.detail)) {
        result.status = ProbeStatus::TaskbarViewUnreadable;
        return result;
    }

    if (result.windowsBuild != kValidatedWindowsBuild) {
        result.status = ProbeStatus::UnsupportedWindowsBuild;
        result.detail = L"windows-build-not-allowlisted";
        return result;
    }

    if (!IsAllowlisted(result.windowsBuild, result.module.fingerprint)) {
        result.status = ProbeStatus::TaskbarViewNotAllowlisted;
        result.detail = L"taskbar-view-not-allowlisted";
        return result;
    }

    result.status = ProbeStatus::Supported;
    result.detail = L"supported";
    return result;
}

const wchar_t* ProbeStatusName(ProbeStatus status) noexcept {
    switch (status) {
        case ProbeStatus::Supported:
            return L"supported";
        case ProbeStatus::TaskbarWindowMissing:
            return L"taskbar-window-missing";
        case ProbeStatus::TaskbarOwnerUnavailable:
            return L"taskbar-owner-unavailable";
        case ProbeStatus::TaskbarOwnerNotExplorer:
            return L"taskbar-owner-not-explorer";
        case ProbeStatus::SessionMismatch:
            return L"session-mismatch";
        case ProbeStatus::TaskbarViewMissing:
            return L"taskbar-view-missing";
        case ProbeStatus::TaskbarViewUnreadable:
            return L"taskbar-view-unreadable";
        case ProbeStatus::UnsupportedWindowsBuild:
            return L"unsupported-windows-build";
        case ProbeStatus::TaskbarViewNotAllowlisted:
            return L"taskbar-view-not-allowlisted";
    }

    return L"unknown";
}

}  // namespace taskflyout::taskbar

