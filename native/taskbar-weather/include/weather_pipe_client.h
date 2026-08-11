#pragma once

#include "weather_view_model.h"

#include <Windows.h>

#include <cstdint>
#include <string>
#include <string_view>

namespace taskflyout::taskbar {

inline constexpr std::uint32_t kWeatherPipeMaxResponseBytes = 16u * 1024u;

constexpr bool IsWeatherPipeResponseLengthAllowed(
    const std::uint32_t length) noexcept {
    return length <= kWeatherPipeMaxResponseBytes;
}

enum class WeatherSnapshotFreshness : std::uint32_t {
    Fresh = 0,
    Invalid = 1,
    Stale = 2,
};

// Evaluates the protocol's UTC timestamp against FILETIME ticks (100 ns since
// 1601-01-01 UTC). Snapshots older than two hours or more than five minutes in
// the future fail closed.
WeatherSnapshotFreshness EvaluateWeatherSnapshotFreshness(
    std::wstring_view updatedUtc,
    std::uint64_t nowUtcFileTimeTicks) noexcept;

enum class WeatherPipeQueryStatus : std::uint32_t {
    Updated = 0,
    PipeUnavailable = 1,
    IoFailed = 2,
    InvalidResponse = 3,
    SnapshotUnavailable = 4,
    StaleSnapshot = 5,
    Cancelled = 6,
};

struct WeatherPipeQueryResult {
    WeatherPipeQueryStatus status =
        WeatherPipeQueryStatus::PipeUnavailable;
    WeatherViewModel model{};
};

// Parses one already-decoded protocol response at a caller-supplied UTC time.
// This contains no pipe or XAML work and keeps protocol policy unit-testable.
WeatherPipeQueryResult ParseWeatherPipeResponse(
    std::wstring_view responseJson,
    std::uint64_t nowUtcFileTimeTicks) noexcept;

// Performs one bounded, same-user/session snapshot request. It performs no
// XAML work and is intended for a worker thread, never the taskbar owner
// thread. The result owns only sanitized scalar strings.
WeatherPipeQueryResult QueryWeatherPipe(
    HANDLE cancellationEvent = nullptr) noexcept;

const wchar_t* WeatherPipeQueryStatusName(
    WeatherPipeQueryStatus status) noexcept;

enum class WeatherPipeActivationStatus : std::uint32_t {
    Queued = 0,
    PipeUnavailable = 1,
    IoFailed = 2,
    InvalidResponse = 3,
    Rejected = 4,
    Cancelled = 5,
};

WeatherPipeActivationStatus ParseWeatherOpenResponse(
    std::wstring_view responseJson) noexcept;

// Requests app-owned navigation without performing work on the XAML thread.
// The caller should invoke this only from the retained pipe worker.
WeatherPipeActivationStatus RequestWeatherOpen(
    HANDLE cancellationEvent = nullptr) noexcept;

const wchar_t* WeatherPipeActivationStatusName(
    WeatherPipeActivationStatus status) noexcept;

enum class WeatherPipeMountReadinessStatus : std::uint32_t {
    Accepted = 0,
    PipeUnavailable = 1,
    IoFailed = 2,
    InvalidResponse = 3,
    Rejected = 4,
    Cancelled = 5,
};

// Builds one fixed-schema request containing only controller-owned numeric
// identity and state. Zero identity/generation values fail closed to an empty
// request and are never sent.
std::string BuildMountReadinessRequest(
    std::uint32_t controllerNonce,
    std::uint64_t mountGeneration,
    bool ready);

WeatherPipeMountReadinessStatus ParseMountReadinessResponse(
    std::wstring_view responseJson) noexcept;

WeatherPipeMountReadinessStatus ReportMountReadiness(
    std::uint32_t controllerNonce,
    std::uint64_t mountGeneration,
    bool ready,
    HANDLE cancellationEvent = nullptr) noexcept;

const wchar_t* WeatherPipeMountReadinessStatusName(
    WeatherPipeMountReadinessStatus status) noexcept;

}  // namespace taskflyout::taskbar
