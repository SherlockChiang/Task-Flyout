#include "weather_xaml_view.h"

#include <Windows.h>

#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.UI.Text.h>
#include <winrt/Windows.UI.Xaml.h>
#include <winrt/Windows.UI.Xaml.Media.h>

namespace taskflyout::taskbar {
namespace {

using winrt::Windows::UI::Text::FontWeights;
using winrt::Windows::UI::Xaml::HorizontalAlignment;
using winrt::Windows::UI::Xaml::Thickness;
using winrt::Windows::UI::Xaml::VerticalAlignment;
using winrt::Windows::UI::Xaml::Controls::Border;
using winrt::Windows::UI::Xaml::Controls::Grid;
using winrt::Windows::UI::Xaml::Controls::Orientation;
using winrt::Windows::UI::Xaml::Controls::StackPanel;
using winrt::Windows::UI::Xaml::Controls::TextBlock;
using winrt::Windows::UI::Xaml::TextTrimming;

void SetTextIfChanged(
    const TextBlock& textBlock,
    const std::wstring& value) {
    if (textBlock.Text() != value) {
        textBlock.Text(value);
    }
}

}  // namespace

WeatherXamlView CreateWeatherXamlView(const WeatherViewModel& model) {
    WeatherXamlView view;
    view.root = Grid();
    view.root.Name(kWeatherHostName);
    view.root.MinWidth(112.0);
    view.root.MaxWidth(220.0);
    view.root.Height(40.0);
    view.root.HorizontalAlignment(HorizontalAlignment::Stretch);
    view.root.VerticalAlignment(VerticalAlignment::Stretch);
    view.root.IsHitTestVisible(false);

    Border inset;
    inset.Padding(Thickness{8.0, 0.0, 8.0, 0.0});
    inset.HorizontalAlignment(HorizontalAlignment::Stretch);
    inset.VerticalAlignment(VerticalAlignment::Stretch);

    StackPanel row;
    row.Orientation(Orientation::Horizontal);
    row.HorizontalAlignment(HorizontalAlignment::Center);
    row.VerticalAlignment(VerticalAlignment::Center);

    view.icon = TextBlock();
    view.icon.Name(kWeatherIconName);
    view.icon.FontFamily(
        winrt::Windows::UI::Xaml::Media::FontFamily(L"Segoe UI Emoji"));
    view.icon.FontSize(18.0);
    view.icon.Margin(Thickness{0.0, 0.0, 6.0, 0.0});
    view.icon.VerticalAlignment(VerticalAlignment::Center);

    view.temperature = TextBlock();
    view.temperature.Name(kWeatherTemperatureName);
    view.temperature.FontSize(12.0);
    view.temperature.FontWeight(FontWeights::SemiBold());
    view.temperature.VerticalAlignment(VerticalAlignment::Center);

    view.condition = TextBlock();
    view.condition.Name(kWeatherConditionName);
    view.condition.FontSize(12.0);
    view.condition.MaxWidth(112.0);
    view.condition.Margin(Thickness{6.0, 0.0, 0.0, 0.0});
    view.condition.Opacity(0.82);
    view.condition.TextTrimming(TextTrimming::CharacterEllipsis);
    view.condition.VerticalAlignment(VerticalAlignment::Center);

    row.Children().Append(view.icon);
    row.Children().Append(view.temperature);
    row.Children().Append(view.condition);
    inset.Child(row);
    view.root.Children().Append(inset);

    if (!UpdateWeatherXamlView(view, model) ||
        !ValidateWeatherXamlView(view)) {
        throw winrt::hresult_error(E_UNEXPECTED);
    }

    return view;
}

bool UpdateWeatherXamlView(
    const WeatherXamlView& view,
    const WeatherViewModel& model) noexcept {
    try {
        if (!ValidateWeatherXamlView(view)) {
            return false;
        }

        SetTextIfChanged(view.icon, model.icon);
        SetTextIfChanged(view.temperature, model.temperature);
        SetTextIfChanged(view.condition, model.condition);
        return true;
    } catch (...) {
        return false;
    }
}

bool ValidateWeatherXamlView(const WeatherXamlView& view) noexcept {
    try {
        return view.root && view.icon && view.temperature && view.condition &&
               view.root.Name() == kWeatherHostName &&
               view.icon.Name() == kWeatherIconName &&
               view.temperature.Name() == kWeatherTemperatureName &&
               view.condition.Name() == kWeatherConditionName &&
               !view.root.IsHitTestVisible() &&
               view.root.Children().Size() == 1;
    } catch (...) {
        return false;
    }
}

}  // namespace taskflyout::taskbar
