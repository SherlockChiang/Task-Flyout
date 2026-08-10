#include "weather_view_model.h"

#include <iostream>
#include <string>

namespace {

bool ExpectEqual(
    std::wstring_view actual,
    std::wstring_view expected,
    const wchar_t* scenario) {
    if (actual == expected) {
        return true;
    }

    std::wcerr << L"FAILED " << scenario << L": expected [" << expected
               << L"] actual [" << actual << L"]\n";
    return false;
}

}  // namespace

int wmain() {
    bool passed = true;

    passed &= ExpectEqual(
        taskflyout::taskbar::SanitizeTaskbarText(
            L"  Light\r\n  rain\t now  ",
            64),
        L"Light rain now",
        L"whitespace collapse");

    passed &= ExpectEqual(
        taskflyout::taskbar::SanitizeTaskbarText(
            L"Safe\u202Etxt\u2066!\u2029next",
            64),
        L"Safetxt!next",
        L"bidi and line separator removal");

    const std::wstring emoji = L"\U0001F324\uFE0F";
    passed &= ExpectEqual(
        taskflyout::taskbar::SanitizeTaskbarText(emoji, 3),
        emoji,
        L"surrogate and variation selector preservation");
    passed &= ExpectEqual(
        taskflyout::taskbar::SanitizeTaskbarText(emoji, 1),
        L"",
        L"surrogate pair is never split");

    std::wstring invalidSurrogate;
    invalidSurrogate.push_back(static_cast<wchar_t>(0xD83C));
    invalidSurrogate += L"ok";
    passed &= ExpectEqual(
        taskflyout::taskbar::SanitizeTaskbarText(invalidSurrogate, 8),
        L"ok",
        L"isolated surrogate removal");

    const auto bounded = taskflyout::taskbar::CreateWeatherViewModel(
        L"\U0001F324\uFE0F extra",
        L"  26 C  ",
        L"Partly cloudy with a deliberately long condition that must be bounded before Explorer sees it");
    passed &= bounded.icon.size() <=
              taskflyout::taskbar::kWeatherIconMaxUtf16Units;
    passed &= bounded.temperature == L"26 C";
    passed &= bounded.condition.size() <=
              taskflyout::taskbar::kWeatherConditionMaxUtf16Units;

    const auto defaults = taskflyout::taskbar::CreateWeatherViewModel(
        L"\u202E",
        L"\r\n",
        L"\u2066");
    passed &= defaults.icon == L"\u2601";
    passed &= defaults.temperature == L"--";
    passed &= defaults.condition == L"Weather";

    if (!passed) {
        return 1;
    }

    std::wcout << L"weather view model tests passed\n";
    return 0;
}
