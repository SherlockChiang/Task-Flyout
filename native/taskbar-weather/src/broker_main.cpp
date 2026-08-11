#include "module_profile.h"
#include "taskbar_control_transport.h"

#include <Windows.h>

#include <fcntl.h>
#include <io.h>

#include <cstdint>
#include <cstdio>
#include <string>
#include <string_view>

namespace {

bool ConfigureUtf8TextOutput() noexcept {
    return _setmode(_fileno(stdout), _O_U8TEXT) != -1 &&
        _setmode(_fileno(stderr), _O_U8TEXT) != -1;
}

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
        L"       TaskFlyout.TaskbarBroker.exe start [--host <path>]\n"
        L"       TaskFlyout.TaskbarBroker.exe stop [--host <path>]\n"
        L"start/stop wait for a bounded host acknowledgement, then remove "
        L"the temporary hook.\n",
        stderr);
}

int PrintControl(
    const taskflyout::taskbar::HostControlCommand command,
    const std::wstring_view hostPath) {
    const auto result = taskflyout::taskbar::DispatchHostControl(
        command,
        hostPath);
    std::wprintf(
        L"{\"status\":\"%ls\",\"detail\":\"%ls\","
        L"\"command\":\"%ls\",\"processId\":%lu,\"threadId\":%lu,"
        L"\"messageId\":%u,\"acknowledgementMessageId\":%u,"
        L"\"controllerStatus\":\"%ls\",\"probeStatus\":\"%ls\"}\n",
        taskflyout::taskbar::HostControlDispatchStatusName(result.status),
        EscapeJson(result.detail).c_str(),
        command == taskflyout::taskbar::HostControlCommand::Start
            ? L"start"
            : L"stop",
        result.processId,
        result.threadId,
        result.messageId,
        result.acknowledgementMessageId,
        taskflyout::taskbar::HostControlAcknowledgementName(
            result.acknowledgement),
        taskflyout::taskbar::ProbeStatusName(result.probeStatus));
    return result.status ==
        taskflyout::taskbar::HostControlDispatchStatus::Acknowledged
        ? 0
        : 2;
}

}  // namespace

int wmain(int argc, wchar_t** argv) {
    // The app consumes redirected broker output as strict UTF-8. Explicit CRT
    // text modes keep non-ASCII install paths valid JSON on every system code
    // page while preserving wide-character formatting for console diagnostics.
    if (!ConfigureUtf8TextOutput()) {
        return 70;
    }

    if (argc < 2) {
        PrintUsage();
        return 64;
    }

    const std::wstring_view command(argv[1]);
    if (command == L"probe") {
        bool strict = false;
        if (argc == 3 && std::wstring_view(argv[2]) == L"--strict") {
            strict = true;
        } else if (argc != 2) {
            PrintUsage();
            return 64;
        }
        return PrintProbe(strict);
    }

    taskflyout::taskbar::HostControlCommand controlCommand;
    if (command == L"start") {
        controlCommand = taskflyout::taskbar::HostControlCommand::Start;
    } else if (command == L"stop") {
        controlCommand = taskflyout::taskbar::HostControlCommand::Stop;
    } else {
        PrintUsage();
        return 64;
    }

    std::wstring hostPath;
    if (argc == 2) {
        try {
            return PrintControl(controlCommand, hostPath);
        } catch (...) {
            std::fputws(L"{\"status\":\"internal-error\"}\n", stderr);
            return 70;
        }
    }
    if (argc == 4 && std::wstring_view(argv[2]) == L"--host") {
        hostPath = argv[3];
        try {
            return PrintControl(controlCommand, hostPath);
        } catch (...) {
            std::fputws(L"{\"status\":\"internal-error\"}\n", stderr);
            return 70;
        }
    }

    PrintUsage();
    return 64;
}
