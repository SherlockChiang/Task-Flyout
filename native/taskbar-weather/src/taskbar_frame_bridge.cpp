#include "taskbar_frame_bridge.h"

#include "taskbar_detour.h"

#include <Windows.h>

#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.UI.Core.h>
#include <winrt/base.h>

#include <array>
#include <cstddef>
#include <cstdint>
#include <limits>

namespace taskflyout::taskbar {
namespace {

constexpr std::size_t kTaskbarFrameIInspectableSlot = 3;
constexpr wchar_t kTaskbarFrameClass[] = L"Taskbar.TaskbarFrame";

using QueryInterfaceFunction = HRESULT(STDMETHODCALLTYPE*)(
    void* object,
    REFIID interfaceId,
    void** result);
using ReleaseFunction = ULONG(STDMETHODCALLTYPE*)(void* object);

bool IsReadableProtection(const DWORD protection) noexcept {
    if ((protection & (PAGE_GUARD | PAGE_NOACCESS)) != 0) {
        return false;
    }
    switch (protection & 0xFFU) {
        case PAGE_READONLY:
        case PAGE_READWRITE:
        case PAGE_WRITECOPY:
        case PAGE_EXECUTE_READ:
        case PAGE_EXECUTE_READWRITE:
        case PAGE_EXECUTE_WRITECOPY:
            return true;
        default:
            return false;
    }
}

bool IsExecutableProtection(const DWORD protection) noexcept {
    if ((protection & (PAGE_GUARD | PAGE_NOACCESS)) != 0) {
        return false;
    }
    switch (protection & 0xFFU) {
        case PAGE_EXECUTE:
        case PAGE_EXECUTE_READ:
        case PAGE_EXECUTE_READWRITE:
        case PAGE_EXECUTE_WRITECOPY:
            return true;
        default:
            return false;
    }
}

bool IsRangeInRegion(
    const void* address,
    const std::size_t length,
    const MEMORY_BASIC_INFORMATION& memory) noexcept {
    if (!address || length == 0 || memory.RegionSize == 0) {
        return false;
    }
    const auto start = reinterpret_cast<std::uintptr_t>(address);
    const auto regionStart =
        reinterpret_cast<std::uintptr_t>(memory.BaseAddress);
    if (start < regionStart ||
        start - regionStart > memory.RegionSize) {
        return false;
    }
    return length <= memory.RegionSize - (start - regionStart);
}

bool IsReadableRange(const void* address, const std::size_t length) noexcept {
    MEMORY_BASIC_INFORMATION memory{};
    return address &&
           VirtualQuery(address, &memory, sizeof(memory)) == sizeof(memory) &&
           memory.State == MEM_COMMIT &&
           IsReadableProtection(memory.Protect) &&
           IsRangeInRegion(address, length, memory);
}

bool IsExecutableAddress(const void* address) noexcept {
    MEMORY_BASIC_INFORMATION memory{};
    return address &&
           VirtualQuery(address, &memory, sizeof(memory)) == sizeof(memory) &&
           memory.State == MEM_COMMIT &&
           memory.Type == MEM_IMAGE &&
           IsExecutableProtection(memory.Protect) &&
           IsRangeInRegion(address, 1, memory);
}

template <typename T>
bool ReadCurrentProcessValue(const void* address, T& value) noexcept {
    value = {};
    SIZE_T bytesRead = 0;
    return IsReadableRange(address, sizeof(T)) &&
           ReadProcessMemory(
               GetCurrentProcess(),
               address,
               &value,
               sizeof(T),
               &bytesRead) != FALSE &&
           bytesRead == sizeof(T);
}

bool ReadInspectableMethods(
    void* interfaceAddress,
    std::array<void*, 3>& methods) noexcept {
    methods.fill(nullptr);
    void** vtable = nullptr;
    if (!ReadCurrentProcessValue(interfaceAddress, vtable) || !vtable) {
        return false;
    }
    return ReadCurrentProcessValue(vtable, methods);
}

bool TryQueryInterface(
    void* interfaceAddress,
    REFIID interfaceId,
    void** result) noexcept {
    if (!result) {
        return false;
    }
    *result = nullptr;

    std::array<void*, 3> methods{};
    if (!ReadInspectableMethods(interfaceAddress, methods) ||
        !IsExecutableAddress(methods[0]) ||
        !IsExecutableAddress(methods[1]) ||
        !IsExecutableAddress(methods[2])) {
        return false;
    }

    const auto queryInterface =
        reinterpret_cast<QueryInterfaceFunction>(methods[0]);
    __try {
        const HRESULT status = queryInterface(interfaceAddress, interfaceId, result);
        if (FAILED(status) || !*result) {
            *result = nullptr;
            return false;
        }
        return true;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        *result = nullptr;
        return false;
    }
}

void ReleaseRawInterface(void* interfaceAddress) noexcept {
    if (!interfaceAddress) {
        return;
    }
    std::array<void*, 3> methods{};
    if (!ReadInspectableMethods(interfaceAddress, methods) ||
        !IsExecutableAddress(methods[2])) {
        return;
    }

    const auto release = reinterpret_cast<ReleaseFunction>(methods[2]);
    __try {
        release(interfaceAddress);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
    }
}

}  // namespace

TaskbarFrameBridgeStatus EvaluateTaskbarFrameBridgeGate(
    const TaskbarFrameBridgeGateInput& input) noexcept {
    if (!input.privateObjectPresent) {
        return TaskbarFrameBridgeStatus::NullPrivateObject;
    }
    if (!input.compatibilitySupported) {
        return TaskbarFrameBridgeStatus::CompatibilityRejected;
    }
    if (!input.detourActive) {
        return TaskbarFrameBridgeStatus::DetourInactive;
    }
    if (!input.callbackScopeActive) {
        return TaskbarFrameBridgeStatus::CallbackScopeInactive;
    }
    if (!input.ownerThread) {
        return TaskbarFrameBridgeStatus::WrongOwnerThread;
    }
    if (!input.inspectableSlotReadable) {
        return TaskbarFrameBridgeStatus::InspectableSlotUnreadable;
    }
    if (!input.inspectablePointerPresent) {
        return TaskbarFrameBridgeStatus::InspectablePointerNull;
    }
    if (!input.inspectableObjectReadable) {
        return TaskbarFrameBridgeStatus::InspectableObjectUnreadable;
    }
    if (!input.inspectableVtableReadable) {
        return TaskbarFrameBridgeStatus::InspectableVtableUnreadable;
    }
    if (!input.inspectableMethodsValid) {
        return TaskbarFrameBridgeStatus::InspectableMethodInvalid;
    }
    if (!input.projectionSucceeded) {
        return TaskbarFrameBridgeStatus::ProjectionFailed;
    }
    if (!input.frameTypeMatches) {
        return TaskbarFrameBridgeStatus::FrameTypeMismatch;
    }
    if (!input.dispatcherAvailable) {
        return TaskbarFrameBridgeStatus::DispatcherUnavailable;
    }
    if (!input.dispatcherHasThreadAccess) {
        return TaskbarFrameBridgeStatus::DispatcherThreadMismatch;
    }
    return TaskbarFrameBridgeStatus::Resolved;
}

TaskbarFrameBridgeResult ResolveTaskbarFrameFromPrivateAbi(
    void* privateTaskbarFrame) noexcept {
    TaskbarFrameBridgeResult result;
    TaskbarFrameBridgeGateInput gate;
    gate.privateObjectPresent = privateTaskbarFrame != nullptr;
    if (!gate.privateObjectPresent) {
        result.status = EvaluateTaskbarFrameBridgeGate(gate);
        return result;
    }

    const TaskbarDetourSnapshot snapshot = GetTaskbarDetourSnapshot();
    gate.compatibilitySupported =
        snapshot.compatibility == HostCompatibility::Supported;
    gate.detourActive = snapshot.state == TaskbarDetourState::Active;
    gate.callbackScopeActive =
        IsInsideTaskbarFrameCallback(privateTaskbarFrame);
    gate.ownerThread = snapshot.bootstrapThreadId != 0 &&
        snapshot.bootstrapThreadId == GetCurrentThreadId();
    result.status = EvaluateTaskbarFrameBridgeGate(gate);
    if (!gate.compatibilitySupported || !gate.detourActive ||
        !gate.callbackScopeActive || !gate.ownerThread) {
        return result;
    }

    const auto objectAddress =
        reinterpret_cast<std::uintptr_t>(privateTaskbarFrame);
    constexpr std::size_t slotOffset =
        kTaskbarFrameIInspectableSlot * sizeof(void*);
    if (objectAddress >
        std::numeric_limits<std::uintptr_t>::max() - slotOffset) {
        result.status = TaskbarFrameBridgeStatus::InspectableSlotUnreadable;
        return result;
    }
    const auto* slotAddress = reinterpret_cast<const void*>(
        objectAddress + slotOffset);

    void* inspectablePointer = const_cast<void*>(slotAddress);
    gate.inspectableSlotReadable =
        IsReadableRange(slotAddress, sizeof(void*));
    gate.inspectablePointerPresent = inspectablePointer != nullptr;
    result.status = EvaluateTaskbarFrameBridgeGate(gate);
    if (!gate.inspectableSlotReadable || !gate.inspectablePointerPresent) {
        return result;
    }

    std::array<void*, 3> methods{};
    gate.inspectableObjectReadable =
        ReadCurrentProcessValue(inspectablePointer, methods[0]);
    if (gate.inspectableObjectReadable) {
        gate.inspectableVtableReadable =
            ReadInspectableMethods(inspectablePointer, methods);
        gate.inspectableMethodsValid =
            gate.inspectableVtableReadable &&
            IsExecutableAddress(methods[0]) &&
            IsExecutableAddress(methods[1]) &&
            IsExecutableAddress(methods[2]);
    }
    result.status = EvaluateTaskbarFrameBridgeGate(gate);
    if (!gate.inspectableObjectReadable ||
        !gate.inspectableVtableReadable ||
        !gate.inspectableMethodsValid) {
        return result;
    }

    void* rawFrame = nullptr;
    if (!TryQueryInterface(
            inspectablePointer,
            winrt::guid_of<winrt::Windows::UI::Xaml::IFrameworkElement>(),
            &rawFrame)) {
        result.status = TaskbarFrameBridgeStatus::ProjectionFailed;
        return result;
    }

    std::array<void*, 3> projectedMethods{};
    if (!ReadInspectableMethods(rawFrame, projectedMethods)) {
        ReleaseRawInterface(rawFrame);
        result.status = TaskbarFrameBridgeStatus::InspectableVtableUnreadable;
        return result;
    }
    if (!IsExecutableAddress(projectedMethods[0]) ||
        !IsExecutableAddress(projectedMethods[1]) ||
        !IsExecutableAddress(projectedMethods[2])) {
        ReleaseRawInterface(rawFrame);
        result.status = TaskbarFrameBridgeStatus::InspectableMethodInvalid;
        return result;
    }

    try {
        result.frame = winrt::Windows::UI::Xaml::FrameworkElement(
            rawFrame,
            winrt::take_ownership_from_abi);
        rawFrame = nullptr;
        gate.projectionSucceeded = !!result.frame;
        gate.frameTypeMatches = gate.projectionSucceeded &&
            winrt::get_class_name(result.frame) == kTaskbarFrameClass;
        if (gate.frameTypeMatches) {
            const auto dispatcher = result.frame.Dispatcher();
            gate.dispatcherAvailable = !!dispatcher;
            gate.dispatcherHasThreadAccess =
                dispatcher && dispatcher.HasThreadAccess();
        }
    } catch (...) {
        result.frame = nullptr;
    }
    if (rawFrame) {
        ReleaseRawInterface(rawFrame);
    }

    result.status = EvaluateTaskbarFrameBridgeGate(gate);
    if (result.status != TaskbarFrameBridgeStatus::Resolved) {
        result.frame = nullptr;
    }
    return result;
}

const wchar_t* TaskbarFrameBridgeStatusName(
    const TaskbarFrameBridgeStatus status) noexcept {
    switch (status) {
        case TaskbarFrameBridgeStatus::Resolved:
            return L"resolved";
        case TaskbarFrameBridgeStatus::NullPrivateObject:
            return L"null-private-object";
        case TaskbarFrameBridgeStatus::CompatibilityRejected:
            return L"compatibility-rejected";
        case TaskbarFrameBridgeStatus::DetourInactive:
            return L"detour-inactive";
        case TaskbarFrameBridgeStatus::CallbackScopeInactive:
            return L"callback-scope-inactive";
        case TaskbarFrameBridgeStatus::WrongOwnerThread:
            return L"wrong-owner-thread";
        case TaskbarFrameBridgeStatus::InspectableSlotUnreadable:
            return L"inspectable-slot-unreadable";
        case TaskbarFrameBridgeStatus::InspectablePointerNull:
            return L"inspectable-pointer-null";
        case TaskbarFrameBridgeStatus::InspectableObjectUnreadable:
            return L"inspectable-object-unreadable";
        case TaskbarFrameBridgeStatus::InspectableVtableUnreadable:
            return L"inspectable-vtable-unreadable";
        case TaskbarFrameBridgeStatus::InspectableMethodInvalid:
            return L"inspectable-method-invalid";
        case TaskbarFrameBridgeStatus::ProjectionFailed:
            return L"projection-failed";
        case TaskbarFrameBridgeStatus::FrameTypeMismatch:
            return L"frame-type-mismatch";
        case TaskbarFrameBridgeStatus::DispatcherUnavailable:
            return L"dispatcher-unavailable";
        case TaskbarFrameBridgeStatus::DispatcherThreadMismatch:
            return L"dispatcher-thread-mismatch";
    }
    return L"unknown";
}

}  // namespace taskflyout::taskbar
