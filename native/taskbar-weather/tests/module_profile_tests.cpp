#include "module_profile.h"
#include "taskbar_host_contract.h"

#include <Windows.h>

#include <array>
#include <cstdint>
#include <cstdio>
#include <limits>
#include <string>

namespace {

bool Expect(bool condition, const wchar_t* message) {
    if (condition) {
        return true;
    }
    std::fwprintf(stderr, L"FAILED: %ls\n", message);
    return false;
}

}  // namespace

int wmain(int argc, wchar_t** argv) {
    bool passed = true;

    taskflyout::taskbar::PeFingerprint allowlisted{
        IMAGE_FILE_MACHINE_AMD64,
        taskflyout::taskbar::kValidatedTaskbarViewTimestamp,
        taskflyout::taskbar::kValidatedTaskbarViewImageSize,
        taskflyout::taskbar::kValidatedTaskbarViewChecksum,
    };
    passed &= Expect(
        taskflyout::taskbar::IsAllowlisted(
            taskflyout::taskbar::kValidatedWindowsBuild,
            allowlisted),
        L"known taskbar profile should be accepted");

    taskflyout::taskbar::PeFingerprint mutated = allowlisted;
    mutated.checksum++;
    passed &= Expect(
        !taskflyout::taskbar::IsAllowlisted(
            taskflyout::taskbar::kValidatedWindowsBuild,
            mutated),
        L"mutated taskbar profile should be rejected");
    passed &= Expect(
        !taskflyout::taskbar::IsAllowlisted(
            taskflyout::taskbar::kValidatedWindowsBuild + 1,
            allowlisted),
        L"unknown Windows build should be rejected");

    passed &= Expect(
        taskflyout::taskbar::MatchesTaskbarFrameHookPrologue(
            taskflyout::taskbar::kTaskbarFrameLayoutHookPrologue),
        L"known hook prologue should be accepted");
    std::array<std::uint8_t,
               taskflyout::taskbar::kTaskbarFrameLayoutHookPrologue.size()>
        mutatedPrologue =
            taskflyout::taskbar::kTaskbarFrameLayoutHookPrologue;
    mutatedPrologue[0] ^= 0x01;
    passed &= Expect(
        !taskflyout::taskbar::MatchesTaskbarFrameHookPrologue(
            mutatedPrologue),
        L"mutated hook prologue should be rejected");

    if (argc != 2) {
        std::fputws(L"FAILED: host DLL path argument is required\n", stderr);
        return 1;
    }

    taskflyout::taskbar::PeFingerprint hostFingerprint;
    std::wstring detail;
    passed &= Expect(
        taskflyout::taskbar::ReadPeFingerprint(
            argv[1],
            hostFingerprint,
            detail),
        L"built host DLL should have a valid PE header");
    passed &= Expect(
        hostFingerprint.machine == IMAGE_FILE_MACHINE_AMD64,
        L"built host DLL should target x64");
    passed &= Expect(
        hostFingerprint.timeDateStamp != 0,
        L"built host DLL should have a PE timestamp");
    passed &= Expect(
        hostFingerprint.sizeOfImage != 0,
        L"built host DLL should have a non-empty image");

    std::array<std::uint8_t, 2> dosSignature{};
    passed &= Expect(
        taskflyout::taskbar::ReadPeBytesAtRva(
            argv[1],
            0,
            dosSignature,
            detail),
        L"built host DLL should expose its PE header range");
    passed &= Expect(
        dosSignature[0] == 'M' && dosSignature[1] == 'Z',
        L"RVA zero should map to the DOS signature");

    std::array<std::uint8_t, 2> invalidRange{};
    passed &= Expect(
        !taskflyout::taskbar::ReadPeBytesAtRva(
            argv[1],
            0xFFFFFFFEU,
            invalidRange,
            detail),
        L"overflowing PE range should be rejected");
    passed &= Expect(
        detail == L"pe-range-overflow",
        L"overflowing PE range should fail at the overflow gate");

    const HMODULE host = LoadLibraryW(argv[1]);
    passed &= Expect(host != nullptr, L"built host DLL should load in tests");
    if (host) {
        using GetApiVersionFn = std::uint32_t(WINAPI*)() noexcept;
        using ProbeCurrentProcessFn = BOOL(WINAPI*)(
            taskflyout::taskbar::HostCompatibilityReport*) noexcept;

        const auto getApiVersion = reinterpret_cast<GetApiVersionFn>(
            GetProcAddress(host, "TaskFlyoutTaskbarHost_GetApiVersion"));
        const auto probeCurrentProcess =
            reinterpret_cast<ProbeCurrentProcessFn>(GetProcAddress(
                host,
                "TaskFlyoutTaskbarHost_ProbeCurrentProcess"));
        passed &= Expect(
            getApiVersion != nullptr && probeCurrentProcess != nullptr,
            L"host ABI exports should be present");
        if (getApiVersion && probeCurrentProcess) {
            passed &= Expect(
                getApiVersion() == taskflyout::taskbar::kHostApiVersion,
                L"host ABI version should match the contract");

            taskflyout::taskbar::HostCompatibilityReport invalidReport{};
            invalidReport.size = sizeof(invalidReport) - 1;
            invalidReport.apiVersion =
                taskflyout::taskbar::kHostApiVersion;
            SetLastError(ERROR_SUCCESS);
            passed &= Expect(
                !probeCurrentProcess(&invalidReport) &&
                    GetLastError() == ERROR_INVALID_PARAMETER,
                L"host probe should reject a mismatched report size");

            taskflyout::taskbar::HostCompatibilityReport report{};
            report.size = sizeof(report);
            report.apiVersion = taskflyout::taskbar::kHostApiVersion;
            passed &= Expect(
                probeCurrentProcess(&report) != FALSE,
                L"host probe should accept the current ABI contract");
            passed &= Expect(
                report.status ==
                    taskflyout::taskbar::HostCompatibility::UnsupportedProcess,
                L"host probe should fail closed outside Explorer");
        }

        const FARPROC apiAddress = GetProcAddress(
            host,
            "TaskFlyoutTaskbarHost_GetApiVersion");
        if (apiAddress) {
            const auto exportRva =
                reinterpret_cast<std::uintptr_t>(apiAddress) -
                reinterpret_cast<std::uintptr_t>(host);
            passed &= Expect(
                exportRva <= std::numeric_limits<std::uint32_t>::max(),
                L"host export should have a 32-bit PE RVA");
            if (exportRva <= std::numeric_limits<std::uint32_t>::max()) {
                std::array<std::uint8_t, 1> exportByte{};
                passed &= Expect(
                    taskflyout::taskbar::ReadPeBytesAtRva(
                        argv[1],
                        static_cast<std::uint32_t>(exportRva),
                        exportByte,
                        detail),
                    L"PE reader should map an exported code RVA");
                passed &= Expect(
                    exportByte[0] ==
                        *reinterpret_cast<const std::uint8_t*>(apiAddress),
                    L"file-backed RVA byte should match the loaded export");
            }
        }

        FreeLibrary(host);
    }

    if (!passed) {
        return 1;
    }

    std::fputws(L"Taskbar module profile tests passed.\n", stdout);
    return 0;
}
