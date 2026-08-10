#include "loaded_taskbar_profile.h"

#include "module_profile.h"

#include <Psapi.h>

#include <algorithm>
#include <array>
#include <cstdint>
#include <cwchar>
#include <limits>
#include <string>

namespace taskflyout::taskbar {
namespace {

bool IsExecutableProtection(DWORD protection) noexcept {
    if ((protection & (PAGE_GUARD | PAGE_NOACCESS)) != 0) {
        return false;
    }

    switch (protection & 0xFFU) {
        case PAGE_EXECUTE:
        case PAGE_EXECUTE_READ:
        case PAGE_EXECUTE_READWRITE:
        case PAGE_EXECUTE_WRITECOPY:
            return true;
        default:
            return false;
    }
}

bool IsWindowsExplorerPath(const std::wstring& path) noexcept {
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

std::wstring GetCurrentProcessPath() {
    std::array<wchar_t, 32768> path{};
    const DWORD length = GetModuleFileNameW(
        nullptr,
        path.data(),
        static_cast<DWORD>(path.size()));
    if (length == 0 || length >= path.size()) {
        return {};
    }

    return std::wstring(path.data(), length);
}

bool ReadLoadedImageBytes(
    HMODULE module,
    const MODULEINFO& moduleInfo,
    std::size_t offset,
    void* output,
    std::size_t length) noexcept {
    if (offset > moduleInfo.SizeOfImage ||
        length > moduleInfo.SizeOfImage - offset) {
        return false;
    }

    SIZE_T bytesRead = 0;
    return ReadProcessMemory(
               GetCurrentProcess(),
               reinterpret_cast<const void*>(
                   reinterpret_cast<std::uintptr_t>(module) + offset),
               output,
               length,
               &bytesRead) != FALSE &&
           bytesRead == length;
}

bool ReadLoadedPeFingerprint(
    HMODULE taskbarView,
    MODULEINFO& moduleInfo,
    PeFingerprint& fingerprint) noexcept {
    moduleInfo = {};
    fingerprint = {};
    if (!GetModuleInformation(
            GetCurrentProcess(),
            taskbarView,
            &moduleInfo,
            sizeof(moduleInfo))) {
        return false;
    }

    IMAGE_DOS_HEADER dos{};
    if (!ReadLoadedImageBytes(
            taskbarView,
            moduleInfo,
            0,
            &dos,
            sizeof(dos)) ||
        dos.e_magic != IMAGE_DOS_SIGNATURE ||
        dos.e_lfanew < static_cast<LONG>(sizeof(IMAGE_DOS_HEADER))) {
        return false;
    }

    IMAGE_NT_HEADERS64 headers{};
    if (!ReadLoadedImageBytes(
            taskbarView,
            moduleInfo,
            static_cast<std::size_t>(dos.e_lfanew),
            &headers,
            sizeof(headers)) ||
        headers.Signature != IMAGE_NT_SIGNATURE ||
        headers.FileHeader.Machine != IMAGE_FILE_MACHINE_AMD64 ||
        headers.FileHeader.SizeOfOptionalHeader !=
            sizeof(IMAGE_OPTIONAL_HEADER64) ||
        headers.OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR64_MAGIC ||
        headers.OptionalHeader.SizeOfImage != moduleInfo.SizeOfImage) {
        return false;
    }

    fingerprint.machine = headers.FileHeader.Machine;
    fingerprint.timeDateStamp = headers.FileHeader.TimeDateStamp;
    fingerprint.sizeOfImage = headers.OptionalHeader.SizeOfImage;
    fingerprint.checksum = headers.OptionalHeader.CheckSum;
    return true;
}

bool GetTaskbarOwner(DWORD& processId, DWORD& threadId) noexcept {
    processId = 0;
    threadId = 0;
    const HWND taskbar = FindWindowW(L"Shell_TrayWnd", nullptr);
    if (!taskbar) {
        return false;
    }

    threadId = GetWindowThreadProcessId(taskbar, &processId);
    return processId != 0 && threadId != 0;
}

}  // namespace

bool RevalidateTaskbarHookTarget(
    const ValidatedTaskbarModule& module) noexcept {
    if (!module.module || !module.hookTarget) {
        return false;
    }

    DWORD taskbarProcessId = 0;
    DWORD taskbarThreadId = 0;
    if (!GetTaskbarOwner(taskbarProcessId, taskbarThreadId) ||
        taskbarProcessId != GetCurrentProcessId() ||
        taskbarThreadId != module.taskbarThreadId) {
        return false;
    }

    MODULEINFO moduleInfo{};
    PeFingerprint fingerprint;
    const std::uint32_t windowsBuild = GetWindowsBuildNumber();
    if (!ReadLoadedPeFingerprint(
            module.module,
            moduleInfo,
            fingerprint) ||
        !IsAllowlisted(windowsBuild, fingerprint)) {
        return false;
    }

    constexpr auto rva = kTaskbarFrameLayoutHookRva;
    constexpr auto length = kTaskbarFrameLayoutHookPrologue.size();
    if (rva > moduleInfo.SizeOfImage ||
        length > moduleInfo.SizeOfImage - rva) {
        return false;
    }

    const auto targetAddress =
        reinterpret_cast<std::uintptr_t>(module.module) + rva;
    const auto* target = reinterpret_cast<const std::uint8_t*>(targetAddress);
    if (reinterpret_cast<const void*>(target) != module.hookTarget) {
        return false;
    }

    MEMORY_BASIC_INFORMATION memory{};
    if (VirtualQuery(target, &memory, sizeof(memory)) != sizeof(memory) ||
        memory.State != MEM_COMMIT ||
        memory.Type != MEM_IMAGE ||
        memory.AllocationBase != module.module ||
        !IsExecutableProtection(memory.Protect)) {
        return false;
    }

    const auto regionStart =
        reinterpret_cast<std::uintptr_t>(memory.BaseAddress);
    const auto targetStart = reinterpret_cast<std::uintptr_t>(target);
    if (targetStart < regionStart ||
        targetStart - regionStart > memory.RegionSize ||
        length > memory.RegionSize - (targetStart - regionStart)) {
        return false;
    }

    std::array<std::uint8_t, kTaskbarFrameLayoutHookPrologue.size()>
        loadedPrologue{};
    return ReadLoadedImageBytes(
               module.module,
               moduleInfo,
               rva,
               loadedPrologue.data(),
               loadedPrologue.size()) &&
           std::equal(
               kTaskbarFrameLayoutHookPrologue.begin(),
               kTaskbarFrameLayoutHookPrologue.end(),
               loadedPrologue.begin());
}

void ReleaseValidatedTaskbarModule(
    ValidatedTaskbarModule& module) noexcept {
    if (module.module) {
        FreeLibrary(module.module);
    }
    module = {};
}

HostCompatibility AcquireValidatedTaskbarModule(
    HostCompatibilityReport& report,
    ValidatedTaskbarModule& module) noexcept {
    ReleaseValidatedTaskbarModule(module);

    const std::wstring processPath = GetCurrentProcessPath();
    if (processPath.empty() || !IsWindowsExplorerPath(processPath)) {
        return HostCompatibility::UnsupportedProcess;
    }

    DWORD taskbarProcessId = 0;
    DWORD taskbarThreadId = 0;
    if (!GetTaskbarOwner(taskbarProcessId, taskbarThreadId) ||
        taskbarProcessId != GetCurrentProcessId()) {
        return HostCompatibility::UnsupportedProcess;
    }

    report.windowsBuild = GetWindowsBuildNumber();
    if (report.windowsBuild != kValidatedWindowsBuild) {
        return HostCompatibility::UnsupportedWindowsBuild;
    }

    if (!GetModuleHandleExW(0, L"Taskbar.View.dll", &module.module)) {
        return HostCompatibility::TaskbarViewMissing;
    }
    module.taskbarThreadId = taskbarThreadId;
    const auto moduleAddress =
        reinterpret_cast<std::uintptr_t>(module.module);
    if (moduleAddress >
        std::numeric_limits<std::uintptr_t>::max() -
            kTaskbarFrameLayoutHookRva) {
        ReleaseValidatedTaskbarModule(module);
        return HostCompatibility::TaskbarHookTargetMismatch;
    }
    module.hookTarget = reinterpret_cast<void*>(
        moduleAddress + kTaskbarFrameLayoutHookRva);

    std::array<wchar_t, 32768> modulePath{};
    const DWORD moduleLength = GetModuleFileNameW(
        module.module,
        modulePath.data(),
        static_cast<DWORD>(modulePath.size()));
    if (moduleLength == 0 || moduleLength >= modulePath.size()) {
        ReleaseValidatedTaskbarModule(module);
        return HostCompatibility::TaskbarViewUnreadable;
    }

    PeFingerprint diskFingerprint;
    std::wstring detail;
    if (!ReadPeFingerprint(
            std::wstring(modulePath.data(), moduleLength),
            diskFingerprint,
            detail)) {
        ReleaseValidatedTaskbarModule(module);
        return HostCompatibility::TaskbarViewUnreadable;
    }

    report.timeDateStamp = diskFingerprint.timeDateStamp;
    report.sizeOfImage = diskFingerprint.sizeOfImage;
    report.checksum = diskFingerprint.checksum;
    if (!IsAllowlisted(report.windowsBuild, diskFingerprint)) {
        ReleaseValidatedTaskbarModule(module);
        return HostCompatibility::TaskbarViewNotAllowlisted;
    }

    MODULEINFO moduleInfo{};
    PeFingerprint loadedFingerprint;
    if (!ReadLoadedPeFingerprint(
            module.module,
            moduleInfo,
            loadedFingerprint)) {
        ReleaseValidatedTaskbarModule(module);
        return HostCompatibility::TaskbarViewUnreadable;
    }

    report.timeDateStamp = loadedFingerprint.timeDateStamp;
    report.sizeOfImage = loadedFingerprint.sizeOfImage;
    report.checksum = loadedFingerprint.checksum;
    if (!IsAllowlisted(report.windowsBuild, loadedFingerprint)) {
        ReleaseValidatedTaskbarModule(module);
        return HostCompatibility::TaskbarViewNotAllowlisted;
    }

    if (!RevalidateTaskbarHookTarget(module)) {
        ReleaseValidatedTaskbarModule(module);
        return HostCompatibility::TaskbarHookTargetMismatch;
    }

    return HostCompatibility::Supported;
}

HostCompatibility ProbeLoadedTaskbarHost(
    HostCompatibilityReport& report) noexcept {
    ValidatedTaskbarModule module;
    const HostCompatibility compatibility =
        AcquireValidatedTaskbarModule(report, module);
    ReleaseValidatedTaskbarModule(module);
    return compatibility;
}

}  // namespace taskflyout::taskbar
