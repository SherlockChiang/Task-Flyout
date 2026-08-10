#pragma once

#include <cstddef>
#include <string>
#include <string_view>

namespace taskflyout::taskbar {

inline constexpr std::size_t kWeatherIconMaxUtf16Units = 8;
inline constexpr std::size_t kWeatherTemperatureMaxUtf16Units = 16;
inline constexpr std::size_t kWeatherConditionMaxUtf16Units = 64;

struct WeatherViewModel {
    std::wstring icon;
    std::wstring temperature;
    std::wstring condition;
};

std::wstring SanitizeTaskbarText(
    std::wstring_view value,
    std::size_t maxUtf16Units);

WeatherViewModel CreateWeatherViewModel(
    std::wstring_view icon,
    std::wstring_view temperature,
    std::wstring_view condition);

}  // namespace taskflyout::taskbar
