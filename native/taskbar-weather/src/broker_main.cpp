#include "module_profile.h"

#include <Windows.h>

#include <cstdint>
#include <cstdio>
#include <string>
#include <string_view>

namespace {

std::wstring EscapeJson(std::wstring_view value) {
    std::wstring escaped;
    escaped.reserve(value.size() + 16);
    for (const wchar_t character : value) {
        switch (character) {
            case L'\\':
                escaped += L"\\\\";
                break;
            case L'\"':
                escaped += L"\\\"";
                break;
            case L'\r':
                escaped += L"\\r";
                break;
            case L'\n':
                escaped += L"\\n";
                break;
            case L'\t':
                escaped += L"\\t";
                break;
            default:
                if (character >= 0x20) {
                    escaped.push_back(character);
                }
                break;
        }
    }
    return escaped;
}

int PrintProbe(bool strictExitCode) {
    const taskflyout::taskbar::ProbeResult result =
        taskflyout::taskbar::ProbePrimaryTaskbar();
    const auto& fingerprint = result.module.fingerprint;
    const std::wstring status =
        taskflyout::taskbar::ProbeStatusName(result.status);

    std::wprintf(
        L"{\"status\":\"%ls\",\"detail\":\"%ls\","
        L"\"windowsBuild\":%u,\"processId\":%lu,\"threadId\":%lu,"
        L"\"sessionId\":%lu,\"explorerPath\":\"%ls\","
        L"\"taskbarViewPath\":\"%ls\",\"machine\":%u,"
        L"\"timeDateStamp\":%u,\"sizeOfImage\":%u,\"checksum\":%u}\n",
        status.c_str(),
        EscapeJson(result.detail).c_str(),
        result.windowsBuild,
        result.module.processId,
        result.module.threadId,
        result.module.sessionId,
        EscapeJson(result.module.explorerPath).c_str(),
        EscapeJson(result.module.taskbarViewPath).c_str(),
        fingerprint.machine,
        fingerprint.timeDateStamp,
        fingerprint.sizeOfImage,
        fingerprint.checksum);

    if (!strictExitCode ||
        result.status == taskflyout::taskbar::ProbeStatus::Supported) {
        return 0;
    }
    return 2;
}

void PrintUsage() {
    std::fputws(
        L"Usage: TaskFlyout.TaskbarBroker.exe probe [--strict]\n"
        L"This build performs compatibility probing only and never injects.\n",
        stderr);
}

}  // namespace

int wmain(int argc, wchar_t** argv) {
    if (argc < 2 || std::wstring_view(argv[1]) != L"probe") {
        PrintUsage();
        return 64;
    }

    bool strict = false;
    if (argc == 3 && std::wstring_view(argv[2]) == L"--strict") {
        strict = true;
    } else if (argc != 2) {
        PrintUsage();
        return 64;
    }

    return PrintProbe(strict);
}

