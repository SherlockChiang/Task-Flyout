#pragma once

#include "weather_view_model.h"

#include <winrt/Windows.UI.Xaml.Controls.h>

namespace taskflyout::taskbar {

inline constexpr wchar_t kWeatherHostName[] =
    L"TaskFlyoutNativeWeatherHost";
inline constexpr wchar_t kWeatherIconName[] =
    L"TaskFlyoutNativeWeatherIcon";
inline constexpr wchar_t kWeatherTemperatureName[] =
    L"TaskFlyoutNativeWeatherTemperature";
inline constexpr wchar_t kWeatherConditionName[] =
    L"TaskFlyoutNativeWeatherCondition";

struct WeatherXamlView {
    winrt::Windows::UI::Xaml::Controls::Grid root{nullptr};
    winrt::Windows::UI::Xaml::Controls::TextBlock icon{nullptr};
    winrt::Windows::UI::Xaml::Controls::TextBlock temperature{nullptr};
    winrt::Windows::UI::Xaml::Controls::TextBlock condition{nullptr};
};

// Must be called from the owner thread of an initialized Windows.UI.Xaml tree.
// The view is composed only of standard XAML primitives and performs no I/O.
WeatherXamlView CreateWeatherXamlView(const WeatherViewModel& model);

bool UpdateWeatherXamlView(
    const WeatherXamlView& view,
    const WeatherViewModel& model) noexcept;

bool ValidateWeatherXamlView(const WeatherXamlView& view) noexcept;

}  // namespace taskflyout::taskbar
