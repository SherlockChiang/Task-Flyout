#include "module_profile.h"

#include <Windows.h>

#include <cstdio>
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

    if (!passed) {
        return 1;
    }

    std::fputws(L"Taskbar module profile tests passed.\n", stdout);
    return 0;
}

