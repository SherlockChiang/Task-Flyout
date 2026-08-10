#define TASKFLYOUT_TASKBAR_HOST_EXPORTS

#include "module_profile.h"
#include "taskbar_host_contract.h"

#include <Psapi.h>

#include <algorithm>
#include <array>
#include <cstdint>
#include <cwchar>

using taskflyout::taskbar::HostCompatibility;
using taskflyout::taskbar::HostCompatibilityReport;
using taskflyout::taskbar::PeFingerprint;

namespace {

class ModuleReference final {
public:
    ModuleReference() = default;
    ModuleReference(const ModuleReference&) = delete;
    ModuleReference& operator=(const ModuleReference&) = delete;

    ~ModuleReference() {
        if (module_) {
            FreeLibrary(module_);
        }
    }

    bool Acquire(const wchar_t* moduleName) noexcept {
        return GetModuleHandleExW(0, moduleName, &module_) != FALSE;
    }

    HMODULE get() const noexcept {
        return module_;
    }

private:
    HMODULE module_ = nullptr;
};

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
               reinterpret_cast<const std::uint8_t*>(module) + offset,
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

bool MatchesLoadedHookTarget(
    HMODULE taskbarView,
    const MODULEINFO& moduleInfo) noexcept {

    constexpr auto rva = taskflyout::taskbar::kTaskbarFrameLayoutHookRva;
    constexpr auto length =
        taskflyout::taskbar::kTaskbarFrameLayoutHookPrologue.size();
    if (rva > moduleInfo.SizeOfImage ||
        length > moduleInfo.SizeOfImage - rva) {
        return false;
    }

    const auto* target = reinterpret_cast<const std::uint8_t*>(taskbarView) +
        rva;
    MEMORY_BASIC_INFORMATION memory{};
    if (VirtualQuery(target, &memory, sizeof(memory)) != sizeof(memory) ||
        memory.State != MEM_COMMIT ||
        !IsExecutableProtection(memory.Protect)) {
        return false;
    }

    const auto regionStart =
        reinterpret_cast<std::uintptr_t>(memory.BaseAddress);
    const auto targetStart = reinterpret_cast<std::uintptr_t>(target);
    if (targetStart < regionStart ||
        length > memory.RegionSize - (targetStart - regionStart)) {
        return false;
    }

    std::array<std::uint8_t,
               taskflyout::taskbar::kTaskbarFrameLayoutHookPrologue.size()>
        loadedPrologue{};
    if (!ReadLoadedImageBytes(
            taskbarView,
            moduleInfo,
            rva,
            loadedPrologue.data(),
            loadedPrologue.size())) {
        return false;
    }

    return std::equal(
        taskflyout::taskbar::kTaskbarFrameLayoutHookPrologue.begin(),
        taskflyout::taskbar::kTaskbarFrameLayoutHookPrologue.end(),
        loadedPrologue.begin());
}

HostCompatibility ProbeCurrentProcessImpl(
    HostCompatibilityReport& report) noexcept {
    std::array<wchar_t, MAX_PATH> processPath{};
    const DWORD processLength = GetModuleFileNameW(
        nullptr,
        processPath.data(),
        static_cast<DWORD>(processPath.size()));
    const wchar_t* processName = processLength == 0
        ? L""
        : wcsrchr(processPath.data(), L'\\');
    processName = processName ? processName + 1 : processPath.data();
    if (_wcsicmp(processName, L"explorer.exe") != 0) {
        return HostCompatibility::UnsupportedProcess;
    }

    report.windowsBuild = taskflyout::taskbar::GetWindowsBuildNumber();
    if (report.windowsBuild != taskflyout::taskbar::kValidatedWindowsBuild) {
        return HostCompatibility::UnsupportedWindowsBuild;
    }

    ModuleReference taskbarViewReference;
    if (!taskbarViewReference.Acquire(L"Taskbar.View.dll")) {
        return HostCompatibility::TaskbarViewMissing;
    }
    const HMODULE taskbarView = taskbarViewReference.get();

    std::array<wchar_t, 32768> modulePath{};
    const DWORD moduleLength = GetModuleFileNameW(
        taskbarView,
        modulePath.data(),
        static_cast<DWORD>(modulePath.size()));
    if (moduleLength == 0 || moduleLength >= modulePath.size()) {
        return HostCompatibility::TaskbarViewUnreadable;
    }

    PeFingerprint fingerprint;
    std::wstring detail;
    if (!taskflyout::taskbar::ReadPeFingerprint(
            std::wstring(modulePath.data(), moduleLength),
            fingerprint,
            detail)) {
        return HostCompatibility::TaskbarViewUnreadable;
    }

    report.timeDateStamp = fingerprint.timeDateStamp;
    report.sizeOfImage = fingerprint.sizeOfImage;
    report.checksum = fingerprint.checksum;
    if (!taskflyout::taskbar::IsAllowlisted(
            report.windowsBuild,
            fingerprint)) {
        return HostCompatibility::TaskbarViewNotAllowlisted;
    }

    MODULEINFO moduleInfo{};
    PeFingerprint loadedFingerprint;
    if (!ReadLoadedPeFingerprint(
            taskbarView,
            moduleInfo,
            loadedFingerprint)) {
        return HostCompatibility::TaskbarViewUnreadable;
    }
    report.timeDateStamp = loadedFingerprint.timeDateStamp;
    report.sizeOfImage = loadedFingerprint.sizeOfImage;
    report.checksum = loadedFingerprint.checksum;
    if (!taskflyout::taskbar::IsAllowlisted(
            report.windowsBuild,
            loadedFingerprint)) {
        return HostCompatibility::TaskbarViewNotAllowlisted;
    }

    return MatchesLoadedHookTarget(taskbarView, moduleInfo)
        ? HostCompatibility::Supported
        : HostCompatibility::TaskbarHookTargetMismatch;
}

}  // namespace

extern "C" __declspec(dllexport) std::uint32_t WINAPI
TaskFlyoutTaskbarHost_GetApiVersion() noexcept {
    return taskflyout::taskbar::kHostApiVersion;
}

extern "C" __declspec(dllexport) BOOL WINAPI
TaskFlyoutTaskbarHost_ProbeCurrentProcess(
    HostCompatibilityReport* report) noexcept {
    if (!report || report->size != sizeof(HostCompatibilityReport) ||
        report->apiVersion != taskflyout::taskbar::kHostApiVersion) {
        SetLastError(ERROR_INVALID_PARAMETER);
        return FALSE;
    }

    report->status = ProbeCurrentProcessImpl(*report);
    return TRUE;
}

extern "C" __declspec(dllexport) LRESULT CALLBACK
TaskFlyoutTaskbarHost_EntryHook(
    int code,
    WPARAM wParam,
    LPARAM lParam) noexcept {
    return CallNextHookEx(nullptr, code, wParam, lParam);
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, void*) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(instance);
    }
    return TRUE;
}
