#include "module_profile.h"

#include <TlHelp32.h>

#include <array>
#include <algorithm>
#include <cwchar>
#include <limits>
#include <memory>
#include <string_view>
#include <vector>

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

bool ReadExactAt(
    HANDLE file,
    std::uint64_t offset,
    void* buffer,
    DWORD bytes,
    std::uint64_t fileSize,
    const wchar_t* failureDetail,
    std::wstring& detail) noexcept {
    if (offset > fileSize || bytes > fileSize - offset) {
        detail = failureDetail;
        return false;
    }

    if (offset > static_cast<std::uint64_t>(std::numeric_limits<LONGLONG>::max())) {
        detail = failureDetail;
        return false;
    }

    LARGE_INTEGER fileOffset{};
    fileOffset.QuadPart = static_cast<LONGLONG>(offset);
    if (!SetFilePointerEx(file, fileOffset, nullptr, FILE_BEGIN)) {
        detail = failureDetail;
        return false;
    }

    DWORD bytesRead = 0;
    if (!ReadFile(file, buffer, bytes, &bytesRead, nullptr) ||
        bytesRead != bytes) {
        detail = failureDetail;
        return false;
    }

    return true;
}

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

bool ReadPeBytesAtRva(
    const std::wstring& path,
    std::uint32_t rva,
    std::span<std::uint8_t> output,
    std::wstring& detail) noexcept {
    if (output.empty()) {
        detail = L"empty-pe-range";
        return false;
    }

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

    LARGE_INTEGER fileSizeValue{};
    if (!GetFileSizeEx(file.get(), &fileSizeValue) ||
        fileSizeValue.QuadPart < 0) {
        detail = L"pe-size-failed";
        return false;
    }
    const auto fileSize = static_cast<std::uint64_t>(fileSizeValue.QuadPart);
    if (output.size() > std::numeric_limits<DWORD>::max()) {
        detail = L"pe-range-too-large";
        return false;
    }

    IMAGE_DOS_HEADER dos{};
    if (!ReadExactAt(
            file.get(),
            0,
            &dos,
            sizeof(dos),
            fileSize,
            L"invalid-dos-header",
            detail) ||
        dos.e_magic != IMAGE_DOS_SIGNATURE ||
        dos.e_lfanew < static_cast<LONG>(sizeof(IMAGE_DOS_HEADER))) {
        detail = L"invalid-dos-header";
        return false;
    }

    const auto peOffset = static_cast<std::uint64_t>(dos.e_lfanew);
    DWORD signature = 0;
    IMAGE_FILE_HEADER fileHeader{};
    if (!ReadExactAt(
            file.get(),
            peOffset,
            &signature,
            sizeof(signature),
            fileSize,
            L"invalid-pe-header",
            detail) ||
        !ReadExactAt(
            file.get(),
            peOffset + sizeof(signature),
            &fileHeader,
            sizeof(fileHeader),
            fileSize,
            L"invalid-pe-header",
            detail) ||
        signature != IMAGE_NT_SIGNATURE ||
        fileHeader.Machine != IMAGE_FILE_MACHINE_AMD64 ||
        fileHeader.SizeOfOptionalHeader != sizeof(IMAGE_OPTIONAL_HEADER64) ||
        fileHeader.NumberOfSections == 0 ||
        fileHeader.NumberOfSections > 96) {
        detail = L"invalid-pe-header";
        return false;
    }

    IMAGE_OPTIONAL_HEADER64 optionalHeader{};
    const auto optionalHeaderOffset =
        peOffset + sizeof(signature) + sizeof(fileHeader);
    if (!ReadExactAt(
            file.get(),
            optionalHeaderOffset,
            &optionalHeader,
            sizeof(optionalHeader),
            fileSize,
            L"invalid-pe-header",
            detail) ||
        optionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR64_MAGIC ||
        optionalHeader.SizeOfHeaders == 0 ||
        optionalHeader.SizeOfHeaders > optionalHeader.SizeOfImage) {
        detail = L"invalid-pe-header";
        return false;
    }

    const std::uint64_t rangeEnd =
        static_cast<std::uint64_t>(rva) + output.size();
    if (rangeEnd < rva ||
        rangeEnd > std::numeric_limits<std::uint32_t>::max()) {
        detail = L"pe-range-overflow";
        return false;
    }
    if (rangeEnd > optionalHeader.SizeOfImage) {
        detail = L"pe-range-outside-image";
        return false;
    }

    if (rva < optionalHeader.SizeOfHeaders &&
        rangeEnd <= optionalHeader.SizeOfHeaders) {
        if (!ReadExactAt(
                file.get(),
                rva,
                output.data(),
                static_cast<DWORD>(output.size()),
                fileSize,
                L"read-pe-range-failed",
                detail)) {
            return false;
        }
        detail = L"ok";
        return true;
    }

    std::vector<IMAGE_SECTION_HEADER> sections(
        fileHeader.NumberOfSections);
    const auto sectionTableOffset =
        optionalHeaderOffset + fileHeader.SizeOfOptionalHeader;
    const auto sectionTableBytes =
        sections.size() * sizeof(IMAGE_SECTION_HEADER);
    if (sectionTableBytes > std::numeric_limits<DWORD>::max() ||
        !ReadExactAt(
            file.get(),
            sectionTableOffset,
            sections.data(),
            static_cast<DWORD>(sectionTableBytes),
            fileSize,
            L"invalid-section-table",
            detail)) {
        detail = L"invalid-section-table";
        return false;
    }

    for (const IMAGE_SECTION_HEADER& section : sections) {
        const std::uint64_t sectionStart = section.VirtualAddress;
        const std::uint64_t sectionExtent = std::max(
            section.Misc.VirtualSize,
            section.SizeOfRawData);
        const std::uint64_t sectionEnd = sectionStart + sectionExtent;
        if (rva < sectionStart || rangeEnd > sectionEnd) {
            continue;
        }

        const std::uint64_t offsetInSection = rva - sectionStart;
        const std::uint64_t rawEnd =
            static_cast<std::uint64_t>(section.PointerToRawData) +
            section.SizeOfRawData;
        if (rawEnd < section.PointerToRawData || rawEnd > fileSize ||
            offsetInSection + output.size() > section.SizeOfRawData) {
            detail = L"pe-range-not-backed-by-file";
            return false;
        }

        const std::uint64_t fileOffset =
            static_cast<std::uint64_t>(section.PointerToRawData) +
            offsetInSection;
        if (!ReadExactAt(
                file.get(),
                fileOffset,
                output.data(),
                static_cast<DWORD>(output.size()),
                fileSize,
                L"read-pe-range-failed",
                detail)) {
            return false;
        }

        detail = L"ok";
        return true;
    }

    detail = L"pe-rva-not-mapped";
    return false;
}

bool MatchesTaskbarFrameHookPrologue(
    std::span<const std::uint8_t> bytes) noexcept {
    return bytes.size() == kTaskbarFrameLayoutHookPrologue.size() &&
           std::equal(
               bytes.begin(),
               bytes.end(),
               kTaskbarFrameLayoutHookPrologue.begin());
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

    std::array<std::uint8_t, kTaskbarFrameLayoutHookPrologue.size()>
        hookPrologue{};
    if (!ReadPeBytesAtRva(
            result.module.taskbarViewPath,
            kTaskbarFrameLayoutHookRva,
            hookPrologue,
            result.detail) ||
        !MatchesTaskbarFrameHookPrologue(hookPrologue)) {
        result.status = ProbeStatus::TaskbarHookTargetMismatch;
        if (result.detail == L"ok") {
            result.detail = L"taskbar-hook-prologue-mismatch";
        }
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
        case ProbeStatus::TaskbarHookTargetMismatch:
            return L"taskbar-hook-target-mismatch";
    }

    return L"unknown";
}

}  // namespace taskflyout::taskbar
