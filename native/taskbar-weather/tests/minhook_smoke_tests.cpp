#include <MinHook.h>

#include <Windows.h>

#include <cstdio>

namespace {

using TestFunction = int(WINAPI*)(int value);
TestFunction g_original = nullptr;
volatile LONG g_detourCalls = 0;

__declspec(noinline) int WINAPI TestTarget(int value) {
    return value + 1;
}

int WINAPI TestDetour(int value) {
    InterlockedIncrement(&g_detourCalls);
    return g_original(value) + 10;
}

bool Expect(bool condition, const wchar_t* message) {
    if (condition) {
        return true;
    }
    fwprintf(stderr, L"FAILED: %ls\n", message);
    return false;
}

}  // namespace

int wmain() {
    bool passed = true;
    bool initialized = false;
    bool created = false;

    MH_STATUS status = MH_Initialize();
    passed &= Expect(status == MH_OK, L"MinHook should initialize");
    initialized = status == MH_OK;
    if (!initialized) {
        return 1;
    }

    status = MH_CreateHook(
        reinterpret_cast<void*>(&TestTarget),
        reinterpret_cast<void*>(&TestDetour),
        reinterpret_cast<void**>(&g_original));
    passed &= Expect(status == MH_OK && g_original != nullptr,
                     L"MinHook should create the smoke detour");
    created = status == MH_OK;

    if (created) {
        status = MH_EnableHook(reinterpret_cast<void*>(&TestTarget));
        passed &= Expect(status == MH_OK, L"MinHook should enable the detour");
        if (status == MH_OK) {
            passed &= Expect(
                TestTarget(1) == 12 &&
                    InterlockedCompareExchange(&g_detourCalls, 0, 0) == 1,
                L"enabled detour should call the trampoline and callback");
        }

        status = MH_DisableHook(reinterpret_cast<void*>(&TestTarget));
        passed &= Expect(status == MH_OK, L"MinHook should disable the detour");
        passed &= Expect(
            TestTarget(1) == 2,
            L"disabled detour should restore the original result");

        status = MH_RemoveHook(reinterpret_cast<void*>(&TestTarget));
        passed &= Expect(status == MH_OK, L"MinHook should remove the detour");
        created = false;
    }

    if (created) {
        MH_DisableHook(reinterpret_cast<void*>(&TestTarget));
        MH_RemoveHook(reinterpret_cast<void*>(&TestTarget));
    }
    if (initialized) {
        status = MH_Uninitialize();
        passed &= Expect(status == MH_OK, L"MinHook should uninitialize");
    }

    if (!passed) {
        return 1;
    }

    fputws(L"MinHook smoke tests passed.\n", stdout);
    return 0;
}
