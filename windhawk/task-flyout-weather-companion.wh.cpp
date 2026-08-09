// ==WindhawkMod==
// @id              task-flyout-weather-companion
// @name            Task Flyout Weather Companion (Experimental)
// @name:zh-CN      Task Flyout 天气伴侣（实验性）
// @description     Replaces the contents of the Windows 11 Widgets taskbar entry while preserving its native shell and Windhawk styling.
// @description:zh-CN 在保留 Windows 11 原生 Widgets 外壳和 Windhawk 样式的前提下替换任务栏入口内容。
// @version         0.2.0
// @author          Task Flyout contributors
// @homepage        https://github.com/SherlockChiang/Task-Flyout
// @include         explorer.exe
// @architecture    x86-64
// @compilerOptions -lole32 -loleaut32 -ladvapi32 -lruntimeobject
// ==/WindhawkMod==

// Source code is published under the GNU General Public License v3.0.

// ==WindhawkModReadme==
/*
# Task Flyout Weather Companion (experimental)

This proof of concept runs inside Explorer and overlays a bounded Task Flyout
weather snapshot in the existing Windows 11 Widgets taskbar button. The native
outer button, hover states, accessibility target, and Windhawk theme styling
remain owned by Explorer. The companion app must have its experimental bridge
switch enabled; otherwise the native content is restored.

The proof of concept is intentionally disabled by default and restricted to the
Windows 11 25H2 build family used for development. The optional static preview
is disabled by default and exists only for shell tree validation. If the
expected symbol or XAML tree cannot be found, it makes no changes.
*/
// ==/WindhawkModReadme==

// ==WindhawkModSettings==
/*
- enabled: false
  $name: Enable companion injection
  $name:zh-CN: 启用伴侣注入
  $description: >-
    Experimental. Requires the Task Flyout app's companion bridge switch. The
    native outer Widgets button and its theme-owned shell remain intact.
  $description:zh-CN: >-
    实验性功能。需要在 Task Flyout 中开启伴侣桥接开关，保留原生 Widgets
    外壳和主题样式。
- allowStaticPreview: false
  $name: Allow static preview (diagnostics)
  $name:zh-CN: 允许静态预览（诊断）
  $description: >-
    Shell validation only. This bypasses the app bridge and must stay off for
    normal weather operation.
  $description:zh-CN: >-
    仅用于外壳验证。此选项绕过应用桥接，正常天气使用时应保持关闭。
- previewText: "--°  Task Flyout"
  $name: Static preview text
  $name:zh-CN: 静态预览文本
  $description: Used only when the diagnostics-only static preview is enabled.
  $description:zh-CN: 仅在启用诊断用静态预览时使用。
*/
// ==/WindhawkModSettings==

#include <windhawk_utils.h>

#include <sddl.h>

#undef GetCurrentTime

#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Data.Json.h>
#include <winrt/Windows.UI.Xaml.Automation.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Markup.h>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <winrt/base.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <cstdint>
#include <mutex>
#include <new>
#include <optional>
#include <string>
#include <unordered_set>
#include <vector>

using namespace winrt::Windows::UI::Xaml;

namespace {

constexpr DWORD kValidatedWindowsBuild = 26200;
constexpr DWORD kValidatedTaskbarViewTimestamp = 0x6A3CD591;
constexpr DWORD kValidatedTaskbarViewImageSize = 0x0098B000;
constexpr DWORD kValidatedTaskbarViewChecksum = 0x00985653;
constexpr size_t kTaskbarFrameIInspectableSlot = 3;
constexpr wchar_t kWidgetsClassName[] = L"Taskbar.AugmentedEntryPointButton";
constexpr wchar_t kWidgetsElementName[] = L"AugmentedEntryPointButton";
constexpr wchar_t kWidgetsAutomationId[] = L"WidgetsButton";
constexpr wchar_t kContentGridName[] = L"AugmentedEntryPointContentGrid";
constexpr wchar_t kHostName[] = L"TaskFlyoutWeatherHost";
constexpr wchar_t kWeatherIconName[] = L"TaskFlyoutWeatherIcon";
constexpr wchar_t kPreviewTextName[] = L"TaskFlyoutPreviewText";
constexpr wchar_t kStateSchemaKey[] = L"state-schema";
constexpr wchar_t kWeatherPipePrefix[] = L"TaskFlyout.Weather.v1";
constexpr int kMaxTreeDepth = 18;
constexpr int kMaxVisitedElements = 768;
constexpr size_t kMaxPreviewTextLength = 48;
constexpr DWORD kOwnerThreadDispatchWaitMs = 3000;
constexpr DWORD kPipeConnectWaitMs = 250;
constexpr DWORD kPipeIoWaitMs = 500;
constexpr DWORD kWorkerPollIntervalMs = 15000;
constexpr DWORD kWorkerStopWaitMs = 6000;
constexpr size_t kMaxWeatherFieldLength = 128;

std::atomic<bool> g_enabled;
std::atomic<bool> g_allowStaticPreview;
std::atomic<bool> g_unloading;
std::atomic<bool> g_taskbarViewDllLoaded;
thread_local bool g_updatingXaml;

std::mutex g_settingsMutex;
std::wstring g_previewText = L"--\x00B0  Task Flyout";

struct WeatherSnapshot {
    std::wstring icon;
    std::wstring temperature;
    std::wstring description;
    std::wstring location;
    std::wstring alert;
    std::wstring updatedUtc;
};

std::mutex g_snapshotMutex;
std::optional<WeatherSnapshot> g_weatherSnapshot;
std::mutex g_workerMutex;
std::atomic<bool> g_workerStop;
HANDLE g_workerWakeEvent = nullptr;
HANDLE g_workerThread = nullptr;

void ApplySettingsToTaskbarWindows(bool triggerLayout);

struct OriginalChildState {
    winrt::weak_ref<UIElement> element;
    bool opacityIsLocal;
    double opacity;
    bool isHitTestVisibleIsLocal;
    bool isHitTestVisible;
};

struct InjectionState {
    winrt::weak_ref<Controls::Grid> contentGrid;
    winrt::weak_ref<FrameworkElement> host;
    std::vector<OriginalChildState> originalChildren;
    DWORD ownerThreadId;
};

struct XamlUpdateGuard {
    XamlUpdateGuard() : acquired(!g_updatingXaml) {
        if (acquired) {
            g_updatingXaml = true;
        }
    }

    ~XamlUpdateGuard() {
        if (acquired) {
            g_updatingXaml = false;
        }
    }

    explicit operator bool() const {
        return acquired;
    }

    bool acquired;
};

// XAML controls are inspected and mutated only from the taskbar UI hook. The
// mutex protects bookkeeping against multiple taskbar UI threads and lets the
// unload path check whether every visible entry acknowledged restoration.
std::mutex g_injectionsMutex;
std::vector<InjectionState> g_injections;
std::mutex g_threadDispatchMutex;
std::mutex g_pendingDispatchMutex;
std::unordered_set<DWORD> g_pendingDispatchThreads;

struct RtlOsVersionInfo {
    ULONG size;
    ULONG majorVersion;
    ULONG minorVersion;
    ULONG buildNumber;
    ULONG platformId;
    WCHAR servicePack[128];
};

DWORD GetWindowsBuildNumber() {
    HMODULE ntdll = GetModuleHandleW(L"ntdll.dll");
    if (!ntdll) {
        return 0;
    }

    using RtlGetVersion_t = LONG(WINAPI*)(RtlOsVersionInfo*);
    auto rtlGetVersion = reinterpret_cast<RtlGetVersion_t>(
        GetProcAddress(ntdll, "RtlGetVersion"));
    if (!rtlGetVersion) {
        return 0;
    }

    RtlOsVersionInfo version{};
    version.size = sizeof(version);
    if (rtlGetVersion(&version) < 0) {
        return 0;
    }

    return version.buildNumber;
}

std::wstring SanitizePreviewText(PCWSTR value) {
    std::wstring result = value ? value : L"";
    if (result.size() > kMaxPreviewTextLength) {
        result.resize(kMaxPreviewTextLength);
        if (!result.empty() && result.back() >= 0xD800 &&
            result.back() <= 0xDBFF) {
            result.pop_back();
        }
    }

    for (wchar_t& character : result) {
        if (character < L' ' || character == 0x7F ||
            character == 0x2028 || character == 0x2029 ||
            character == 0x061C || character == 0x200E ||
            character == 0x200F ||
            (character >= 0x202A && character <= 0x202E) ||
            (character >= 0x2066 && character <= 0x2069)) {
            character = L' ';
        }
    }

    if (result.empty()) {
        result = L"--\x00B0  Task Flyout";
    }

    return result;
}

void LoadSettings() {
    g_enabled = Wh_GetIntSetting(L"enabled") != 0;
    g_allowStaticPreview = Wh_GetIntSetting(L"allowStaticPreview") != 0;

    PCWSTR previewText = Wh_GetStringSetting(L"previewText");
    std::wstring sanitized = SanitizePreviewText(previewText);
    Wh_FreeStringSetting(previewText);

    std::lock_guard lock(g_settingsMutex);
    g_previewText = std::move(sanitized);
}

std::wstring GetPreviewText() {
    std::lock_guard lock(g_settingsMutex);
    return g_previewText;
}

bool SameObject(DependencyObject const& left, DependencyObject const& right) {
    return left && right && winrt::get_abi(left) == winrt::get_abi(right);
}

FrameworkElement FindDescendantByName(DependencyObject const& root,
                                      PCWSTR name,
                                      int depth = 0) {
    if (!root || depth >= kMaxTreeDepth) {
        return nullptr;
    }

    int childCount = Media::VisualTreeHelper::GetChildrenCount(root);
    for (int index = 0; index < childCount; index++) {
        auto child = Media::VisualTreeHelper::GetChild(root, index);
        auto element = child.try_as<FrameworkElement>();
        if (element && element.Name() == name) {
            return element;
        }

        auto descendant = FindDescendantByName(child, name, depth + 1);
        if (descendant) {
            return descendant;
        }
    }

    return nullptr;
}

bool SameWeatherSnapshot(std::optional<WeatherSnapshot> const& left,
                         std::optional<WeatherSnapshot> const& right) {
    if (left.has_value() != right.has_value()) return false;
    if (!left) return true;
    return left->icon == right->icon &&
           left->temperature == right->temperature &&
           left->description == right->description &&
           left->location == right->location &&
           left->alert == right->alert &&
           left->updatedUtc == right->updatedUtc;
}

std::wstring BuildWeatherDisplayText(WeatherSnapshot const& snapshot) {
    std::wstring detail = snapshot.alert.empty()
                              ? snapshot.description
                              : snapshot.alert;
    std::wstring result = snapshot.temperature;
    if (!detail.empty()) {
        result += L"  ";
        result += detail;
    }
    return SanitizePreviewText(result.c_str());
}

std::optional<WeatherSnapshot> GetEffectiveWeatherSnapshot() {
    {
        std::lock_guard lock(g_snapshotMutex);
        if (g_weatherSnapshot) return g_weatherSnapshot;
    }

    if (!g_allowStaticPreview) return std::nullopt;
    WeatherSnapshot preview;
    preview.icon = L"\x2600";
    preview.temperature = GetPreviewText();
    preview.updatedUtc = L"diagnostic";
    return preview;
}

void PublishWeatherSnapshot(std::optional<WeatherSnapshot> snapshot) {
    bool changed;
    {
        std::lock_guard lock(g_snapshotMutex);
        changed = !SameWeatherSnapshot(g_weatherSnapshot, snapshot);
        if (changed) g_weatherSnapshot = std::move(snapshot);
    }

    if (changed && g_enabled && !g_unloading) {
        ApplySettingsToTaskbarWindows(true);
    }
}

void CollectDescendantsByName(DependencyObject const& root,
                              PCWSTR name,
                              std::vector<FrameworkElement>& results,
                              int depth,
                              int& visited) {
    if (!root || depth >= kMaxTreeDepth || visited >= kMaxVisitedElements) {
        return;
    }

    int childCount = Media::VisualTreeHelper::GetChildrenCount(root);
    for (int index = 0;
         index < childCount && visited < kMaxVisitedElements;
         index++) {
        auto child = Media::VisualTreeHelper::GetChild(root, index);
        visited++;

        auto element = child.try_as<FrameworkElement>();
        if (element && element.Name() == name) {
            results.push_back(element);
        }

        CollectDescendantsByName(child, name, results, depth + 1, visited);
    }
}

bool IsWidgetsEntry(FrameworkElement const& element) {
    if (!element || element.Name() != kWidgetsElementName ||
        winrt::get_class_name(element) != kWidgetsClassName) {
        return false;
    }

    return Automation::AutomationProperties::GetAutomationId(element) ==
           kWidgetsAutomationId;
}

void CollectWidgetsEntries(DependencyObject const& root,
                           std::vector<FrameworkElement>& results,
                           int depth,
                           int& visited) {
    if (!root || depth >= kMaxTreeDepth || visited >= kMaxVisitedElements) {
        return;
    }

    int childCount = Media::VisualTreeHelper::GetChildrenCount(root);
    for (int index = 0;
         index < childCount && visited < kMaxVisitedElements;
         index++) {
        auto child = Media::VisualTreeHelper::GetChild(root, index);
        visited++;

        auto element = child.try_as<FrameworkElement>();
        if (element && IsWidgetsEntry(element)) {
            results.push_back(element);
            continue;
        }

        CollectWidgetsEntries(child, results, depth + 1, visited);
    }
}

FrameworkElement FindDirectChildByName(Controls::Grid const& grid,
                                       PCWSTR name) {
    if (!grid) {
        return nullptr;
    }

    for (auto const& child : grid.Children()) {
        auto element = child.try_as<FrameworkElement>();
        if (element && element.Name() == name) {
            return element;
        }
    }

    return nullptr;
}

size_t CountDirectChildrenByName(Controls::Grid const& grid, PCWSTR name) {
    size_t count = 0;
    for (auto const& child : grid.Children()) {
        auto element = child.try_as<FrameworkElement>();
        if (element && element.Name() == name) {
            count++;
        }
    }
    return count;
}

std::wstring MakeChildStateKey(PCWSTR propertyName,
                               UIElement const& child) {
    std::wstring key = propertyName;
    key += L":";
    key += std::to_wstring(
        reinterpret_cast<uintptr_t>(winrt::get_abi(child)));
    return key;
}

std::optional<OriginalChildState> CaptureChildState(
    UIElement const& child) noexcept {
    try {
        auto unsetValue = DependencyProperty::UnsetValue();
        auto opacityLocalValue =
            child.ReadLocalValue(UIElement::OpacityProperty());
        auto hitTestLocalValue =
            child.ReadLocalValue(UIElement::IsHitTestVisibleProperty());

        OriginalChildState state{
            .element = winrt::make_weak(child),
            .opacityIsLocal = opacityLocalValue != unsetValue,
            .opacity = child.Opacity(),
            .isHitTestVisibleIsLocal = hitTestLocalValue != unsetValue,
            .isHitTestVisible = child.IsHitTestVisible(),
        };

        if (state.opacityIsLocal) {
            state.opacity = winrt::unbox_value<double>(opacityLocalValue);
        }
        if (state.isHitTestVisibleIsLocal) {
            state.isHitTestVisible =
                winrt::unbox_value<bool>(hitTestLocalValue);
        }
        return state;
    } catch (...) {
        // BindingExpression and other non-primitive local values can't be
        // restored safely after a local override, so don't inject at all.
        return std::nullopt;
    }
}

void PersistChildState(
    winrt::Windows::Foundation::Collections::PropertySet const& stateBag,
    UIElement const& child,
    OriginalChildState const& childState) {
    stateBag.Insert(MakeChildStateKey(L"opacity-is-local", child),
                    winrt::box_value(childState.opacityIsLocal));
    stateBag.Insert(MakeChildStateKey(L"opacity", child),
                    winrt::box_value(childState.opacity));
    stateBag.Insert(
        MakeChildStateKey(L"hit-test-is-local", child),
        winrt::box_value(childState.isHitTestVisibleIsLocal));
    stateBag.Insert(MakeChildStateKey(L"hit-test", child),
                    winrt::box_value(childState.isHitTestVisible));
}

FrameworkElement CreateWeatherHost(std::wstring const& previewText) {
    constexpr PCWSTR xaml = LR"(
        <Grid
            xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            Name="TaskFlyoutWeatherHost"
            HorizontalAlignment="Stretch"
            VerticalAlignment="Stretch"
            IsHitTestVisible="False">
            <StackPanel
                HorizontalAlignment="Center"
                VerticalAlignment="Center"
                Orientation="Horizontal"
                Spacing="6">
                <TextBlock
                    Name="TaskFlyoutWeatherIcon"
                    FontFamily="Segoe UI Emoji"
                    FontSize="16"
                    Text="&#x2600;" />
                <TextBlock
                    Name="TaskFlyoutPreviewText"
                    VerticalAlignment="Center"
                    FontSize="12"
                    MaxLines="1"
                    TextTrimming="CharacterEllipsis" />
            </StackPanel>
        </Grid>
    )";

    auto host = Markup::XamlReader::Load(xaml).as<FrameworkElement>();
    auto textElement =
        FindDescendantByName(host, kPreviewTextName).as<Controls::TextBlock>();
    textElement.Text(previewText);
    return host;
}

void ApplyWeatherHostSnapshot(FrameworkElement const& host,
                              WeatherSnapshot const& snapshot) noexcept {
    try {
        auto icon = FindDescendantByName(host, kWeatherIconName)
                        .try_as<Controls::TextBlock>();
        auto text = FindDescendantByName(host, kPreviewTextName)
                        .try_as<Controls::TextBlock>();
        if (!icon || !text) return;

        std::wstring glyph = snapshot.icon.empty() ? L"\x2600" : snapshot.icon;
        std::wstring displayText = BuildWeatherDisplayText(snapshot);
        if (icon.Text() != glyph) icon.Text(glyph);
        if (text.Text() != displayText) text.Text(displayText);
    } catch (...) {
        // A rebuilt XAML subtree is handled by the next taskbar layout pass.
    }
}

bool WaitForOverlappedIo(HANDLE pipe,
                         OVERLAPPED& overlapped,
                         DWORD& transferred) noexcept {
    DWORD waitResult = WaitForSingleObject(overlapped.hEvent, kPipeIoWaitMs);
    if (waitResult != WAIT_OBJECT_0) {
        CancelIoEx(pipe, &overlapped);
        GetOverlappedResult(pipe, &overlapped, &transferred, TRUE);
        return false;
    }

    return GetOverlappedResult(pipe, &overlapped, &transferred, FALSE) != FALSE;
}

bool ReadPipeExact(HANDLE pipe, BYTE* buffer, DWORD length) noexcept {
    DWORD offset = 0;
    while (offset < length) {
        OVERLAPPED overlapped{};
        overlapped.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        if (!overlapped.hEvent) return false;

        DWORD transferred = 0;
        BOOL completed = ReadFile(pipe, buffer + offset, length - offset,
                                  &transferred, &overlapped);
        bool success = completed != FALSE;
        if (!success && GetLastError() == ERROR_IO_PENDING) {
            success = WaitForOverlappedIo(pipe, overlapped, transferred);
        }
        CloseHandle(overlapped.hEvent);
        if (!success || transferred == 0) return false;
        offset += transferred;
    }
    return true;
}

bool WritePipeExact(HANDLE pipe, BYTE const* buffer, DWORD length) noexcept {
    DWORD offset = 0;
    while (offset < length) {
        OVERLAPPED overlapped{};
        overlapped.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        if (!overlapped.hEvent) return false;

        DWORD transferred = 0;
        BOOL completed = WriteFile(pipe, buffer + offset, length - offset,
                                   &transferred, &overlapped);
        bool success = completed != FALSE;
        if (!success && GetLastError() == ERROR_IO_PENDING) {
            success = WaitForOverlappedIo(pipe, overlapped, transferred);
        }
        CloseHandle(overlapped.hEvent);
        if (!success || transferred == 0) return false;
        offset += transferred;
    }
    return true;
}

bool WritePipeFrame(HANDLE pipe, std::string const& payload) noexcept {
    if (payload.size() > 4 * 1024) return false;
    std::array<BYTE, 4> header{};
    uint32_t length = static_cast<uint32_t>(payload.size());
    header[0] = static_cast<BYTE>(length);
    header[1] = static_cast<BYTE>(length >> 8);
    header[2] = static_cast<BYTE>(length >> 16);
    header[3] = static_cast<BYTE>(length >> 24);
    return WritePipeExact(pipe, header.data(), static_cast<DWORD>(header.size())) &&
           WritePipeExact(pipe,
                          reinterpret_cast<BYTE const*>(payload.data()),
                          static_cast<DWORD>(payload.size()));
}

bool ReadPipeFrame(HANDLE pipe, std::vector<BYTE>& payload) noexcept {
    std::array<BYTE, 4> header{};
    if (!ReadPipeExact(pipe, header.data(), static_cast<DWORD>(header.size()))) {
        return false;
    }

    uint32_t length = static_cast<uint32_t>(header[0]) |
                      (static_cast<uint32_t>(header[1]) << 8) |
                      (static_cast<uint32_t>(header[2]) << 16) |
                      (static_cast<uint32_t>(header[3]) << 24);
    if (length > 16 * 1024) return false;

    payload.resize(length);
    return length == 0 || ReadPipeExact(pipe, payload.data(), length);
}

std::wstring GetWeatherPipePath() noexcept {
    HANDLE token = nullptr;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) {
        return {};
    }

    DWORD required = 0;
    GetTokenInformation(token, TokenUser, nullptr, 0, &required);
    std::vector<BYTE> tokenBuffer(required);
    bool tokenRead = required != 0 &&
                     GetTokenInformation(token, TokenUser, tokenBuffer.data(),
                                         required, &required) != FALSE;
    CloseHandle(token);
    if (!tokenRead) return {};

    auto tokenUser = reinterpret_cast<TOKEN_USER*>(tokenBuffer.data());
    LPWSTR sidText = nullptr;
    if (!ConvertSidToStringSidW(tokenUser->User.Sid, &sidText)) return {};

    DWORD sessionId = 0;
    bool sessionRead = ProcessIdToSessionId(GetCurrentProcessId(), &sessionId) != FALSE;
    std::wstring path;
    if (sessionRead) {
        path = L"\\\\.\\pipe\\";
        path += kWeatherPipePrefix;
        path += L".";
        path += sidText;
        path += L".";
        path += std::to_wstring(sessionId);
    }
    LocalFree(sidText);
    return path;
}

std::optional<std::wstring> Utf8ToWide(std::vector<BYTE> const& bytes) noexcept {
    if (bytes.empty()) return std::nullopt;
    int byteCount = static_cast<int>(bytes.size());
    int characterCount = MultiByteToWideChar(
        CP_UTF8, MB_ERR_INVALID_CHARS,
        reinterpret_cast<LPCCH>(bytes.data()), byteCount, nullptr, 0);
    if (characterCount <= 0) return std::nullopt;

    std::wstring result(characterCount, L'\0');
    if (!MultiByteToWideChar(
            CP_UTF8, MB_ERR_INVALID_CHARS,
            reinterpret_cast<LPCCH>(bytes.data()), byteCount,
            result.data(), characterCount)) {
        return std::nullopt;
    }
    return result;
}

std::wstring GetJsonString(
    winrt::Windows::Data::Json::JsonObject const& object,
    PCWSTR name) noexcept {
    try {
        winrt::hstring value = object.GetNamedString(name, {});
        std::wstring result(value.data(), value.size());
        if (result.size() > kMaxWeatherFieldLength) {
            result.resize(kMaxWeatherFieldLength);
            if (!result.empty() && result.back() >= 0xD800 &&
                result.back() <= 0xDBFF) {
                result.pop_back();
            }
        }
        for (wchar_t& character : result) {
            if (character < L' ' || character == 0x7F ||
                character == 0x2028 || character == 0x2029 ||
                character == 0x061C || character == 0x200E ||
                character == 0x200F ||
                (character >= 0x202A && character <= 0x202E) ||
                (character >= 0x2066 && character <= 0x2069)) {
                character = L' ';
            }
        }
        return result;
    } catch (...) {
        return {};
    }
}

bool ParseWeatherResponse(std::vector<BYTE> const& bytes,
                          WeatherSnapshot& snapshot) noexcept {
    auto wide = Utf8ToWide(bytes);
    if (!wide) return false;

    try {
        auto root = winrt::Windows::Data::Json::JsonObject::Parse(*wide);
        if (root.GetNamedNumber(L"version", -1) != 1 ||
            root.GetNamedString(L"status", {}) != L"ok") {
            return false;
        }

        auto snapshotObject = root.GetNamedObject(L"snapshot", nullptr);
        if (!snapshotObject) return false;
        WeatherSnapshot parsed{
            .icon = GetJsonString(snapshotObject, L"icon"),
            .temperature = GetJsonString(snapshotObject, L"temperature"),
            .description = GetJsonString(snapshotObject, L"description"),
            .location = GetJsonString(snapshotObject, L"location"),
            .alert = GetJsonString(snapshotObject, L"alert"),
            .updatedUtc = GetJsonString(snapshotObject, L"updatedUtc"),
        };
        if (parsed.temperature.empty() || parsed.updatedUtc.empty()) return false;
        snapshot = std::move(parsed);
        return true;
    } catch (...) {
        return false;
    }
}

bool QueryWeatherSnapshot(WeatherSnapshot& snapshot) noexcept {
    std::wstring pipePath = GetWeatherPipePath();
    if (pipePath.empty() || !WaitNamedPipeW(pipePath.c_str(), kPipeConnectWaitMs)) {
        return false;
    }

    HANDLE pipe = CreateFileW(
        pipePath.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr,
        OPEN_EXISTING, FILE_FLAG_OVERLAPPED, nullptr);
    if (pipe == INVALID_HANDLE_VALUE) return false;

    DWORD mode = PIPE_READMODE_BYTE;
    bool ready = SetNamedPipeHandleState(pipe, &mode, nullptr, nullptr) != FALSE;
    std::string request = R"({"version":1,"command":"get-snapshot"})";
    std::vector<BYTE> response;
    bool success = ready && WritePipeFrame(pipe, request) &&
                   ReadPipeFrame(pipe, response) &&
                   ParseWeatherResponse(response, snapshot);
    CloseHandle(pipe);
    return success;
}

struct WeatherWorkerContext {
    HMODULE selfModule;
};

DWORD WINAPI WeatherWorkerProc(void* parameter) {
    auto context = static_cast<WeatherWorkerContext*>(parameter);
    HMODULE selfModule = context->selfModule;
    delete context;

    while (!g_workerStop.load(std::memory_order_acquire)) {
        WeatherSnapshot snapshot;
        if (QueryWeatherSnapshot(snapshot)) {
            PublishWeatherSnapshot(std::move(snapshot));
        } else {
            PublishWeatherSnapshot(std::nullopt);
        }

        if (g_workerWakeEvent) {
            WaitForSingleObject(g_workerWakeEvent, kWorkerPollIntervalMs);
        } else {
            Sleep(kWorkerPollIntervalMs);
        }
    }

    FreeLibraryAndExitThread(selfModule, 0);
}

void StartWeatherWorker() {
    if (!g_enabled || g_allowStaticPreview || g_unloading) return;

    std::lock_guard lock(g_workerMutex);
    if (g_workerThread) {
        if (WaitForSingleObject(g_workerThread, 0) == WAIT_OBJECT_0) {
            CloseHandle(g_workerThread);
            g_workerThread = nullptr;
            if (g_workerWakeEvent) {
                CloseHandle(g_workerWakeEvent);
                g_workerWakeEvent = nullptr;
            }
        } else {
            return;
        }
    }

    g_workerWakeEvent = CreateEventW(nullptr, FALSE, FALSE, nullptr);
    if (!g_workerWakeEvent) return;
    g_workerStop = false;

    HMODULE selfModule = nullptr;
    if (!GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
            reinterpret_cast<LPCWSTR>(&WeatherWorkerProc), &selfModule)) {
        CloseHandle(g_workerWakeEvent);
        g_workerWakeEvent = nullptr;
        return;
    }

    auto context = new (std::nothrow) WeatherWorkerContext{selfModule};
    if (!context) {
        FreeLibrary(selfModule);
        CloseHandle(g_workerWakeEvent);
        g_workerWakeEvent = nullptr;
        return;
    }

    g_workerThread = CreateThread(
        nullptr, 0, WeatherWorkerProc, context, 0, nullptr);
    if (!g_workerThread) {
        delete context;
        FreeLibrary(selfModule);
        CloseHandle(g_workerWakeEvent);
        g_workerWakeEvent = nullptr;
    }
}

void StopWeatherWorker() {
    HANDLE worker = nullptr;
    {
        std::lock_guard lock(g_workerMutex);
        g_workerStop = true;
        if (g_workerWakeEvent) SetEvent(g_workerWakeEvent);
        worker = g_workerThread;
    }

    if (!worker) return;
    if (WaitForSingleObject(worker, kWorkerStopWaitMs) != WAIT_OBJECT_0) {
        Wh_Log(L"Weather companion worker is still stopping; its module reference remains held");
        return;
    }

    std::lock_guard lock(g_workerMutex);
    if (g_workerThread == worker) {
        CloseHandle(g_workerThread);
        g_workerThread = nullptr;
        if (g_workerWakeEvent) {
            CloseHandle(g_workerWakeEvent);
            g_workerWakeEvent = nullptr;
        }
    }
}

void RemoveHostsFromGrid(Controls::Grid const& grid) {
    if (!grid) {
        return;
    }

    auto children = grid.Children();
    for (uint32_t index = children.Size(); index > 0; index--) {
        auto child = children.GetAt(index - 1);
        auto element = child.try_as<FrameworkElement>();
        if (element && element.Name() == kHostName) {
            children.RemoveAt(index - 1);
        }
    }
}

void RestoreChildState(UIElement const& child,
                       OriginalChildState const& state) {
    if (state.opacityIsLocal) {
        child.Opacity(state.opacity);
    } else {
        child.ClearValue(UIElement::OpacityProperty());
    }

    if (state.isHitTestVisibleIsLocal) {
        child.IsHitTestVisible(state.isHitTestVisible);
    } else {
        child.ClearValue(UIElement::IsHitTestVisibleProperty());
    }
}

bool RecoverOrphanedHosts(Controls::Grid const& grid) noexcept {
    try {
        bool foundHost = false;
        for (auto const& possibleHost : grid.Children()) {
            auto host = possibleHost.try_as<FrameworkElement>();
            if (!host || host.Name() != kHostName) {
                continue;
            }
            foundHost = true;

            auto stateBag =
                host.Tag()
                    .try_as<
                        winrt::Windows::Foundation::Collections::PropertySet>();
            if (!stateBag || !stateBag.HasKey(kStateSchemaKey)) {
                // Keep an unknown host visible rather than risk revealing
                // native children whose original values can't be recovered.
                return false;
            }

            for (auto const& child : grid.Children()) {
                auto element = child.try_as<FrameworkElement>();
                if (element && element.Name() == kHostName) {
                    continue;
                }

                std::wstring opacityIsLocalKey =
                    MakeChildStateKey(L"opacity-is-local", child);
                std::wstring opacityKey =
                    MakeChildStateKey(L"opacity", child);
                std::wstring hitTestIsLocalKey =
                    MakeChildStateKey(L"hit-test-is-local", child);
                std::wstring hitTestKey =
                    MakeChildStateKey(L"hit-test", child);
                if (stateBag.HasKey(opacityIsLocalKey) &&
                    stateBag.HasKey(opacityKey)) {
                    if (winrt::unbox_value<bool>(
                            stateBag.Lookup(opacityIsLocalKey))) {
                        child.Opacity(winrt::unbox_value<double>(
                            stateBag.Lookup(opacityKey)));
                    } else {
                        child.ClearValue(UIElement::OpacityProperty());
                    }
                }
                if (stateBag.HasKey(hitTestIsLocalKey) &&
                    stateBag.HasKey(hitTestKey)) {
                    if (winrt::unbox_value<bool>(
                            stateBag.Lookup(hitTestIsLocalKey))) {
                        child.IsHitTestVisible(winrt::unbox_value<bool>(
                            stateBag.Lookup(hitTestKey)));
                    } else {
                        child.ClearValue(
                            UIElement::IsHitTestVisibleProperty());
                    }
                }
            }
        }

        if (foundHost) {
            RemoveHostsFromGrid(grid);
        }
        return true;
    } catch (...) {
        // The normal in-memory snapshot remains the authoritative restoration
        // path. Metadata recovery is only for an interrupted prior module.
        return false;
    }
}

bool RestoreState(InjectionState& state) noexcept {
    bool restored = true;
    for (auto const& childState : state.originalChildren) {
        try {
            if (auto child = childState.element.get()) {
                RestoreChildState(child, childState);
            }
        } catch (...) {
            restored = false;
        }
    }

    if (restored) {
        try {
            if (auto contentGrid = state.contentGrid.get()) {
                RemoveHostsFromGrid(contentGrid);
            }
        } catch (...) {
            restored = false;
        }
    }

    return restored;
}

void PruneExpiredStatesLocked() {
    DWORD currentThreadId = GetCurrentThreadId();
    std::erase_if(g_injections, [currentThreadId](InjectionState const& state) {
        return state.ownerThreadId == currentThreadId &&
               !state.contentGrid.get();
    });
}

size_t FindStateIndexLocked(Controls::Grid const& contentGrid) {
    DWORD currentThreadId = GetCurrentThreadId();
    for (size_t index = 0; index < g_injections.size(); index++) {
        if (g_injections[index].ownerThreadId != currentThreadId) {
            continue;
        }
        auto existingGrid = g_injections[index].contentGrid.get();
        if (existingGrid && SameObject(existingGrid, contentGrid)) {
            return index;
        }
    }

    return g_injections.size();
}

bool HasChildState(InjectionState const& state, UIElement const& child) {
    for (auto const& childState : state.originalChildren) {
        auto existingChild = childState.element.get();
        if (existingChild && SameObject(existingChild, child)) {
            return true;
        }
    }

    return false;
}

void RestoreContentGrid(Controls::Grid const& contentGrid) {
    std::lock_guard lock(g_injectionsMutex);
    PruneExpiredStatesLocked();

    size_t stateIndex = FindStateIndexLocked(contentGrid);
    if (stateIndex == g_injections.size()) {
        // Recover an orphan left by a prior interrupted preview without guessing
        // about any other element's original values.
        if (!RecoverOrphanedHosts(contentGrid)) {
            Wh_Log(L"Orphaned weather host couldn't be restored");
        }
        return;
    }

    if (RestoreState(g_injections[stateIndex])) {
        g_injections.erase(g_injections.begin() + stateIndex);
    } else {
        Wh_Log(L"Widgets content restoration was incomplete; will retry");
    }
}

void CreateInjectionLocked(Controls::Grid const& contentGrid) {
    // An orphan host means the previous module instance didn't get a clean
    // unload callback. Remove only our named element, then take a new exact
    // snapshot of the Windows-owned children.
    if (!RecoverOrphanedHosts(contentGrid)) {
        Wh_Log(L"Orphaned weather host couldn't be restored; skipping");
        return;
    }

    InjectionState newState;
    newState.contentGrid = winrt::make_weak(contentGrid);
    newState.ownerThreadId = GetCurrentThreadId();

    auto snapshot = GetEffectiveWeatherSnapshot();
    if (!snapshot) return;
    auto host = CreateWeatherHost(BuildWeatherDisplayText(*snapshot));
    ApplyWeatherHostSnapshot(host, *snapshot);
    newState.host = winrt::make_weak(host);

    auto stateBag =
        winrt::Windows::Foundation::Collections::PropertySet();
    stateBag.Insert(kStateSchemaKey, winrt::box_value(1));
    for (auto const& child : contentGrid.Children()) {
        auto childState = CaptureChildState(child);
        if (!childState) {
            Wh_Log(L"Widgets child has unsupported local values; skipping");
            return;
        }
        newState.originalChildren.push_back(std::move(*childState));
        PersistChildState(stateBag, child,
                          newState.originalChildren.back());
    }
    host.Tag(stateBag);

    g_injections.push_back(std::move(newState));
    InjectionState& trackedState = g_injections.back();

    try {
        for (auto const& childState : trackedState.originalChildren) {
            if (auto child = childState.element.get()) {
                child.Opacity(0);
                child.IsHitTestVisible(false);
            }
        }
        contentGrid.Children().Append(host);
    } catch (...) {
        if (RestoreState(trackedState)) {
            g_injections.pop_back();
        }
        throw;
    }
}

void EnsureContentGridInjected(Controls::Grid const& contentGrid) {
    std::lock_guard lock(g_injectionsMutex);
    PruneExpiredStatesLocked();

    size_t stateIndex = FindStateIndexLocked(contentGrid);
    if (!g_enabled || g_unloading) {
        if (stateIndex != g_injections.size()) {
            if (!RestoreState(g_injections[stateIndex])) {
                Wh_Log(L"Late injection was canceled with partial restore");
                return;
            }
            g_injections.erase(g_injections.begin() + stateIndex);
        } else {
            if (!RecoverOrphanedHosts(contentGrid)) {
                Wh_Log(L"Late orphan recovery was incomplete");
            }
        }
        return;
    }

    if (!GetEffectiveWeatherSnapshot()) {
        if (stateIndex != g_injections.size()) {
            if (!RestoreState(g_injections[stateIndex])) {
                Wh_Log(L"Weather snapshot unavailable; restoration is incomplete");
                return;
            }
            g_injections.erase(g_injections.begin() + stateIndex);
        } else if (!RecoverOrphanedHosts(contentGrid)) {
            Wh_Log(L"Weather snapshot unavailable; orphan recovery is incomplete");
        }
        return;
    }

    if (stateIndex == g_injections.size()) {
        CreateInjectionLocked(contentGrid);
        return;
    }

    InjectionState& state = g_injections[stateIndex];
    auto host = state.host.get();
    auto currentHost = FindDirectChildByName(contentGrid, kHostName);
    if (!host || !currentHost || !SameObject(host, currentHost) ||
        CountDirectChildrenByName(contentGrid, kHostName) != 1) {
        // Windows rebuilt this part of the Adaptive Card. Restore the exact
        // values we own before taking a fresh snapshot.
        if (!RestoreState(state)) {
            Wh_Log(L"Widgets content restoration was incomplete; will retry");
            return;
        }
        g_injections.erase(g_injections.begin() + stateIndex);
        CreateInjectionLocked(contentGrid);
        return;
    }

    // Adaptive Card children can be replaced without replacing the outer grid.
    // Track and hide any newly realized child while leaving it measured so the
    // Luminosity theme can keep using its ActualWidth variables.
    for (auto const& child : contentGrid.Children()) {
        auto element = child.try_as<FrameworkElement>();
        if (element && element.Name() == kHostName) {
            continue;
        }
        if (!HasChildState(state, child)) {
            auto childState = CaptureChildState(child);
            if (!childState) {
                Wh_Log(L"New Widgets child has unsupported local values; "
                       L"restoring native content");
                if (RestoreState(state)) {
                    g_injections.erase(g_injections.begin() + stateIndex);
                }
                return;
            }
            if (auto stateBag =
                    host.Tag()
                        .try_as<winrt::Windows::Foundation::Collections::
                                    PropertySet>()) {
                try {
                    PersistChildState(stateBag, child, *childState);
                } catch (...) {
                    Wh_Log(L"New Widgets child state couldn't be persisted; "
                           L"restoring native content");
                    if (RestoreState(state)) {
                        g_injections.erase(g_injections.begin() + stateIndex);
                    }
                    return;
                }
            } else {
                Wh_Log(L"Weather host state metadata is missing; restoring");
                if (RestoreState(state)) {
                    g_injections.erase(g_injections.begin() + stateIndex);
                }
                return;
            }
            state.originalChildren.push_back(std::move(*childState));
        }
        if (child.Opacity() != 0) {
            child.Opacity(0);
        }
        if (child.IsHitTestVisible()) {
            child.IsHitTestVisible(false);
        }
    }

    if (auto snapshot = GetEffectiveWeatherSnapshot()) {
        ApplyWeatherHostSnapshot(host, *snapshot);
    }
}

void UpdateWidgetsEntry(FrameworkElement const& widgetsEntry) {
    std::vector<FrameworkElement> contentGridCandidates;
    int visited = 0;
    CollectDescendantsByName(widgetsEntry, kContentGridName,
                             contentGridCandidates, 0, visited);
    if (contentGridCandidates.size() != 1) {
        Wh_Log(L"Expected exactly one AugmentedEntryPointContentGrid, got %zu",
               contentGridCandidates.size());
        return;
    }

    auto contentGrid = contentGridCandidates.front().try_as<Controls::Grid>();
    if (!contentGrid ||
        winrt::get_class_name(contentGrid) !=
            L"Windows.UI.Xaml.Controls.Grid") {
        Wh_Log(L"Widgets content-grid type mismatch; skipping");
        return;
    }

    if (g_enabled && !g_unloading) {
        EnsureContentGridInjected(contentGrid);
    } else {
        RestoreContentGrid(contentGrid);
    }
}

void UpdateTaskbarFrame(FrameworkElement const& taskbarFrame) {
    std::vector<FrameworkElement> widgetsEntries;
    int visited = 0;
    CollectWidgetsEntries(taskbarFrame, widgetsEntries, 0, visited);

    if (widgetsEntries.size() > 1) {
        Wh_Log(L"Expected at most one Widgets entry per TaskbarFrame, got %zu",
               widgetsEntries.size());
        return;
    }

    if (!widgetsEntries.empty()) {
        UpdateWidgetsEntry(widgetsEntries.front());
    }
}

using TaskbarFrame_OnTaskbarLayoutChildBoundsChanged_t =
    void(WINAPI*)(void* pThis);
TaskbarFrame_OnTaskbarLayoutChildBoundsChanged_t
    TaskbarFrame_OnTaskbarLayoutChildBoundsChanged_Original;

void WINAPI TaskbarFrame_OnTaskbarLayoutChildBoundsChanged_Hook(void* pThis) {
    TaskbarFrame_OnTaskbarLayoutChildBoundsChanged_Original(pThis);

    XamlUpdateGuard updateGuard;
    if (!updateGuard) {
        return;
    }

    try {
        void* taskbarFrameIUnknownPtr =
            reinterpret_cast<void**>(pThis) +
            kTaskbarFrameIInspectableSlot;
        winrt::Windows::Foundation::IUnknown taskbarFrameIUnknown;
        winrt::copy_from_abi(taskbarFrameIUnknown, taskbarFrameIUnknownPtr);

        auto taskbarFrame = taskbarFrameIUnknown.try_as<FrameworkElement>();
        if (!taskbarFrame ||
            winrt::get_class_name(taskbarFrame) != L"Taskbar.TaskbarFrame") {
            Wh_Log(L"TaskbarFrame ABI/tree signature mismatch; skipping");
            return;
        }

        UpdateTaskbarFrame(taskbarFrame);
    } catch (winrt::hresult_error const& error) {
        Wh_Log(L"Taskbar weather companion skipped update: 0x%08X %s",
               error.code().value, error.message().c_str());
    } catch (...) {
        Wh_Log(L"Taskbar weather companion skipped update: unknown error");
    }
}

bool HookTaskbarViewDllSymbols(HMODULE module) {
    WindhawkUtils::SYMBOL_HOOK symbolHooks[] = {
        {
            {LR"(private: void __cdecl winrt::Taskbar::implementation::TaskbarFrame::OnTaskbarLayoutChildBoundsChanged(void))"},
            &TaskbarFrame_OnTaskbarLayoutChildBoundsChanged_Original,
            TaskbarFrame_OnTaskbarLayoutChildBoundsChanged_Hook,
        },
    };

    if (!HookSymbols(module, symbolHooks, ARRAYSIZE(symbolHooks))) {
        Wh_Log(L"Required TaskbarFrame symbol was not resolved; no injection");
        return false;
    }

    return true;
}

HMODULE GetTaskbarViewModuleHandle() {
    HMODULE module = GetModuleHandleW(L"Taskbar.View.dll");
    if (!module) {
        module = GetModuleHandleW(L"ExplorerExtensions.dll");
    }
    return module;
}

bool IsValidatedTaskbarViewModule(HMODULE module) {
    if (!module) {
        return false;
    }

    auto base = reinterpret_cast<const BYTE*>(module);
    auto dosHeader = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    if (dosHeader->e_magic != IMAGE_DOS_SIGNATURE ||
        dosHeader->e_lfanew <= 0 || dosHeader->e_lfanew > 0x100000) {
        return false;
    }

    auto ntHeaders = reinterpret_cast<const IMAGE_NT_HEADERS64*>(
        base + dosHeader->e_lfanew);
    if (ntHeaders->Signature != IMAGE_NT_SIGNATURE ||
        ntHeaders->FileHeader.Machine != IMAGE_FILE_MACHINE_AMD64) {
        return false;
    }

    return ntHeaders->FileHeader.TimeDateStamp ==
               kValidatedTaskbarViewTimestamp &&
           ntHeaders->OptionalHeader.SizeOfImage ==
               kValidatedTaskbarViewImageSize &&
           ntHeaders->OptionalHeader.CheckSum ==
               kValidatedTaskbarViewChecksum;
}

void ApplyTrackedStatesFromCurrentTaskbarThread() noexcept {
    XamlUpdateGuard updateGuard;
    if (!updateGuard) {
        return;
    }

    try {
        DWORD currentThreadId = GetCurrentThreadId();
        auto snapshot = GetEffectiveWeatherSnapshot();
        bool shouldRestore = !g_enabled || g_unloading || !snapshot;

        std::lock_guard lock(g_injectionsMutex);
        for (size_t index = 0; index < g_injections.size();) {
            InjectionState& state = g_injections[index];
            if (state.ownerThreadId != currentThreadId) {
                index++;
                continue;
            }

            auto contentGrid = state.contentGrid.get();
            if (!contentGrid) {
                g_injections.erase(g_injections.begin() + index);
                continue;
            }

            if (shouldRestore) {
                if (!RestoreState(state)) {
                    Wh_Log(L"Taskbar-thread restoration was incomplete");
                    index++;
                    continue;
                }
                g_injections.erase(g_injections.begin() + index);
                continue;
            }

            auto host = state.host.get();
            auto currentHost =
                FindDirectChildByName(contentGrid, kHostName);
            if (!host || !currentHost || !SameObject(host, currentHost) ||
                CountDirectChildrenByName(contentGrid, kHostName) != 1) {
                if (RestoreState(state)) {
                    g_injections.erase(g_injections.begin() + index);
                } else {
                    index++;
                }
                continue;
            }

            auto textElement =
                FindDescendantByName(host, kPreviewTextName)
                    .try_as<Controls::TextBlock>();
            if (!textElement) {
                if (RestoreState(state)) {
                    g_injections.erase(g_injections.begin() + index);
                } else {
                    index++;
                }
                continue;
            }

            if (snapshot) ApplyWeatherHostSnapshot(host, *snapshot);
            index++;
        }
    } catch (winrt::hresult_error const& error) {
        Wh_Log(L"Taskbar-thread state update failed: 0x%08X %s",
               error.code().value, error.message().c_str());
    } catch (...) {
        Wh_Log(L"Taskbar-thread state update failed: unknown error");
    }
}

std::vector<DWORD> GetOwnerThreadIds() {
    std::vector<DWORD> result;
    std::lock_guard lock(g_injectionsMutex);
    for (auto const& state : g_injections) {
        if (std::find(result.begin(), result.end(), state.ownerThreadId) ==
            result.end()) {
            result.push_back(state.ownerThreadId);
        }
    }
    return result;
}

struct FindThreadWindowContext {
    DWORD threadId;
    HWND window;
};

BOOL CALLBACK FindThreadChildWindow(HWND window, LPARAM parameter) {
    auto context = reinterpret_cast<FindThreadWindowContext*>(parameter);
    if (GetWindowThreadProcessId(window, nullptr) == context->threadId) {
        context->window = window;
        return FALSE;
    }
    return TRUE;
}

HWND FindWindowForThread(DWORD threadId) {
    FindThreadWindowContext context{threadId, nullptr};
    EnumWindows(
        [](HWND window, LPARAM parameter) -> BOOL {
            auto context =
                reinterpret_cast<FindThreadWindowContext*>(parameter);
            if (GetWindowThreadProcessId(window, nullptr) ==
                context->threadId) {
                context->window = window;
                return FALSE;
            }

            EnumChildWindows(window, FindThreadChildWindow, parameter);
            return context->window ? FALSE : TRUE;
        },
        reinterpret_cast<LPARAM>(&context));
    return context.window;
}

UINT GetTaskbarThreadUpdateMessage() {
    static const UINT message =
        RegisterWindowMessageW(L"Windhawk_RunFromWindowThread_" WH_MOD_ID);
    return message;
}

struct ThreadDispatchRequest {
    DWORD expectedThreadId;
    HHOOK hook;
    std::atomic<bool> completed;
    std::atomic<bool> unhooked;
    std::atomic<bool> retryRequested;
};

std::atomic<ThreadDispatchRequest*> g_activeThreadDispatchRequest;

void DiscardStatesForExitedThread(DWORD ownerThreadId) {
    std::lock_guard lock(g_injectionsMutex);
    std::erase_if(g_injections, [ownerThreadId](auto const& state) {
        return state.ownerThreadId == ownerThreadId;
    });
}

bool RunTrackedStateUpdateOnOwnerThreadCore(DWORD ownerThreadId) noexcept {
    if (ownerThreadId == GetCurrentThreadId()) {
        ApplyTrackedStatesFromCurrentTaskbarThread();
        return true;
    }

    HANDLE threadHandle = OpenThread(
        SYNCHRONIZE | THREAD_QUERY_LIMITED_INFORMATION, FALSE, ownerThreadId);
    if (!threadHandle ||
        WaitForSingleObject(threadHandle, 0) == WAIT_OBJECT_0) {
        if (threadHandle) {
            CloseHandle(threadHandle);
        }
        Wh_Log(L"Taskbar XAML thread %lu has exited", ownerThreadId);
        DiscardStatesForExitedThread(ownerThreadId);
        return true;
    }

    HWND targetWindow = FindWindowForThread(ownerThreadId);
    if (!targetWindow) {
        Wh_Log(L"Taskbar XAML thread %lu has no reachable window",
               ownerThreadId);
        CloseHandle(threadHandle);
        // Keep the native-host metadata for recovery, but release the in-memory
        // weak state so unloading can't release it from the wrong apartment.
        DiscardStatesForExitedThread(ownerThreadId);
        return true;
    }

    UINT updateMessage = GetTaskbarThreadUpdateMessage();
    if (!updateMessage) {
        Wh_Log(L"Failed to register the taskbar-thread update message");
        CloseHandle(threadHandle);
        return false;
    }

    ThreadDispatchRequest request{ownerThreadId, nullptr, false, false, false};
    g_activeThreadDispatchRequest.store(&request, std::memory_order_release);

    HHOOK hook = SetWindowsHookExW(
        WH_CALLWNDPROC,
        [](int code, WPARAM wParam, LPARAM lParam) -> LRESULT {
            ThreadDispatchRequest* request =
                g_activeThreadDispatchRequest.load(std::memory_order_acquire);

            if (code == HC_ACTION) {
                auto messageDetails = reinterpret_cast<CWPSTRUCT*>(lParam);
                auto messageRequest = reinterpret_cast<ThreadDispatchRequest*>(
                    messageDetails->lParam);
                if (request && messageDetails->message ==
                                   GetTaskbarThreadUpdateMessage() &&
                    messageRequest == request &&
                    request->expectedThreadId == GetCurrentThreadId()) {
                        // Remove this hook from its owner thread before doing
                        // any XAML work. The synchronous sender returns only
                        // after this callback has returned, and no later message
                        // can enter the removed hook.
                        if (UnhookWindowsHookEx(request->hook)) {
                            request->unhooked.store(
                                true, std::memory_order_release);
                            ApplyTrackedStatesFromCurrentTaskbarThread();
                            request->completed.store(
                                true, std::memory_order_release);
                        } else {
                            request->retryRequested.store(
                                true, std::memory_order_release);
                        }
                }
            }

            return CallNextHookEx(nullptr, code, wParam, lParam);
        },
        nullptr, ownerThreadId);
    if (!hook) {
        Wh_Log(L"Failed to install taskbar-thread hook for %lu",
               ownerThreadId);
        g_activeThreadDispatchRequest.store(nullptr,
                                            std::memory_order_release);
        CloseHandle(threadHandle);
        return false;
    }
    request.hook = hook;

    bool threadExited = false;
    for (;;) {
        if (request.unhooked.load(std::memory_order_acquire) &&
            request.completed.load(std::memory_order_acquire)) {
            break;
        }

        if (WaitForSingleObject(threadHandle, 0) == WAIT_OBJECT_0) {
            // Windows removes a thread hook when its owner thread exits.
            threadExited = true;
            break;
        }

        targetWindow = FindWindowForThread(ownerThreadId);
        if (targetWindow) {
            // Windhawk's established taskbar pattern uses a synchronous window
            // message to run on the XAML owner thread. The matching callback
            // self-unhooks before touching XAML.
            SendMessageW(targetWindow, updateMessage, 0,
                         reinterpret_cast<LPARAM>(&request));
        }
        if (request.retryRequested.exchange(false,
                                            std::memory_order_acq_rel)) {
            Wh_Log(L"Retrying taskbar-thread self-unhook on %lu",
                   ownerThreadId);
        }
        Sleep(1);
    }

    CloseHandle(threadHandle);
    g_activeThreadDispatchRequest.store(nullptr, std::memory_order_release);

    if (threadExited) {
        DiscardStatesForExitedThread(ownerThreadId);
    }

    bool updated =
        request.completed.load(std::memory_order_acquire) || threadExited;
    if (!updated) {
        Wh_Log(L"Taskbar XAML thread %lu didn't run the state update",
               ownerThreadId);
    }
    return updated;
}

struct OwnerThreadDispatchContext {
    DWORD ownerThreadId;
    HMODULE selfModule;
};

DWORD WINAPI OwnerThreadDispatchWorker(void* parameter) {
    auto context = static_cast<OwnerThreadDispatchContext*>(parameter);
    DWORD ownerThreadId = context->ownerThreadId;
    HMODULE selfModule = context->selfModule;
    delete context;

    bool updated = false;
    {
        std::lock_guard dispatchLock(g_threadDispatchMutex);
        updated = RunTrackedStateUpdateOnOwnerThreadCore(ownerThreadId);
    }
    {
        std::lock_guard pendingLock(g_pendingDispatchMutex);
        g_pendingDispatchThreads.erase(ownerThreadId);
    }

    // The worker owns an extra module reference. This API releases it only
    // after all worker code and local destructors have finished executing.
    FreeLibraryAndExitThread(selfModule, updated ? 0 : 1);
}

bool RunTrackedStateUpdateOnOwnerThread(DWORD ownerThreadId) noexcept {
    if (ownerThreadId == GetCurrentThreadId()) {
        std::lock_guard dispatchLock(g_threadDispatchMutex);
        ApplyTrackedStatesFromCurrentTaskbarThread();
        return true;
    }

    {
        std::lock_guard pendingLock(g_pendingDispatchMutex);
        if (!g_pendingDispatchThreads.insert(ownerThreadId).second) {
            Wh_Log(L"Taskbar dispatch is already pending on thread %lu",
                   ownerThreadId);
            return false;
        }
    }

    auto releasePendingClaim = [ownerThreadId] {
        std::lock_guard pendingLock(g_pendingDispatchMutex);
        g_pendingDispatchThreads.erase(ownerThreadId);
    };

    HMODULE selfModule = nullptr;
    if (!GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
            reinterpret_cast<LPCWSTR>(&OwnerThreadDispatchWorker),
            &selfModule)) {
        Wh_Log(L"Failed to retain the companion module for dispatch");
        releasePendingClaim();
        return false;
    }

    auto context = new (std::nothrow)
        OwnerThreadDispatchContext{ownerThreadId, selfModule};
    if (!context) {
        FreeLibrary(selfModule);
        releasePendingClaim();
        return false;
    }

    HANDLE worker = CreateThread(nullptr, 0, OwnerThreadDispatchWorker,
                                 context, 0, nullptr);
    if (!worker) {
        delete context;
        FreeLibrary(selfModule);
        releasePendingClaim();
        Wh_Log(L"Failed to create taskbar dispatch worker");
        return false;
    }

    DWORD waitResult =
        WaitForSingleObject(worker, kOwnerThreadDispatchWaitMs);
    if (waitResult == WAIT_OBJECT_0) {
        DWORD exitCode = 1;
        GetExitCodeThread(worker, &exitCode);
        CloseHandle(worker);
        return exitCode == 0;
    }

    // Never terminate the worker or unload its code while a window hook might
    // still call it. The worker's self-reference keeps the DLL valid and it
    // will release itself after the taskbar thread becomes responsive.
    CloseHandle(worker);
    Wh_Log(L"Taskbar dispatch on thread %lu exceeded %lu ms; continuing "
           L"under a retained module reference",
           ownerThreadId, kOwnerThreadDispatchWaitMs);
    return false;
}

void RunTrackedStateUpdatesOnOwnerThreads() {
    // Copy thread IDs while locked, then release the lock before posting and
    // waiting; the owner-thread callback locks the state table again.
    auto ownerThreadIds = GetOwnerThreadIds();
    for (DWORD ownerThreadId : ownerThreadIds) {
        if (!RunTrackedStateUpdateOnOwnerThread(ownerThreadId)) {
            Wh_Log(L"Taskbar state update remains pending on thread %lu",
                   ownerThreadId);
        }
    }
}

void ApplySettingsToTaskbarWindows(bool triggerLayout) {
    RunTrackedStateUpdatesOnOwnerThreads();

    if (!triggerLayout) {
        return;
    }

    EnumWindows(
        [](HWND window, LPARAM) -> BOOL {
            DWORD processId = 0;
            GetWindowThreadProcessId(window, &processId);
            if (processId != GetCurrentProcessId()) {
                return TRUE;
            }

            WCHAR className[64]{};
            if (!GetClassNameW(window, className, ARRAYSIZE(className)) ||
                (wcscmp(className, L"Shell_TrayWnd") != 0 &&
                 wcscmp(className, L"Shell_SecondaryTrayWnd") != 0)) {
                return TRUE;
            }

            PostMessageW(window, WM_SETTINGCHANGE, 0, 0);
            return TRUE;
        },
        0);
}

BOOL ModInitWithTaskbarView(HMODULE taskbarViewModule) {
    if (!IsValidatedTaskbarViewModule(taskbarViewModule)) {
        Wh_Log(L"Taskbar view ABI fingerprint is not allowlisted; no injection");
        return FALSE;
    }

    if (!HookTaskbarViewDllSymbols(taskbarViewModule)) {
        return FALSE;
    }
    return TRUE;
}

using LoadLibraryExW_t = decltype(&LoadLibraryExW);
LoadLibraryExW_t LoadLibraryExW_Original;

HMODULE WINAPI LoadLibraryExW_Hook(LPCWSTR fileName,
                                   HANDLE file,
                                   DWORD flags) {
    HMODULE module = LoadLibraryExW_Original(fileName, file, flags);
    if (module && !g_taskbarViewDllLoaded &&
        GetTaskbarViewModuleHandle() == module &&
        !g_taskbarViewDllLoaded.exchange(true)) {
        Wh_Log(L"Taskbar view module loaded late: %s", fileName);
        if (ModInitWithTaskbarView(module)) {
            Wh_ApplyHookOperations();
            StartWeatherWorker();
            ApplySettingsToTaskbarWindows(true);
        }
    }

    return module;
}

}  // namespace

BOOL Wh_ModInit() {
    Wh_Log(L">");
    LoadSettings();

    DWORD buildNumber = GetWindowsBuildNumber();
    if (buildNumber != kValidatedWindowsBuild) {
        Wh_Log(L"Unsupported Windows build %lu (validated: %lu); no injection",
               buildNumber, kValidatedWindowsBuild);
        return FALSE;
    }

    if (HMODULE taskbarViewModule = GetTaskbarViewModuleHandle()) {
        g_taskbarViewDllLoaded = true;
        return ModInitWithTaskbarView(taskbarViewModule);
    }

    HMODULE kernelBase = GetModuleHandleW(L"kernelbase.dll");
    auto loadLibraryExW = reinterpret_cast<decltype(&LoadLibraryExW)>(
        GetProcAddress(kernelBase, "LoadLibraryExW"));
    if (!loadLibraryExW) {
        Wh_Log(L"LoadLibraryExW was not found; no injection");
        return FALSE;
    }

    WindhawkUtils::SetFunctionHook(loadLibraryExW, LoadLibraryExW_Hook,
                                   &LoadLibraryExW_Original);
    return TRUE;
}

void Wh_ModAfterInit() {
    if (!g_taskbarViewDllLoaded) {
        if (HMODULE taskbarViewModule = GetTaskbarViewModuleHandle()) {
            if (!g_taskbarViewDllLoaded.exchange(true) &&
                ModInitWithTaskbarView(taskbarViewModule)) {
                Wh_ApplyHookOperations();
            }
        }
    }

    if (g_taskbarViewDllLoaded) {
        StartWeatherWorker();
        ApplySettingsToTaskbarWindows(true);
    }
}

void Wh_ModBeforeUninit() {
    Wh_Log(L">");
    g_unloading = true;
    StopWeatherWorker();
    PublishWeatherSnapshot(std::nullopt);

    if (g_taskbarViewDllLoaded) {
        ApplySettingsToTaskbarWindows(false);
    }

    std::lock_guard lock(g_injectionsMutex);
    if (!g_injections.empty()) {
        Wh_Log(L"%zu taskbar injection state(s) did not receive a restore pass",
               g_injections.size());
    }
}

void Wh_ModUninit() {
    Wh_Log(L">");
}

void Wh_ModSettingsChanged() {
    LoadSettings();
    if (g_enabled && !g_allowStaticPreview) {
        StartWeatherWorker();
    } else {
        StopWeatherWorker();
        PublishWeatherSnapshot(std::nullopt);
    }
    if (g_taskbarViewDllLoaded) {
        ApplySettingsToTaskbarWindows(true);
    }
}
