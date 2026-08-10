#include "weather_view_model.h"

#include <cwctype>

namespace taskflyout::taskbar {
namespace {

constexpr bool IsHighSurrogate(wchar_t value) noexcept {
    return value >= 0xD800 && value <= 0xDBFF;
}

constexpr bool IsLowSurrogate(wchar_t value) noexcept {
    return value >= 0xDC00 && value <= 0xDFFF;
}

constexpr bool IsRejectedControl(wchar_t value) noexcept {
    return value < 0x20 || (value >= 0x7F && value <= 0x9F) ||
           value == 0x2028 || value == 0x2029;
}

constexpr bool IsBidirectionalControl(wchar_t value) noexcept {
    return value == 0x061C || value == 0x200E || value == 0x200F ||
           (value >= 0x202A && value <= 0x202E) ||
           (value >= 0x2066 && value <= 0x2069);
}

constexpr bool IsCollapsibleWhitespace(wchar_t value) noexcept {
    return value == L' ' || value == L'\t' || value == L'\r' ||
           value == L'\n' || value == 0x00A0;
}

}  // namespace

std::wstring SanitizeTaskbarText(
    std::wstring_view value,
    std::size_t maxUtf16Units) {
    if (maxUtf16Units == 0) {
        return {};
    }

    std::wstring result;
    result.reserve(value.size() < maxUtf16Units ? value.size() : maxUtf16Units);
    bool pendingSpace = false;

    for (std::size_t index = 0; index < value.size();) {
        const wchar_t current = value[index];
        if (IsCollapsibleWhitespace(current)) {
            pendingSpace = !result.empty();
            ++index;
            continue;
        }

        if (IsRejectedControl(current) || IsBidirectionalControl(current)) {
            ++index;
            continue;
        }

        std::size_t unitCount = 1;
        if (IsHighSurrogate(current)) {
            if (index + 1 >= value.size() ||
                !IsLowSurrogate(value[index + 1])) {
                ++index;
                continue;
            }
            unitCount = 2;
        } else if (IsLowSurrogate(current)) {
            ++index;
            continue;
        }

        const std::size_t requiredUnits =
            unitCount + ((pendingSpace && !result.empty()) ? 1u : 0u);
        if (result.size() + requiredUnits > maxUtf16Units) {
            break;
        }

        if (pendingSpace && !result.empty()) {
            result.push_back(L' ');
        }
        pendingSpace = false;
        result.append(value.substr(index, unitCount));
        index += unitCount;
    }

    return result;
}

WeatherViewModel CreateWeatherViewModel(
    std::wstring_view icon,
    std::wstring_view temperature,
    std::wstring_view condition) {
    WeatherViewModel result{
        SanitizeTaskbarText(icon, kWeatherIconMaxUtf16Units),
        SanitizeTaskbarText(
            temperature,
            kWeatherTemperatureMaxUtf16Units),
        SanitizeTaskbarText(condition, kWeatherConditionMaxUtf16Units),
    };

    if (result.icon.empty()) {
        result.icon = L"\u2601";
    }
    if (result.temperature.empty()) {
        result.temperature = L"--";
    }
    if (result.condition.empty()) {
        result.condition = L"Weather";
    }

    return result;
}

}  // namespace taskflyout::taskbar
