#include "weather_pipe_client.h"

#include <winrt/Windows.Data.Json.h>

#include <Windows.h>
#include <sddl.h>

#include <array>
#include <cstddef>
#include <cstdint>
#include <limits>
#include <string>
#include <string_view>
#include <vector>

namespace taskflyout::taskbar {
namespace {

constexpr wchar_t kWeatherPipePrefix[] = L"TaskFlyout.Weather.v1";
constexpr DWORD kPipeConnectWaitMilliseconds = 250;
constexpr DWORD kPipeIoWaitMilliseconds = 750;
constexpr ULONGLONG kPipeQueryDeadlineMilliseconds = 2500;
constexpr std::uint64_t kFileTimeTicksPerSecond = 10'000'000u;
constexpr std::uint64_t kSnapshotMaxAgeTicks =
    2u * 60u * 60u * kFileTimeTicksPerSecond;
constexpr std::uint64_t kSnapshotFutureToleranceTicks =
    5u * 60u * kFileTimeTicksPerSecond;
constexpr char kSnapshotRequest[] =
    R"({"version":1,"command":"get-snapshot"})";
constexpr char kOpenWeatherRequest[] =
    R"({"version":1,"command":"open-weather"})";

class HandleGuard final {
public:
    explicit HandleGuard(const HANDLE handle = nullptr) noexcept
        : handle_(handle) {}

    HandleGuard(const HandleGuard&) = delete;
    HandleGuard& operator=(const HandleGuard&) = delete;

    ~HandleGuard() {
        if (handle_ && handle_ != INVALID_HANDLE_VALUE) {
            CloseHandle(handle_);
        }
    }

    HANDLE get() const noexcept {
        return handle_;
    }

    void Reset(const HANDLE handle) noexcept {
        if (handle_ && handle_ != INVALID_HANDLE_VALUE) {
            CloseHandle(handle_);
        }
        handle_ = handle;
    }

private:
    HANDLE handle_ = nullptr;
};

class LocalStringGuard final {
public:
    explicit LocalStringGuard(LPWSTR value) noexcept
        : value_(value) {}

    LocalStringGuard(const LocalStringGuard&) = delete;
    LocalStringGuard& operator=(const LocalStringGuard&) = delete;

    ~LocalStringGuard() {
        if (value_) {
            LocalFree(value_);
        }
    }

    LPCWSTR get() const noexcept {
        return value_;
    }

private:
    LPWSTR value_ = nullptr;
};

std::wstring BuildWeatherPipePath() {
    HandleGuard token;
    HANDLE tokenHandle = nullptr;
    if (!OpenProcessToken(
            GetCurrentProcess(),
            TOKEN_QUERY,
            &tokenHandle)) {
        return {};
    }
    token.Reset(tokenHandle);

    DWORD required = 0;
    GetTokenInformation(
        token.get(),
        TokenUser,
        nullptr,
        0,
        &required);
    if (required == 0) {
        return {};
    }

    std::vector<std::uint8_t> buffer(required);
    if (!GetTokenInformation(
            token.get(),
            TokenUser,
            buffer.data(),
            required,
            &required)) {
        return {};
    }

    auto* user = reinterpret_cast<TOKEN_USER*>(buffer.data());
    LPWSTR sid = nullptr;
    if (!ConvertSidToStringSidW(user->User.Sid, &sid) || !sid) {
        return {};
    }
    LocalStringGuard sidGuard(sid);

    DWORD sessionId = 0;
    const bool sessionRead = ProcessIdToSessionId(
        GetCurrentProcessId(),
        &sessionId) != FALSE;
    std::wstring path;
    if (sessionRead) {
        path = LR"(\\.\pipe\)";
        path += kWeatherPipePrefix;
        path += L'.';
        path += sidGuard.get();
        path += L'.';
        path += std::to_wstring(sessionId);
    }
    return path;
}

bool IsCancellationRequested(const HANDLE cancellationEvent) noexcept {
    return cancellationEvent &&
        WaitForSingleObject(cancellationEvent, 0) == WAIT_OBJECT_0;
}

DWORD RemainingQueryWaitMilliseconds(
    const ULONGLONG queryStartedTicks) noexcept {
    const ULONGLONG elapsed = GetTickCount64() - queryStartedTicks;
    if (elapsed >= kPipeQueryDeadlineMilliseconds) {
        return 0;
    }
    const ULONGLONG remaining =
        kPipeQueryDeadlineMilliseconds - elapsed;
    return static_cast<DWORD>(
        remaining < kPipeIoWaitMilliseconds
            ? remaining
            : kPipeIoWaitMilliseconds);
}

void CancelAndDrainOverlapped(
    HANDLE pipe,
    OVERLAPPED& overlapped) noexcept {
    CancelIoEx(pipe, &overlapped);
    DWORD ignored = 0;
    // OVERLAPPED is stack-owned. Drain the cancelled operation before it
    // leaves scope so the kernel cannot complete into reused stack memory.
    GetOverlappedResult(pipe, &overlapped, &ignored, TRUE);
}

bool CompleteOverlapped(
    HANDLE pipe,
    OVERLAPPED& overlapped,
    DWORD& transferred,
    const HANDLE cancellationEvent,
    const ULONGLONG queryStartedTicks) noexcept {
    const DWORD waitMilliseconds =
        RemainingQueryWaitMilliseconds(queryStartedTicks);
    if (waitMilliseconds == 0) {
        CancelAndDrainOverlapped(pipe, overlapped);
        return false;
    }

    std::array<HANDLE, 2> waitHandles{
        overlapped.hEvent,
        cancellationEvent};
    const DWORD handleCount = cancellationEvent ? 2u : 1u;
    const DWORD wait = WaitForMultipleObjects(
        handleCount,
        waitHandles.data(),
        FALSE,
        waitMilliseconds);
    if (wait != WAIT_OBJECT_0) {
        CancelAndDrainOverlapped(pipe, overlapped);
        return false;
    }
    return GetOverlappedResult(
               pipe,
               &overlapped,
               &transferred,
               FALSE) != FALSE;
}

bool WritePipeExact(
    HANDLE pipe,
    const std::uint8_t* data,
    const DWORD length,
    const HANDLE cancellationEvent,
    const ULONGLONG queryStartedTicks) noexcept {
    if (length == 0) {
        return true;
    }

    HandleGuard event(CreateEventW(nullptr, TRUE, FALSE, nullptr));
    if (!event.get()) {
        return false;
    }

    DWORD offset = 0;
    while (offset < length) {
        if (IsCancellationRequested(cancellationEvent) ||
            RemainingQueryWaitMilliseconds(queryStartedTicks) == 0) {
            return false;
        }
        OVERLAPPED overlapped{};
        overlapped.hEvent = event.get();
        ResetEvent(event.get());
        DWORD transferred = 0;
        if (WriteFile(
                pipe,
                data + offset,
                length - offset,
                &transferred,
                &overlapped)) {
            if (transferred == 0) {
                return false;
            }
        } else if (GetLastError() == ERROR_IO_PENDING) {
            if (!CompleteOverlapped(
                    pipe,
                    overlapped,
                    transferred,
                    cancellationEvent,
                    queryStartedTicks) ||
                transferred == 0) {
                return false;
            }
        } else {
            return false;
        }
        offset += transferred;
    }
    return true;
}

bool ReadPipeExact(
    HANDLE pipe,
    std::uint8_t* data,
    const DWORD length,
    const HANDLE cancellationEvent,
    const ULONGLONG queryStartedTicks) noexcept {
    if (length == 0) {
        return true;
    }

    HandleGuard event(CreateEventW(nullptr, TRUE, FALSE, nullptr));
    if (!event.get()) {
        return false;
    }

    DWORD offset = 0;
    while (offset < length) {
        if (IsCancellationRequested(cancellationEvent) ||
            RemainingQueryWaitMilliseconds(queryStartedTicks) == 0) {
            return false;
        }
        OVERLAPPED overlapped{};
        overlapped.hEvent = event.get();
        ResetEvent(event.get());
        DWORD transferred = 0;
        if (ReadFile(
                pipe,
                data + offset,
                length - offset,
                &transferred,
                &overlapped)) {
            if (transferred == 0) {
                return false;
            }
        } else if (GetLastError() == ERROR_IO_PENDING) {
            if (!CompleteOverlapped(
                    pipe,
                    overlapped,
                    transferred,
                    cancellationEvent,
                    queryStartedTicks) ||
                transferred == 0) {
                return false;
            }
        } else {
            return false;
        }
        offset += transferred;
    }
    return true;
}

bool WritePipeFrame(
    HANDLE pipe,
    const std::string_view payload,
    const HANDLE cancellationEvent,
    const ULONGLONG queryStartedTicks) noexcept {
    if (payload.size() > std::numeric_limits<std::uint32_t>::max()) {
        return false;
    }
    const auto length = static_cast<std::uint32_t>(payload.size());
    std::array<std::uint8_t, sizeof(length)> header{
        static_cast<std::uint8_t>(length & 0xFFu),
        static_cast<std::uint8_t>((length >> 8) & 0xFFu),
        static_cast<std::uint8_t>((length >> 16) & 0xFFu),
        static_cast<std::uint8_t>((length >> 24) & 0xFFu)};
    return WritePipeExact(
               pipe,
               header.data(),
               static_cast<DWORD>(header.size()),
               cancellationEvent,
               queryStartedTicks) &&
           WritePipeExact(
               pipe,
               reinterpret_cast<const std::uint8_t*>(payload.data()),
               static_cast<DWORD>(payload.size()),
               cancellationEvent,
               queryStartedTicks);
}

bool ReadPipeFrame(
    HANDLE pipe,
    std::vector<std::uint8_t>& payload,
    const HANDLE cancellationEvent,
    const ULONGLONG queryStartedTicks) noexcept {
    std::array<std::uint8_t, sizeof(std::uint32_t)> header{};
    if (!ReadPipeExact(
            pipe,
            header.data(),
            static_cast<DWORD>(header.size()),
            cancellationEvent,
            queryStartedTicks)) {
        return false;
    }
    const std::uint32_t length =
        static_cast<std::uint32_t>(header[0]) |
        (static_cast<std::uint32_t>(header[1]) << 8) |
        (static_cast<std::uint32_t>(header[2]) << 16) |
        (static_cast<std::uint32_t>(header[3]) << 24);
    if (!IsWeatherPipeResponseLengthAllowed(length)) {
        return false;
    }
    payload.resize(length);
    return length == 0 || ReadPipeExact(
        pipe,
        payload.data(),
        length,
        cancellationEvent,
        queryStartedTicks);
}

std::wstring Utf8ToWide(const std::vector<std::uint8_t>& bytes) {
    if (bytes.empty() || bytes.size() > INT_MAX) {
        return {};
    }
    const int byteCount = static_cast<int>(bytes.size());
    const int characterCount = MultiByteToWideChar(
        CP_UTF8,
        MB_ERR_INVALID_CHARS,
        reinterpret_cast<LPCCH>(bytes.data()),
        byteCount,
        nullptr,
        0);
    if (characterCount <= 0) {
        return {};
    }
    std::wstring result(characterCount, L'\0');
    if (!MultiByteToWideChar(
            CP_UTF8,
            MB_ERR_INVALID_CHARS,
            reinterpret_cast<LPCCH>(bytes.data()),
            byteCount,
            result.data(),
            characterCount)) {
        return {};
    }
    return result;
}

bool TryParseFixedUnsigned(
    const std::wstring_view value,
    const std::size_t offset,
    const std::size_t count,
    unsigned& result) noexcept {
    if (offset > value.size() || count > value.size() - offset) {
        return false;
    }

    unsigned parsed = 0;
    for (std::size_t index = 0; index < count; ++index) {
        const wchar_t character = value[offset + index];
        if (character < L'0' || character > L'9') {
            return false;
        }
        parsed = (parsed * 10u) +
            static_cast<unsigned>(character - L'0');
    }
    result = parsed;
    return true;
}

bool TryParseWeatherSnapshotUtc(
    const std::wstring_view value,
    std::uint64_t& utcFileTimeTicks) noexcept {
    if (value.size() < 20 ||
        value[4] != L'-' || value[7] != L'-' ||
        (value[10] != L'T' && value[10] != L't') ||
        value[13] != L':' || value[16] != L':') {
        return false;
    }

    unsigned year = 0;
    unsigned month = 0;
    unsigned day = 0;
    unsigned hour = 0;
    unsigned minute = 0;
    unsigned second = 0;
    if (!TryParseFixedUnsigned(value, 0, 4, year) ||
        !TryParseFixedUnsigned(value, 5, 2, month) ||
        !TryParseFixedUnsigned(value, 8, 2, day) ||
        !TryParseFixedUnsigned(value, 11, 2, hour) ||
        !TryParseFixedUnsigned(value, 14, 2, minute) ||
        !TryParseFixedUnsigned(value, 17, 2, second) ||
        year < 1601 || year > 9999) {
        return false;
    }

    SYSTEMTIME systemTime{};
    systemTime.wYear = static_cast<WORD>(year);
    systemTime.wMonth = static_cast<WORD>(month);
    systemTime.wDay = static_cast<WORD>(day);
    systemTime.wHour = static_cast<WORD>(hour);
    systemTime.wMinute = static_cast<WORD>(minute);
    systemTime.wSecond = static_cast<WORD>(second);

    FILETIME fileTime{};
    if (!SystemTimeToFileTime(&systemTime, &fileTime)) {
        return false;
    }
    ULARGE_INTEGER localTicks{};
    localTicks.LowPart = fileTime.dwLowDateTime;
    localTicks.HighPart = fileTime.dwHighDateTime;

    std::size_t position = 19;
    std::uint64_t fractionalTicks = 0;
    if (position < value.size() && value[position] == L'.') {
        ++position;
        std::size_t digitCount = 0;
        while (position < value.size() &&
               value[position] >= L'0' && value[position] <= L'9') {
            if (digitCount == 7) {
                return false;
            }
            fractionalTicks = (fractionalTicks * 10u) +
                static_cast<std::uint64_t>(value[position] - L'0');
            ++digitCount;
            ++position;
        }
        if (digitCount == 0) {
            return false;
        }
        while (digitCount < 7) {
            fractionalTicks *= 10u;
            ++digitCount;
        }
    }

    bool offsetPositive = true;
    unsigned offsetHours = 0;
    unsigned offsetMinutes = 0;
    if (position < value.size() &&
        (value[position] == L'Z' || value[position] == L'z')) {
        ++position;
    } else if (position < value.size() &&
               (value[position] == L'+' || value[position] == L'-')) {
        offsetPositive = value[position] == L'+';
        if (position + 6 > value.size() || value[position + 3] != L':' ||
            !TryParseFixedUnsigned(value, position + 1, 2, offsetHours) ||
            !TryParseFixedUnsigned(value, position + 4, 2, offsetMinutes)) {
            return false;
        }
        position += 6;
        if (offsetHours > 14 || offsetMinutes > 59 ||
            (offsetHours == 14 && offsetMinutes != 0)) {
            return false;
        }
    } else {
        return false;
    }
    if (position != value.size()) {
        return false;
    }

    if (localTicks.QuadPart >
        std::numeric_limits<std::uint64_t>::max() - fractionalTicks) {
        return false;
    }
    std::uint64_t parsedTicks = localTicks.QuadPart + fractionalTicks;
    const std::uint64_t offsetTicks =
        (static_cast<std::uint64_t>(offsetHours) * 60u + offsetMinutes) *
        60u * kFileTimeTicksPerSecond;
    if (offsetPositive) {
        if (parsedTicks < offsetTicks) {
            return false;
        }
        parsedTicks -= offsetTicks;
    } else {
        if (parsedTicks >
            std::numeric_limits<std::uint64_t>::max() - offsetTicks) {
            return false;
        }
        parsedTicks += offsetTicks;
    }

    utcFileTimeTicks = parsedTicks;
    return true;
}

std::wstring GetBoundedJsonString(
    const winrt::Windows::Data::Json::JsonObject& object,
    PCWSTR name) {
    constexpr std::size_t kMaxJsonFieldUnits = 512;
    const winrt::hstring value = object.GetNamedString(name, {});
    if (value.size() > kMaxJsonFieldUnits) {
        return {};
    }
    return std::wstring(value.data(), value.size());
}

enum class WeatherPipeExchangeStatus : std::uint32_t {
    Completed,
    PipeUnavailable,
    IoFailed,
    Cancelled,
};

struct WeatherPipeExchangeResult {
    WeatherPipeExchangeStatus status =
        WeatherPipeExchangeStatus::IoFailed;
    std::vector<std::uint8_t> response;
};

WeatherPipeExchangeResult ExchangeWeatherPipe(
    const std::string_view request,
    const HANDLE cancellationEvent) noexcept {
    WeatherPipeExchangeResult result;
    try {
        if (IsCancellationRequested(cancellationEvent)) {
            result.status = WeatherPipeExchangeStatus::Cancelled;
            return result;
        }
        const ULONGLONG queryStartedTicks = GetTickCount64();
        const std::wstring pipePath = BuildWeatherPipePath();
        if (pipePath.empty()) {
            result.status = WeatherPipeExchangeStatus::PipeUnavailable;
            return result;
        }
        const DWORD remainingQueryWait =
            RemainingQueryWaitMilliseconds(queryStartedTicks);
        if (remainingQueryWait == 0) {
            return result;
        }
        const DWORD connectWait =
            remainingQueryWait < kPipeConnectWaitMilliseconds
                ? remainingQueryWait
                : kPipeConnectWaitMilliseconds;
        if (!WaitNamedPipeW(pipePath.c_str(), connectWait)) {
            result.status = IsCancellationRequested(cancellationEvent)
                ? WeatherPipeExchangeStatus::Cancelled
                : WeatherPipeExchangeStatus::PipeUnavailable;
            return result;
        }
        if (IsCancellationRequested(cancellationEvent)) {
            result.status = WeatherPipeExchangeStatus::Cancelled;
            return result;
        }

        HandleGuard pipe(CreateFileW(
            pipePath.c_str(),
            GENERIC_READ | GENERIC_WRITE,
            0,
            nullptr,
            OPEN_EXISTING,
            FILE_FLAG_OVERLAPPED,
            nullptr));
        if (!pipe.get() || pipe.get() == INVALID_HANDLE_VALUE) {
            return result;
        }

        DWORD mode = PIPE_READMODE_BYTE;
        if (!SetNamedPipeHandleState(pipe.get(), &mode, nullptr, nullptr) ||
            !WritePipeFrame(
                pipe.get(),
                request,
                cancellationEvent,
                queryStartedTicks)) {
            result.status = IsCancellationRequested(cancellationEvent)
                ? WeatherPipeExchangeStatus::Cancelled
                : WeatherPipeExchangeStatus::IoFailed;
            return result;
        }

        if (!ReadPipeFrame(
                pipe.get(),
                result.response,
                cancellationEvent,
                queryStartedTicks)) {
            result.status = IsCancellationRequested(cancellationEvent)
                ? WeatherPipeExchangeStatus::Cancelled
                : WeatherPipeExchangeStatus::IoFailed;
            return result;
        }
        result.status = IsCancellationRequested(cancellationEvent)
            ? WeatherPipeExchangeStatus::Cancelled
            : WeatherPipeExchangeStatus::Completed;
        return result;
    } catch (...) {
        return result;
    }
}

}  // namespace

WeatherSnapshotFreshness EvaluateWeatherSnapshotFreshness(
    const std::wstring_view updatedUtc,
    const std::uint64_t nowUtcFileTimeTicks) noexcept {
    std::uint64_t updatedUtcFileTimeTicks = 0;
    if (!TryParseWeatherSnapshotUtc(
            updatedUtc,
            updatedUtcFileTimeTicks)) {
        return WeatherSnapshotFreshness::Invalid;
    }

    if (updatedUtcFileTimeTicks > nowUtcFileTimeTicks) {
        return updatedUtcFileTimeTicks - nowUtcFileTimeTicks <=
                kSnapshotFutureToleranceTicks
            ? WeatherSnapshotFreshness::Fresh
            : WeatherSnapshotFreshness::Stale;
    }
    return nowUtcFileTimeTicks - updatedUtcFileTimeTicks <=
            kSnapshotMaxAgeTicks
        ? WeatherSnapshotFreshness::Fresh
        : WeatherSnapshotFreshness::Stale;
}

WeatherPipeQueryResult ParseWeatherPipeResponse(
    const std::wstring_view responseJson,
    const std::uint64_t nowUtcFileTimeTicks) noexcept {
    WeatherPipeQueryResult result;
    result.status = WeatherPipeQueryStatus::InvalidResponse;
    if (responseJson.empty()) {
        return result;
    }

    try {
        const auto root =
            winrt::Windows::Data::Json::JsonObject::Parse(
                std::wstring(responseJson));
        if (root.GetNamedNumber(L"version", -1.0) != 1.0) {
            return result;
        }
        const winrt::hstring responseStatus =
            root.GetNamedString(L"status", {});
        if (responseStatus != L"ok") {
            result.status = responseStatus == L"unavailable"
                ? WeatherPipeQueryStatus::SnapshotUnavailable
                : WeatherPipeQueryStatus::InvalidResponse;
            return result;
        }
        const auto snapshot = root.GetNamedObject(L"snapshot", nullptr);
        if (!snapshot) {
            return result;
        }

        const std::wstring icon = GetBoundedJsonString(snapshot, L"icon");
        const std::wstring temperature =
            GetBoundedJsonString(snapshot, L"temperature");
        const std::wstring description =
            GetBoundedJsonString(snapshot, L"description");
        const std::wstring alert =
            GetBoundedJsonString(snapshot, L"alert");
        const std::wstring updatedUtc =
            GetBoundedJsonString(snapshot, L"updatedUtc");
        if (temperature.empty() || updatedUtc.empty()) {
            return result;
        }

        const WeatherSnapshotFreshness freshness =
            EvaluateWeatherSnapshotFreshness(
                updatedUtc,
                nowUtcFileTimeTicks);
        if (freshness != WeatherSnapshotFreshness::Fresh) {
            result.status = freshness == WeatherSnapshotFreshness::Stale
                ? WeatherPipeQueryStatus::StaleSnapshot
                : WeatherPipeQueryStatus::InvalidResponse;
            return result;
        }

        std::wstring effectiveCondition = SanitizeTaskbarText(
            alert,
            kWeatherConditionMaxUtf16Units);
        if (effectiveCondition.empty()) {
            effectiveCondition = description;
        }
        result.model = CreateWeatherViewModel(
            icon,
            temperature,
            effectiveCondition);
        result.status = WeatherPipeQueryStatus::Updated;
        return result;
    } catch (...) {
        return result;
    }
}

WeatherPipeQueryResult QueryWeatherPipe(
    const HANDLE cancellationEvent) noexcept {
    WeatherPipeQueryResult result;
    try {
        const WeatherPipeExchangeResult exchange = ExchangeWeatherPipe(
            kSnapshotRequest,
            cancellationEvent);
        switch (exchange.status) {
            case WeatherPipeExchangeStatus::PipeUnavailable:
                result.status = WeatherPipeQueryStatus::PipeUnavailable;
                return result;
            case WeatherPipeExchangeStatus::IoFailed:
                result.status = WeatherPipeQueryStatus::IoFailed;
                return result;
            case WeatherPipeExchangeStatus::Cancelled:
                result.status = WeatherPipeQueryStatus::Cancelled;
                return result;
            case WeatherPipeExchangeStatus::Completed:
                break;
        }

        const std::wstring responseJson = Utf8ToWide(exchange.response);
        if (responseJson.empty()) {
            result.status = WeatherPipeQueryStatus::InvalidResponse;
            return result;
        }
        FILETIME nowFileTime{};
        GetSystemTimeAsFileTime(&nowFileTime);
        ULARGE_INTEGER nowTicks{};
        nowTicks.LowPart = nowFileTime.dwLowDateTime;
        nowTicks.HighPart = nowFileTime.dwHighDateTime;
        return ParseWeatherPipeResponse(
            responseJson,
            nowTicks.QuadPart);
    } catch (...) {
        result.status = WeatherPipeQueryStatus::InvalidResponse;
        return result;
    }
}

WeatherPipeActivationStatus ParseWeatherOpenResponse(
    const std::wstring_view responseJson) noexcept {
    if (responseJson.empty()) {
        return WeatherPipeActivationStatus::InvalidResponse;
    }
    try {
        const auto root =
            winrt::Windows::Data::Json::JsonObject::Parse(
                std::wstring(responseJson));
        if (root.GetNamedNumber(L"version", -1.0) != 1.0) {
            return WeatherPipeActivationStatus::InvalidResponse;
        }
        const winrt::hstring status = root.GetNamedString(L"status", {});
        if (status == L"ok") {
            return WeatherPipeActivationStatus::Queued;
        }
        if (status == L"unavailable") {
            return WeatherPipeActivationStatus::Rejected;
        }
    } catch (...) {
    }
    return WeatherPipeActivationStatus::InvalidResponse;
}

WeatherPipeActivationStatus RequestWeatherOpen(
    const HANDLE cancellationEvent) noexcept {
    try {
        const WeatherPipeExchangeResult exchange = ExchangeWeatherPipe(
            kOpenWeatherRequest,
            cancellationEvent);
        switch (exchange.status) {
            case WeatherPipeExchangeStatus::PipeUnavailable:
                return WeatherPipeActivationStatus::PipeUnavailable;
            case WeatherPipeExchangeStatus::IoFailed:
                return WeatherPipeActivationStatus::IoFailed;
            case WeatherPipeExchangeStatus::Cancelled:
                return WeatherPipeActivationStatus::Cancelled;
            case WeatherPipeExchangeStatus::Completed:
                break;
        }
        const std::wstring responseJson = Utf8ToWide(exchange.response);
        if (responseJson.empty()) {
            return WeatherPipeActivationStatus::InvalidResponse;
        }
        return ParseWeatherOpenResponse(responseJson);
    } catch (...) {
        return WeatherPipeActivationStatus::InvalidResponse;
    }
}

const wchar_t* WeatherPipeQueryStatusName(
    const WeatherPipeQueryStatus status) noexcept {
    switch (status) {
        case WeatherPipeQueryStatus::Updated:
            return L"updated";
        case WeatherPipeQueryStatus::PipeUnavailable:
            return L"pipe-unavailable";
        case WeatherPipeQueryStatus::IoFailed:
            return L"io-failed";
        case WeatherPipeQueryStatus::InvalidResponse:
            return L"invalid-response";
        case WeatherPipeQueryStatus::SnapshotUnavailable:
            return L"snapshot-unavailable";
        case WeatherPipeQueryStatus::StaleSnapshot:
            return L"stale-snapshot";
        case WeatherPipeQueryStatus::Cancelled:
            return L"cancelled";
    }
    return L"unknown";
}

const wchar_t* WeatherPipeActivationStatusName(
    const WeatherPipeActivationStatus status) noexcept {
    switch (status) {
        case WeatherPipeActivationStatus::Queued:
            return L"queued";
        case WeatherPipeActivationStatus::PipeUnavailable:
            return L"pipe-unavailable";
        case WeatherPipeActivationStatus::IoFailed:
            return L"io-failed";
        case WeatherPipeActivationStatus::InvalidResponse:
            return L"invalid-response";
        case WeatherPipeActivationStatus::Rejected:
            return L"rejected";
        case WeatherPipeActivationStatus::Cancelled:
            return L"cancelled";
    }
    return L"unknown";
}

}  // namespace taskflyout::taskbar
