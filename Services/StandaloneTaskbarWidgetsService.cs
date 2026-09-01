using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Task_Flyout.Services
{
    internal enum StandaloneTaskbarWidgetsResultKind
    {
        Suppressed,
        AlreadySuppressedUnowned,
        RecoveredSuppression,
        Restored,
        NoOwnership,
        ExternalChangePreserved,
        ConflictingOwner,
        LockUnavailable,
        InvalidSnapshot,
        SettingsFailure,
        RegistryFailure
    }

    internal readonly record struct StandaloneTaskbarWidgetsResult(
        StandaloneTaskbarWidgetsResultKind Kind,
        bool Succeeded,
        bool RegistryChanged,
        bool IsEffectivelySuppressed = false,
        bool OwnsSetting = false,
        bool NotificationPending = false)
    {
        public bool RequiresRecovery => OwnsSetting || NotificationPending;

        public string DiagnosticKey => Kind switch
        {
            StandaloneTaskbarWidgetsResultKind.Suppressed => "windows-widgets-suppressed",
            StandaloneTaskbarWidgetsResultKind.AlreadySuppressedUnowned => "windows-widgets-already-suppressed",
            StandaloneTaskbarWidgetsResultKind.RecoveredSuppression => "windows-widgets-suppression-recovered",
            StandaloneTaskbarWidgetsResultKind.Restored => "windows-widgets-restored",
            StandaloneTaskbarWidgetsResultKind.NoOwnership => "windows-widgets-no-ownership",
            StandaloneTaskbarWidgetsResultKind.ExternalChangePreserved => "windows-widgets-external-change-preserved",
            StandaloneTaskbarWidgetsResultKind.ConflictingOwner => "windows-widgets-conflicting-owner",
            StandaloneTaskbarWidgetsResultKind.LockUnavailable => "windows-widgets-lock-unavailable",
            StandaloneTaskbarWidgetsResultKind.InvalidSnapshot => "windows-widgets-invalid-snapshot",
            StandaloneTaskbarWidgetsResultKind.SettingsFailure => "windows-widgets-settings-failure",
            _ => "windows-widgets-registry-failure"
        };
    }

    internal readonly record struct TaskbarDaRegistryValue(
        bool IsPresent,
        object? Value,
        RegistryValueKind Kind);

    internal interface ITaskbarDaRegistryStore
    {
        TaskbarDaRegistryValue Read();
        void Set(object value, RegistryValueKind kind);
        void Delete();
    }

    /// <summary>
    /// Temporarily hides the Windows-owned Widgets taskbar entry while the
    /// standalone Task Flyout button is requested. Ownership is persisted before
    /// TaskbarDa is changed, survives process failure, and is released only when
    /// the current value still matches the DWORD 0 written by this service.
    /// </summary>
    internal static class StandaloneTaskbarWidgetsService
    {
        internal const string CapturedSettingKey =
            "StandaloneTaskbarTaskbarDaCaptured";
        internal const string CapturedPresentKey =
            "StandaloneTaskbarTaskbarDaPresent";
        internal const string CapturedValueKey =
            "StandaloneTaskbarTaskbarDaValue";
        internal const string CapturedKindKey =
            "StandaloneTaskbarTaskbarDaKind";
        internal const string CapturedSchemaKey =
            "StandaloneTaskbarTaskbarDaSchema";
        internal const string CapturedAppliedKey =
            "StandaloneTaskbarTaskbarDaApplied";
        internal const string NotificationPendingKey =
            "StandaloneTaskbarTaskbarDaNotificationPending";
        internal const string CurrentSchemaVersion = "1";

        public static StandaloneTaskbarWidgetsResult TrySuppress(
            IDictionary<string, object> localSettings)
            => RunWithRegistry(localSettings, EnsureSuppressedCore);

        public static StandaloneTaskbarWidgetsResult TryRestore(
            IDictionary<string, object> localSettings)
            => RunWithRegistry(localSettings, RestoreCore);

        internal static StandaloneTaskbarWidgetsResult EnsureSuppressedCore(
            IDictionary<string, object> localSettings,
            ITaskbarDaRegistryStore store)
        {
            ArgumentNullException.ThrowIfNull(localSettings);
            ArgumentNullException.ThrowIfNull(store);

            try
            {
                if (WindowsWidgetsService.HasCapturedTaskbarEntry(localSettings))
                {
                    return new StandaloneTaskbarWidgetsResult(
                        StandaloneTaskbarWidgetsResultKind.ConflictingOwner,
                        Succeeded: false,
                        RegistryChanged: false,
                        OwnsSetting: HasCapturedTaskbarEntry(localSettings));
                }

                TaskbarDaRegistryValue current = store.Read();
                if (HasCapturedTaskbarEntry(localSettings))
                {
                    if (!TryReadCapturedTaskbarEntry(
                            localSettings,
                            out TaskbarEntrySnapshot snapshot,
                            out bool applied))
                    {
                        return new StandaloneTaskbarWidgetsResult(
                            StandaloneTaskbarWidgetsResultKind.InvalidSnapshot,
                            Succeeded: false,
                            RegistryChanged: false,
                            OwnsSetting: true);
                    }

                    if (!applied)
                    {
                        // A prepared snapshot cannot prove whether this process
                        // reached the registry write. The current value may also
                        // have been changed by the user or policy after a crash.
                        // Keep the marker quarantined for a safe mode-exit release,
                        // but never adopt or replay an ambiguous value.
                        return new StandaloneTaskbarWidgetsResult(
                            StandaloneTaskbarWidgetsResultKind.ExternalChangePreserved,
                            Succeeded: false,
                            RegistryChanged: false,
                            IsEffectivelySuppressed:
                                IsServiceOwnedTaskbarValue(current),
                            OwnsSetting: true);
                    }

                    if (IsServiceOwnedTaskbarValue(current))
                    {
                        return new StandaloneTaskbarWidgetsResult(
                            StandaloneTaskbarWidgetsResultKind.RecoveredSuppression,
                            Succeeded: true,
                            RegistryChanged: false,
                            IsEffectivelySuppressed: true,
                            OwnsSetting: true);
                    }

                    // A committed snapshot plus a non-zero current value means
                    // another actor changed TaskbarDa after our write (or our write
                    // never completed). Preserve it and keep the snapshot so later
                    // Apply calls cannot repeatedly overwrite that newer choice.
                    return new StandaloneTaskbarWidgetsResult(
                        StandaloneTaskbarWidgetsResultKind.ExternalChangePreserved,
                        Succeeded: false,
                        RegistryChanged: false,
                        OwnsSetting: true);
                }

                if (IsServiceOwnedTaskbarValue(current))
                {
                    RemoveUncommittedSnapshotMetadata(localSettings);
                    return new StandaloneTaskbarWidgetsResult(
                        StandaloneTaskbarWidgetsResultKind.AlreadySuppressedUnowned,
                        Succeeded: true,
                        RegistryChanged: false,
                        IsEffectivelySuppressed: true);
                }

                WriteCapturedTaskbarEntry(localSettings, current);
                TaskbarDaRegistryValue confirmed = store.Read();
                if (!RegistryValuesEqual(current, confirmed))
                {
                    return new StandaloneTaskbarWidgetsResult(
                        StandaloneTaskbarWidgetsResultKind.ExternalChangePreserved,
                        Succeeded: false,
                        RegistryChanged: false,
                        OwnsSetting: true);
                }

                if (!TryPrepareNotification(localSettings))
                {
                    return new StandaloneTaskbarWidgetsResult(
                        StandaloneTaskbarWidgetsResultKind.SettingsFailure,
                        Succeeded: false,
                        RegistryChanged: false,
                        OwnsSetting: true);
                }
                store.Set(0, RegistryValueKind.DWord);
                if (!TryMarkApplied(localSettings))
                {
                    return new StandaloneTaskbarWidgetsResult(
                        StandaloneTaskbarWidgetsResultKind.SettingsFailure,
                        Succeeded: false,
                        RegistryChanged: true,
                        IsEffectivelySuppressed: true,
                        OwnsSetting: true);
                }
                return new StandaloneTaskbarWidgetsResult(
                    StandaloneTaskbarWidgetsResultKind.Suppressed,
                    Succeeded: true,
                    RegistryChanged: true,
                    IsEffectivelySuppressed: true,
                    OwnsSetting: true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Suppressing the Windows Widgets taskbar entry failed: {ex.Message}");
                return new StandaloneTaskbarWidgetsResult(
                    StandaloneTaskbarWidgetsResultKind.RegistryFailure,
                    Succeeded: false,
                    RegistryChanged: false,
                    OwnsSetting: HasCapturedTaskbarEntry(localSettings));
            }
        }

        internal static StandaloneTaskbarWidgetsResult RestoreCore(
            IDictionary<string, object> localSettings,
            ITaskbarDaRegistryStore store)
        {
            ArgumentNullException.ThrowIfNull(localSettings);
            ArgumentNullException.ThrowIfNull(store);

            try
            {
                if (!HasCapturedTaskbarEntry(localSettings))
                {
                    return new StandaloneTaskbarWidgetsResult(
                        StandaloneTaskbarWidgetsResultKind.NoOwnership,
                        Succeeded: true,
                        RegistryChanged: false);
                }

                if (!TryReadCapturedTaskbarEntry(
                        localSettings,
                        out TaskbarEntrySnapshot snapshot,
                        out bool applied))
                {
                    return new StandaloneTaskbarWidgetsResult(
                        StandaloneTaskbarWidgetsResultKind.InvalidSnapshot,
                        Succeeded: false,
                        RegistryChanged: false,
                        OwnsSetting: true);
                }

                TaskbarDaRegistryValue current = store.Read();
                if (!applied)
                {
                    // Applied=false is deliberately not ownership: a crash may
                    // have happened on either side of the registry write. Release
                    // only our metadata and leave the current value untouched.
                    if (!TryClearCapturedTaskbarEntry(localSettings))
                    {
                        return new StandaloneTaskbarWidgetsResult(
                            StandaloneTaskbarWidgetsResultKind.SettingsFailure,
                            Succeeded: false,
                            RegistryChanged: false,
                            OwnsSetting: true);
                    }

                    return new StandaloneTaskbarWidgetsResult(
                        StandaloneTaskbarWidgetsResultKind.ExternalChangePreserved,
                        Succeeded: true,
                        RegistryChanged: false);
                }

                bool changed = false;
                StandaloneTaskbarWidgetsResultKind resultKind;
                if (IsServiceOwnedTaskbarValue(current))
                {
                    if (!TryPrepareNotification(localSettings))
                    {
                        return new StandaloneTaskbarWidgetsResult(
                            StandaloneTaskbarWidgetsResultKind.SettingsFailure,
                            Succeeded: false,
                            RegistryChanged: false,
                            OwnsSetting: true);
                    }
                    if (snapshot.WasPresent)
                    {
                        if (!WindowsWidgetsService.TryDeserializeRegistryValue(
                                snapshot.SerializedValue,
                                snapshot.Kind,
                                out object? restoredValue))
                        {
                            return new StandaloneTaskbarWidgetsResult(
                                StandaloneTaskbarWidgetsResultKind.InvalidSnapshot,
                                Succeeded: false,
                                RegistryChanged: false);
                        }

                        store.Set(restoredValue!, snapshot.Kind);
                    }
                    else
                    {
                        store.Delete();
                    }

                    changed = true;
                    resultKind = StandaloneTaskbarWidgetsResultKind.Restored;
                }
                else
                {
                    // The user or Windows changed TaskbarDa while standalone mode
                    // was active. That newer value wins; only release our marker.
                    resultKind =
                        StandaloneTaskbarWidgetsResultKind.ExternalChangePreserved;
                }

                if (!TryClearCapturedTaskbarEntry(localSettings))
                {
                    return new StandaloneTaskbarWidgetsResult(
                        StandaloneTaskbarWidgetsResultKind.SettingsFailure,
                        Succeeded: false,
                        RegistryChanged: changed,
                        OwnsSetting: true);
                }

                return new StandaloneTaskbarWidgetsResult(
                    resultKind,
                    Succeeded: true,
                    RegistryChanged: changed);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Restoring the Windows Widgets taskbar entry failed: {ex.Message}");
                return new StandaloneTaskbarWidgetsResult(
                    StandaloneTaskbarWidgetsResultKind.RegistryFailure,
                    Succeeded: false,
                    RegistryChanged: false,
                    OwnsSetting: HasCapturedTaskbarEntry(localSettings));
            }
        }

        internal static bool HasCapturedTaskbarEntry(
            IDictionary<string, object> localSettings)
        {
            ArgumentNullException.ThrowIfNull(localSettings);
            return localSettings.TryGetValue(
                       CapturedSettingKey,
                       out object? value) &&
                   value is true;
        }

        internal static bool TryReadCapturedTaskbarEntry(
            IDictionary<string, object> localSettings,
            out TaskbarEntrySnapshot snapshot)
            => TryReadCapturedTaskbarEntry(
                localSettings,
                out snapshot,
                out _);

        internal static bool TryReadCapturedTaskbarEntry(
            IDictionary<string, object> localSettings,
            out TaskbarEntrySnapshot snapshot,
            out bool applied)
        {
            ArgumentNullException.ThrowIfNull(localSettings);
            snapshot = default;
            applied = false;
            if (!HasCapturedTaskbarEntry(localSettings) ||
                !localSettings.TryGetValue(CapturedSchemaKey, out object? schemaValue) ||
                schemaValue is not string schema ||
                !string.Equals(schema, CurrentSchemaVersion, StringComparison.Ordinal) ||
                !localSettings.TryGetValue(CapturedPresentKey, out object? presentValue) ||
                presentValue is not bool wasPresent ||
                !localSettings.TryGetValue(CapturedValueKey, out object? valueValue) ||
                valueValue is not string serializedValue ||
                !localSettings.TryGetValue(CapturedKindKey, out object? kindValue) ||
                kindValue is not string kindText ||
                !localSettings.TryGetValue(CapturedAppliedKey, out object? appliedValue) ||
                appliedValue is not bool wasApplied ||
                !Enum.TryParse(kindText, ignoreCase: false, out RegistryValueKind kind) ||
                !Enum.IsDefined(kind))
            {
                return false;
            }

            if (wasPresent &&
                !WindowsWidgetsService.TryDeserializeRegistryValue(
                    serializedValue,
                    kind,
                    out _))
            {
                return false;
            }

            snapshot = new TaskbarEntrySnapshot(
                wasPresent,
                serializedValue,
                kind);
            applied = wasApplied;
            return true;
        }

        internal static void WriteCapturedTaskbarEntry(
            IDictionary<string, object> localSettings,
            TaskbarDaRegistryValue current)
        {
            ArgumentNullException.ThrowIfNull(localSettings);
            string serializedValue = string.Empty;
            if (current.IsPresent &&
                (current.Value == null ||
                 !WindowsWidgetsService.TrySerializeRegistryValue(
                     current.Value,
                     current.Kind,
                     out serializedValue)))
            {
                throw new InvalidOperationException(
                    $"TaskbarDa uses the unsupported registry type {current.Kind}.");
            }

            try
            {
                RemoveUncommittedSnapshotMetadata(localSettings);
                localSettings[CapturedSchemaKey] = CurrentSchemaVersion;
                localSettings[CapturedPresentKey] = current.IsPresent;
                localSettings[CapturedValueKey] = current.IsPresent
                    ? serializedValue
                    : string.Empty;
                localSettings[CapturedKindKey] = current.Kind.ToString();
                localSettings[CapturedAppliedKey] = false;

                // The marker is the transaction commit point and must be last.
                localSettings[CapturedSettingKey] = true;
            }
            catch
            {
                _ = TryClearCapturedTaskbarEntry(localSettings);
                throw;
            }
        }

        internal static bool TryClearCapturedTaskbarEntry(
            IDictionary<string, object> localSettings)
        {
            ArgumentNullException.ThrowIfNull(localSettings);
            try
            {
                // Remove the commit marker first so stale payload cannot authorize
                // a later process to alter the user's TaskbarDa value.
                localSettings.Remove(CapturedSettingKey);
            }
            catch
            {
                return false;
            }

            RemoveUncommittedSnapshotMetadata(localSettings);
            return true;
        }

        internal static bool IsServiceOwnedTaskbarValue(
            TaskbarDaRegistryValue value)
            => value.IsPresent &&
               value.Kind == RegistryValueKind.DWord &&
               value.Value is int number &&
               number == 0;

        internal static bool RegistryValuesEqual(
            TaskbarDaRegistryValue left,
            TaskbarDaRegistryValue right)
        {
            if (left.IsPresent != right.IsPresent)
                return false;
            if (!left.IsPresent)
                return true;
            return left.Kind == right.Kind &&
                   RegistryValueObjectsEqual(left.Value, right.Value);
        }

        private static bool RegistryValueObjectsEqual(object? left, object? right)
        {
            if (left is byte[] leftBytes && right is byte[] rightBytes)
                return leftBytes.SequenceEqual(rightBytes);
            if (left is string[] leftStrings && right is string[] rightStrings)
                return leftStrings.SequenceEqual(rightStrings, StringComparer.Ordinal);
            return Equals(left, right);
        }

        private static StandaloneTaskbarWidgetsResult RunWithRegistry(
            IDictionary<string, object> localSettings,
            Func<IDictionary<string, object>, ITaskbarDaRegistryStore,
                StandaloneTaskbarWidgetsResult> operation)
        {
            ArgumentNullException.ThrowIfNull(localSettings);
            Mutex? stateMutex = null;
            bool lockTaken = false;
            try
            {
                stateMutex = new Mutex(
                    initiallyOwned: false,
                    WindowsWidgetsService.TaskbarStateMutexName);
                lockTaken = WindowsWidgetsService.TryEnterTaskbarStateMutex(
                    stateMutex);
                if (!lockTaken)
                {
                    return new StandaloneTaskbarWidgetsResult(
                        StandaloneTaskbarWidgetsResultKind.LockUnavailable,
                        Succeeded: false,
                        RegistryChanged: false,
                        OwnsSetting: HasCapturedTaskbarEntry(localSettings),
                        NotificationPending: IsNotificationPending(localSettings));
                }

                using RegistryKey key = Registry.CurrentUser.CreateSubKey(
                        WindowsWidgetsService.TaskbarAdvancedRegistryPath,
                        writable: true)
                    ?? throw new InvalidOperationException(
                        "Taskbar settings key is unavailable.");
                var store = new RegistryTaskbarDaStore(key);
                StandaloneTaskbarWidgetsResult result = operation(
                    localSettings,
                    store);

                WindowsWidgetsService.ReleaseTaskbarStateMutex(
                    stateMutex,
                    ref lockTaken);
                string? notificationToken =
                    TryReadNotificationToken(localSettings);
                if (notificationToken != null)
                {
                    bool notified = WindowsWidgetsService
                        .BroadcastTaskbarSettingsChanged();
                    bool cleared = false;
                    if (notified)
                    {
                        bool cleanupLockTaken =
                            WindowsWidgetsService.TryEnterTaskbarStateMutex(
                                stateMutex);
                        if (cleanupLockTaken)
                        {
                            try
                            {
                                cleared = TryClearNotificationPendingIfCurrent(
                                    localSettings,
                                    notificationToken);
                            }
                            finally
                            {
                                WindowsWidgetsService.ReleaseTaskbarStateMutex(
                                    stateMutex,
                                    ref cleanupLockTaken);
                            }
                        }
                    }
                    return result with
                    {
                        NotificationPending = !notified || !cleared
                    };
                }
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Updating standalone Widgets suppression failed: {ex.Message}");
                return new StandaloneTaskbarWidgetsResult(
                    StandaloneTaskbarWidgetsResultKind.RegistryFailure,
                    Succeeded: false,
                    RegistryChanged: false,
                    OwnsSetting: HasCapturedTaskbarEntryNoThrow(localSettings),
                    NotificationPending: IsNotificationPendingNoThrow(localSettings));
            }
            finally
            {
                if (lockTaken)
                {
                    WindowsWidgetsService.ReleaseTaskbarStateMutex(
                        stateMutex!,
                        ref lockTaken);
                }

                stateMutex?.Dispose();
            }
        }

        private static void RemoveUncommittedSnapshotMetadata(
            IDictionary<string, object> localSettings)
        {
            foreach (string key in new[]
            {
                CapturedPresentKey,
                CapturedValueKey,
                CapturedKindKey,
                CapturedSchemaKey,
                CapturedAppliedKey
            })
            {
                try { localSettings.Remove(key); }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        $"Removing standalone Widgets snapshot metadata failed: {ex.Message}");
                }
            }
        }

        private static bool TryMarkApplied(
            IDictionary<string, object> localSettings)
        {
            try
            {
                localSettings[CapturedAppliedKey] = true;
                return true;
            }
            catch (Exception ex)
            {
                // The write is now ambiguous across a crash boundary. Callers
                // quarantine Applied=false and never infer ownership from DWORD 0.
                Debug.WriteLine(
                    $"Marking standalone Widgets suppression applied failed: {ex.Message}");
                return false;
            }
        }

        private static bool TryPrepareNotification(
            IDictionary<string, object> localSettings)
        {
            try
            {
                localSettings[NotificationPendingKey] =
                    Guid.NewGuid().ToString("N");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"Persisting taskbar notification retry failed: {ex.Message}");
                return false;
            }
        }

        internal static bool IsNotificationPending(
            IDictionary<string, object> localSettings)
            => TryReadNotificationToken(localSettings) != null;

        internal static string? TryReadNotificationToken(
            IDictionary<string, object> localSettings)
            => localSettings.TryGetValue(
                       NotificationPendingKey,
                       out object? value) &&
                   value is string token &&
                   token.Length == 32
                ? token
                : null;

        private static bool HasCapturedTaskbarEntryNoThrow(
            IDictionary<string, object> localSettings)
        {
            try { return HasCapturedTaskbarEntry(localSettings); }
            catch { return false; }
        }

        private static bool IsNotificationPendingNoThrow(
            IDictionary<string, object> localSettings)
        {
            try { return IsNotificationPending(localSettings); }
            catch { return false; }
        }

        internal static bool TryClearNotificationPendingIfCurrent(
            IDictionary<string, object> localSettings,
            string expectedToken)
        {
            try
            {
                string? currentToken = TryReadNotificationToken(localSettings);
                if (currentToken == null)
                    return true;
                if (!string.Equals(
                        currentToken,
                        expectedToken,
                        StringComparison.Ordinal))
                {
                    return false;
                }

                localSettings.Remove(NotificationPendingKey);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"Clearing taskbar notification retry failed: {ex.Message}");
                return false;
            }
        }

        private sealed class RegistryTaskbarDaStore : ITaskbarDaRegistryStore
        {
            private readonly RegistryKey _key;

            public RegistryTaskbarDaStore(RegistryKey key)
            {
                _key = key;
            }

            public TaskbarDaRegistryValue Read()
            {
                bool isPresent = _key.GetValueNames().Contains(
                    WindowsWidgetsService.TaskbarWidgetsValueName,
                    StringComparer.OrdinalIgnoreCase);
                if (!isPresent)
                {
                    return new TaskbarDaRegistryValue(
                        IsPresent: false,
                        Value: null,
                        RegistryValueKind.DWord);
                }

                object? value = _key.GetValue(
                    WindowsWidgetsService.TaskbarWidgetsValueName,
                    null,
                    RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (value == null)
                    throw new InvalidOperationException("TaskbarDa could not be read exactly.");
                return new TaskbarDaRegistryValue(
                    IsPresent: true,
                    value,
                    _key.GetValueKind(
                        WindowsWidgetsService.TaskbarWidgetsValueName));
            }

            public void Set(object value, RegistryValueKind kind)
                => _key.SetValue(
                    WindowsWidgetsService.TaskbarWidgetsValueName,
                    value,
                    kind);

            public void Delete()
                => _key.DeleteValue(
                    WindowsWidgetsService.TaskbarWidgetsValueName,
                    throwOnMissingValue: false);
        }
    }
}
