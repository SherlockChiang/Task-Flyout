#include "weather_pipe_client.h"

#include <Windows.h>

#include <winrt/base.h>

#include <array>
#include <cstdint>
#include <cstdio>
#include <string_view>
#include <utility>

namespace {

bool Expect(const bool condition, const wchar_t* message) {
    if (condition) {
        return true;
    }
    fwprintf(stderr, L"FAILED: %ls\n", message);
    return false;
}

std::uint64_t FileTimeTicks(
    const WORD year,
    const WORD month,
    const WORD day,
    const WORD hour,
    const WORD minute,
    const WORD second) {
    SYSTEMTIME systemTime{};
    systemTime.wYear = year;
    systemTime.wMonth = month;
    systemTime.wDay = day;
    systemTime.wHour = hour;
    systemTime.wMinute = minute;
    systemTime.wSecond = second;
    FILETIME fileTime{};
    if (!SystemTimeToFileTime(&systemTime, &fileTime)) {
        return 0;
    }
    ULARGE_INTEGER ticks{};
    ticks.LowPart = fileTime.dwLowDateTime;
    ticks.HighPart = fileTime.dwHighDateTime;
    return ticks.QuadPart;
}

}  // namespace

int wmain() {
    using namespace taskflyout::taskbar;

    bool passed = true;
    bool apartmentInitialized = false;
    try {
        winrt::init_apartment(winrt::apartment_type::multi_threaded);
        apartmentInitialized = true;
    } catch (...) {
        passed &= Expect(false, L"the JSON policy test needs an MTA apartment");
    }
    passed &= Expect(
        IsWeatherPipeResponseLengthAllowed(0),
        L"an empty response frame remains within the bounded policy");
    passed &= Expect(
        IsWeatherPipeResponseLengthAllowed(kWeatherPipeMaxResponseBytes),
        L"the protocol maximum response is accepted");
    passed &= Expect(
        !IsWeatherPipeResponseLengthAllowed(
            kWeatherPipeMaxResponseBytes + 1u),
        L"a response larger than the protocol maximum is rejected");

    const std::uint64_t nowTicks =
        FileTimeTicks(2026, 8, 10, 12, 0, 0);
    passed &= Expect(
        nowTicks != 0,
        L"the freshness test reference time should be valid");
    passed &= Expect(
        EvaluateWeatherSnapshotFreshness(
            L"2026-08-10T10:00:00Z",
            nowTicks) == WeatherSnapshotFreshness::Fresh,
        L"a snapshot exactly two hours old remains fresh");
    passed &= Expect(
        EvaluateWeatherSnapshotFreshness(
            L"2026-08-10T09:59:59.9999999Z",
            nowTicks) == WeatherSnapshotFreshness::Stale,
        L"a snapshot older than two hours is stale");
    passed &= Expect(
        EvaluateWeatherSnapshotFreshness(
            L"2026-08-10T12:05:00+00:00",
            nowTicks) == WeatherSnapshotFreshness::Fresh,
        L"the five-minute future tolerance is accepted");
    passed &= Expect(
        EvaluateWeatherSnapshotFreshness(
            L"2026-08-10T12:05:00.0000001Z",
            nowTicks) == WeatherSnapshotFreshness::Stale,
        L"a timestamp beyond the future tolerance is stale");
    passed &= Expect(
        EvaluateWeatherSnapshotFreshness(
            L"2026-08-10T20:00:00+08:00",
            nowTicks) == WeatherSnapshotFreshness::Fresh,
        L"an explicit timezone offset is normalized to UTC");
    passed &= Expect(
        EvaluateWeatherSnapshotFreshness(
            L"2026-08-10T12:00:00",
            nowTicks) == WeatherSnapshotFreshness::Invalid,
        L"a timestamp without a timezone is invalid");
    passed &= Expect(
        EvaluateWeatherSnapshotFreshness(
            L"2026-02-30T12:00:00Z",
            nowTicks) == WeatherSnapshotFreshness::Invalid,
        L"an impossible calendar date is invalid");

    if (apartmentInitialized) {
        const auto normal = ParseWeatherPipeResponse(
            LR"({"version":1,"status":"ok","snapshot":{"icon":"*","temperature":"26 C","description":"Cloudy","location":"Test","alert":"","updatedUtc":"2026-08-10T12:00:00+00:00"}})",
            nowTicks);
        passed &= Expect(
            normal.status == WeatherPipeQueryStatus::Updated,
            L"a valid current response should publish a model");
        passed &= Expect(
            normal.model.icon == L"*" &&
                normal.model.temperature == L"26 C" &&
                normal.model.condition == L"Cloudy",
            L"a normal response should use the weather description");

        const auto alert = ParseWeatherPipeResponse(
            LR"({"version":1,"status":"ok","snapshot":{"icon":"!","temperature":"26 C","description":"Cloudy","location":"Test","alert":"Storm\u202E warning","updatedUtc":"2026-08-10T12:00:00Z"}})",
            nowTicks);
        passed &= Expect(
            alert.status == WeatherPipeQueryStatus::Updated &&
                alert.model.icon == L"!" &&
                alert.model.condition == L"Storm warning",
            L"a sanitized alert should take precedence over description text");

        const auto emptyAfterSanitizingAlert = ParseWeatherPipeResponse(
            LR"({"version":1,"status":"ok","snapshot":{"icon":"!","temperature":"26 C","description":"Cloudy","alert":"\u202E\u2066","updatedUtc":"2026-08-10T12:00:00Z"}})",
            nowTicks);
        passed &= Expect(
            emptyAfterSanitizingAlert.status ==
                    WeatherPipeQueryStatus::Updated &&
                emptyAfterSanitizingAlert.model.condition == L"Cloudy",
            L"an alert emptied by sanitization should fall back to description");

        const auto unavailable = ParseWeatherPipeResponse(
            LR"({"version":1,"status":"unavailable"})",
            nowTicks);
        passed &= Expect(
            unavailable.status ==
                WeatherPipeQueryStatus::SnapshotUnavailable,
            L"an unavailable response should fail open without a model");

        const auto stale = ParseWeatherPipeResponse(
            LR"({"version":1,"status":"ok","snapshot":{"icon":"*","temperature":"26 C","description":"Cloudy","alert":"","updatedUtc":"2026-08-10T09:59:59Z"}})",
            nowTicks);
        passed &= Expect(
            stale.status == WeatherPipeQueryStatus::StaleSnapshot,
            L"an otherwise valid stale response should be rejected");

        const auto wrongVersion = ParseWeatherPipeResponse(
            LR"({"version":2,"status":"ok","snapshot":{}})",
            nowTicks);
        passed &= Expect(
            wrongVersion.status == WeatherPipeQueryStatus::InvalidResponse,
            L"a protocol version mismatch should be rejected");
        const auto wrongUnavailableVersion = ParseWeatherPipeResponse(
            LR"({"version":2,"status":"unavailable"})",
            nowTicks);
        passed &= Expect(
            wrongUnavailableVersion.status ==
                WeatherPipeQueryStatus::InvalidResponse,
            L"an unavailable status cannot override a version mismatch");
        const auto malformed = ParseWeatherPipeResponse(L"{not-json", nowTicks);
        passed &= Expect(
            malformed.status == WeatherPipeQueryStatus::InvalidResponse,
            L"malformed JSON should be rejected");
    }

    HANDLE cancellationEvent = CreateEventW(nullptr, TRUE, TRUE, nullptr);
    passed &= Expect(
        cancellationEvent != nullptr,
        L"the cancellation test event should be created");
    if (cancellationEvent) {
        const auto cancelled = QueryWeatherPipe(cancellationEvent);
        passed &= Expect(
            cancelled.status == WeatherPipeQueryStatus::Cancelled,
            L"a pre-signalled stop event should cancel before pipe discovery");
        CloseHandle(cancellationEvent);
    }

    constexpr std::array statuses{
        std::pair{WeatherPipeQueryStatus::Updated, L"updated"},
        std::pair{WeatherPipeQueryStatus::PipeUnavailable, L"pipe-unavailable"},
        std::pair{WeatherPipeQueryStatus::IoFailed, L"io-failed"},
        std::pair{WeatherPipeQueryStatus::InvalidResponse, L"invalid-response"},
        std::pair{
            WeatherPipeQueryStatus::SnapshotUnavailable,
            L"snapshot-unavailable"},
        std::pair{WeatherPipeQueryStatus::StaleSnapshot, L"stale-snapshot"},
        std::pair{WeatherPipeQueryStatus::Cancelled, L"cancelled"},
    };
    for (const auto& [status, expected] : statuses) {
        passed &= Expect(
            std::wstring_view(WeatherPipeQueryStatusName(status)) == expected,
            L"each query result status has a stable diagnostic name");
    }
    passed &= Expect(
        std::wstring_view(WeatherPipeQueryStatusName(
            static_cast<WeatherPipeQueryStatus>(99))) == L"unknown",
        L"unknown query result statuses fail closed to an explicit name");

    if (apartmentInitialized) {
        winrt::uninit_apartment();
    }
    if (!passed) {
        return 1;
    }
    fputws(L"Weather pipe client policy tests passed.\n", stdout);
    return 0;
}
