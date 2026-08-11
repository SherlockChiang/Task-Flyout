using Microsoft.Win32;
using System.Collections;
using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class StandaloneTaskbarWidgetsServiceTests
{
    [Fact]
    public void Suppression_captures_exact_value_before_writing_dword_zero()
    {
        var values = new TrackingDictionary();
        var store = new FakeStore(1, RegistryValueKind.DWord);

        StandaloneTaskbarWidgetsResult result =
            StandaloneTaskbarWidgetsService.EnsureSuppressedCore(values, store);

        Assert.True(result.Succeeded);
        Assert.True(result.RegistryChanged);
        Assert.Equal(StandaloneTaskbarWidgetsResultKind.Suppressed, result.Kind);
        Assert.Equal(0, store.Value);
        Assert.Equal(RegistryValueKind.DWord, store.Kind);
        Assert.True(StandaloneTaskbarWidgetsService.IsNotificationPending(values));
        Assert.Equal(StandaloneTaskbarWidgetsService.CapturedSettingKey, values.SetKeys[^3]);
        Assert.True(values.SetKeys.IndexOf(
            StandaloneTaskbarWidgetsService.CapturedAppliedKey) <
            values.SetKeys.IndexOf(
                StandaloneTaskbarWidgetsService.CapturedSettingKey));
        Assert.Equal(
            StandaloneTaskbarWidgetsService.NotificationPendingKey,
            values.SetKeys[^2]);
        Assert.Equal(
            StandaloneTaskbarWidgetsService.CapturedAppliedKey,
            values.SetKeys[^1]);
        Assert.True(StandaloneTaskbarWidgetsService.TryReadCapturedTaskbarEntry(
            values,
            out TaskbarEntrySnapshot snapshot));
        Assert.True(snapshot.WasPresent);
        Assert.Equal("1", snapshot.SerializedValue);
        Assert.Equal(RegistryValueKind.DWord, snapshot.Kind);
    }

    [Fact]
    public void Already_hidden_dword_is_not_claimed_or_rewritten()
    {
        var values = new Dictionary<string, object>();
        var store = new FakeStore(0, RegistryValueKind.DWord);

        StandaloneTaskbarWidgetsResult result =
            StandaloneTaskbarWidgetsService.EnsureSuppressedCore(values, store);

        Assert.True(result.Succeeded);
        Assert.False(result.RegistryChanged);
        Assert.Equal(
            StandaloneTaskbarWidgetsResultKind.AlreadySuppressedUnowned,
            result.Kind);
        Assert.False(StandaloneTaskbarWidgetsService.HasCapturedTaskbarEntry(values));
        Assert.False(StandaloneTaskbarWidgetsService.IsNotificationPending(values));
        Assert.Empty(store.Writes);
    }

    [Fact]
    public void Only_an_exact_dword_zero_counts_as_existing_suppression()
    {
        Assert.True(StandaloneTaskbarWidgetsService.IsServiceOwnedTaskbarValue(
            new TaskbarDaRegistryValue(true, 0, RegistryValueKind.DWord)));
        Assert.False(StandaloneTaskbarWidgetsService.IsServiceOwnedTaskbarValue(
            new TaskbarDaRegistryValue(true, 0L, RegistryValueKind.QWord)));
        Assert.False(StandaloneTaskbarWidgetsService.IsServiceOwnedTaskbarValue(
            new TaskbarDaRegistryValue(true, "0", RegistryValueKind.String)));
        Assert.False(StandaloneTaskbarWidgetsService.IsServiceOwnedTaskbarValue(
            new TaskbarDaRegistryValue(false, null, RegistryValueKind.DWord)));
    }

    [Fact]
    public void Interrupted_snapshot_commit_never_reaches_the_registry()
    {
        var values = new TrackingDictionary
        {
            ThrowOnSetKey = StandaloneTaskbarWidgetsService.CapturedSettingKey
        };
        var store = new FakeStore(1, RegistryValueKind.DWord);

        StandaloneTaskbarWidgetsResult result =
            StandaloneTaskbarWidgetsService.EnsureSuppressedCore(values, store);

        Assert.False(result.Succeeded);
        Assert.Empty(store.Writes);
        Assert.Equal(1, store.Value);
        Assert.False(StandaloneTaskbarWidgetsService.HasCapturedTaskbarEntry(values));
    }

    [Fact]
    public void Notification_lease_failure_blocks_the_registry_mutation()
    {
        var values = new TrackingDictionary
        {
            ThrowOnSetKey = StandaloneTaskbarWidgetsService.NotificationPendingKey
        };
        var store = new FakeStore(1, RegistryValueKind.DWord);

        StandaloneTaskbarWidgetsResult result =
            StandaloneTaskbarWidgetsService.EnsureSuppressedCore(values, store);

        Assert.False(result.Succeeded);
        Assert.Equal(StandaloneTaskbarWidgetsResultKind.SettingsFailure, result.Kind);
        Assert.Empty(store.Writes);
        Assert.Equal(1, store.Value);
        Assert.True(result.RequiresRecovery);
        Assert.True(StandaloneTaskbarWidgetsService.HasCapturedTaskbarEntry(values));
    }

    [Fact]
    public void Notification_lease_is_cleared_only_by_its_own_generation()
    {
        const string first = "11111111111111111111111111111111";
        const string second = "22222222222222222222222222222222";
        var values = new Dictionary<string, object>
        {
            [StandaloneTaskbarWidgetsService.NotificationPendingKey] = second
        };

        Assert.False(
            StandaloneTaskbarWidgetsService.TryClearNotificationPendingIfCurrent(
                values,
                first));
        Assert.Equal(
            second,
            StandaloneTaskbarWidgetsService.TryReadNotificationToken(values));
        Assert.True(
            StandaloneTaskbarWidgetsService.TryClearNotificationPendingIfCurrent(
                values,
                second));
        Assert.False(StandaloneTaskbarWidgetsService.IsNotificationPending(values));
    }

    [Fact]
    public void Pending_notification_alone_is_a_recovery_obligation()
    {
        var result = new StandaloneTaskbarWidgetsResult(
            StandaloneTaskbarWidgetsResultKind.NoOwnership,
            Succeeded: true,
            RegistryChanged: false,
            NotificationPending: true);

        Assert.True(result.RequiresRecovery);
    }

    [Fact]
    public void Missing_value_is_restored_by_deleting_the_owned_zero()
    {
        var values = new Dictionary<string, object>();
        var store = new FakeStore();
        Assert.True(StandaloneTaskbarWidgetsService
            .EnsureSuppressedCore(values, store).Succeeded);

        StandaloneTaskbarWidgetsResult result =
            StandaloneTaskbarWidgetsService.RestoreCore(values, store);

        Assert.True(result.Succeeded);
        Assert.True(result.RegistryChanged);
        Assert.Equal(StandaloneTaskbarWidgetsResultKind.Restored, result.Kind);
        Assert.False(store.IsPresent);
        Assert.Equal(1, store.DeleteCount);
        Assert.False(StandaloneTaskbarWidgetsService.HasCapturedTaskbarEntry(values));
        Assert.True(StandaloneTaskbarWidgetsService.IsNotificationPending(values));
    }

    [Fact]
    public void Restore_round_trips_non_default_original_type_exactly()
    {
        var values = new Dictionary<string, object>();
        var original = new[] { "weather", "widgets" };
        var store = new FakeStore(original, RegistryValueKind.MultiString);
        Assert.True(StandaloneTaskbarWidgetsService
            .EnsureSuppressedCore(values, store).Succeeded);

        StandaloneTaskbarWidgetsResult result =
            StandaloneTaskbarWidgetsService.RestoreCore(values, store);

        Assert.True(result.Succeeded);
        Assert.Equal(RegistryValueKind.MultiString, store.Kind);
        Assert.Equal(original, Assert.IsType<string[]>(store.Value));
    }

    [Fact]
    public void External_change_is_preserved_and_not_overwritten_by_reapply()
    {
        var values = new Dictionary<string, object>();
        var store = new FakeStore(1, RegistryValueKind.DWord);
        Assert.True(StandaloneTaskbarWidgetsService
            .EnsureSuppressedCore(values, store).Succeeded);
        store.ReplaceExternally(7, RegistryValueKind.DWord);
        store.Writes.Clear();

        StandaloneTaskbarWidgetsResult reapply =
            StandaloneTaskbarWidgetsService.EnsureSuppressedCore(values, store);

        Assert.False(reapply.Succeeded);
        Assert.Equal(
            StandaloneTaskbarWidgetsResultKind.ExternalChangePreserved,
            reapply.Kind);
        Assert.Empty(store.Writes);
        Assert.True(StandaloneTaskbarWidgetsService.HasCapturedTaskbarEntry(values));

        StandaloneTaskbarWidgetsResult restore =
            StandaloneTaskbarWidgetsService.RestoreCore(values, store);
        Assert.True(restore.Succeeded);
        Assert.False(restore.RegistryChanged);
        Assert.Equal(7, store.Value);
        Assert.False(StandaloneTaskbarWidgetsService.HasCapturedTaskbarEntry(values));
    }

    [Fact]
    public void Failed_registry_write_keeps_snapshot_for_safe_recovery()
    {
        var values = new Dictionary<string, object>();
        var store = new FakeStore(1, RegistryValueKind.DWord)
        {
            ThrowOnSet = true
        };

        StandaloneTaskbarWidgetsResult result =
            StandaloneTaskbarWidgetsService.EnsureSuppressedCore(values, store);

        Assert.False(result.Succeeded);
        Assert.Equal(StandaloneTaskbarWidgetsResultKind.RegistryFailure, result.Kind);
        Assert.True(StandaloneTaskbarWidgetsService.HasCapturedTaskbarEntry(values));
        Assert.Equal(1, store.Value);
    }

    [Fact]
    public void Invalid_committed_snapshot_is_quarantined_without_registry_write()
    {
        var values = new Dictionary<string, object>
        {
            [StandaloneTaskbarWidgetsService.CapturedSettingKey] = true,
            [StandaloneTaskbarWidgetsService.CapturedSchemaKey] =
                StandaloneTaskbarWidgetsService.CurrentSchemaVersion,
            [StandaloneTaskbarWidgetsService.CapturedPresentKey] = true,
            [StandaloneTaskbarWidgetsService.CapturedValueKey] = "invalid",
            [StandaloneTaskbarWidgetsService.CapturedKindKey] =
                nameof(RegistryValueKind.DWord),
            [StandaloneTaskbarWidgetsService.CapturedAppliedKey] = true
        };
        var store = new FakeStore(0, RegistryValueKind.DWord);

        StandaloneTaskbarWidgetsResult result =
            StandaloneTaskbarWidgetsService.RestoreCore(values, store);

        Assert.False(result.Succeeded);
        Assert.Equal(StandaloneTaskbarWidgetsResultKind.InvalidSnapshot, result.Kind);
        Assert.Empty(store.Writes);
        Assert.True(StandaloneTaskbarWidgetsService.HasCapturedTaskbarEntry(values));
    }

    [Fact]
    public void Prepared_snapshot_never_replays_an_ambiguous_registry_write()
    {
        var values = new Dictionary<string, object>();
        var original = new TaskbarDaRegistryValue(
            IsPresent: true,
            Value: 1,
            RegistryValueKind.DWord);
        StandaloneTaskbarWidgetsService.WriteCapturedTaskbarEntry(values, original);
        var store = new FakeStore(1, RegistryValueKind.DWord);

        StandaloneTaskbarWidgetsResult result =
            StandaloneTaskbarWidgetsService.EnsureSuppressedCore(values, store);

        Assert.False(result.Succeeded);
        Assert.Equal(
            StandaloneTaskbarWidgetsResultKind.ExternalChangePreserved,
            result.Kind);
        Assert.Equal(1, store.Value);
        Assert.Empty(store.Writes);
        Assert.True(StandaloneTaskbarWidgetsService.TryReadCapturedTaskbarEntry(
            values,
            out _,
            out bool applied));
        Assert.False(applied);
    }

    [Fact]
    public void Failed_applied_marker_is_quarantined_without_rewriting_taskbar_da()
    {
        var values = new TrackingDictionary
        {
            ThrowOnAppliedTrue = true
        };
        var store = new FakeStore(1, RegistryValueKind.DWord);

        StandaloneTaskbarWidgetsResult first =
            StandaloneTaskbarWidgetsService.EnsureSuppressedCore(values, store);

        Assert.False(first.Succeeded);
        Assert.Equal(
            StandaloneTaskbarWidgetsResultKind.SettingsFailure,
            first.Kind);
        Assert.True(first.RegistryChanged);
        Assert.True(first.IsEffectivelySuppressed);
        Assert.True(first.OwnsSetting);
        Assert.Single(store.Writes);

        store.Writes.Clear();
        StandaloneTaskbarWidgetsResult retry =
            StandaloneTaskbarWidgetsService.EnsureSuppressedCore(values, store);
        Assert.Equal(
            StandaloneTaskbarWidgetsResultKind.ExternalChangePreserved,
            retry.Kind);
        Assert.False(retry.Succeeded);
        Assert.Empty(store.Writes);

        store.ReplaceExternally(1, RegistryValueKind.DWord);
        StandaloneTaskbarWidgetsResult afterExternalRestore =
            StandaloneTaskbarWidgetsService.EnsureSuppressedCore(values, store);
        Assert.Equal(
            StandaloneTaskbarWidgetsResultKind.ExternalChangePreserved,
            afterExternalRestore.Kind);
        Assert.Empty(store.Writes);
    }

    [Fact]
    public void Restore_of_unconfirmed_snapshot_releases_metadata_only()
    {
        var values = new Dictionary<string, object>();
        StandaloneTaskbarWidgetsService.WriteCapturedTaskbarEntry(
            values,
            new TaskbarDaRegistryValue(
                IsPresent: true,
                Value: 1,
                RegistryValueKind.DWord));
        var store = new FakeStore(0, RegistryValueKind.DWord);

        StandaloneTaskbarWidgetsResult result =
            StandaloneTaskbarWidgetsService.RestoreCore(values, store);

        Assert.True(result.Succeeded);
        Assert.Equal(
            StandaloneTaskbarWidgetsResultKind.ExternalChangePreserved,
            result.Kind);
        Assert.Equal(0, store.Value);
        Assert.Empty(store.Writes);
        Assert.False(StandaloneTaskbarWidgetsService.HasCapturedTaskbarEntry(values));
    }

    [Fact]
    public void Compare_before_write_prevents_overwriting_a_racing_change()
    {
        var values = new Dictionary<string, object>();
        var store = new FakeStore(1, RegistryValueKind.DWord)
        {
            ReplaceAfterFirstRead = new TaskbarDaRegistryValue(
                IsPresent: true,
                Value: 9,
                RegistryValueKind.DWord)
        };

        StandaloneTaskbarWidgetsResult result =
            StandaloneTaskbarWidgetsService.EnsureSuppressedCore(values, store);

        Assert.False(result.Succeeded);
        Assert.Equal(
            StandaloneTaskbarWidgetsResultKind.ExternalChangePreserved,
            result.Kind);
        Assert.Empty(store.Writes);
        Assert.Equal(9, store.Value);
        Assert.True(StandaloneTaskbarWidgetsService.HasCapturedTaskbarEntry(values));
    }

    [Fact]
    public void Existing_windows_widgets_owner_blocks_suppression()
    {
        var values = new Dictionary<string, object>();
        WindowsWidgetsService.WriteCapturedTaskbarEntry(
            values,
            existingValue: 0,
            RegistryValueKind.DWord);
        var store = new FakeStore(1, RegistryValueKind.DWord);

        StandaloneTaskbarWidgetsResult result =
            StandaloneTaskbarWidgetsService.EnsureSuppressedCore(values, store);

        Assert.False(result.Succeeded);
        Assert.Equal(
            StandaloneTaskbarWidgetsResultKind.ConflictingOwner,
            result.Kind);
        Assert.Empty(store.Writes);
    }

    [Fact]
    public void Failed_marker_clear_is_retriable_after_the_value_was_restored()
    {
        var values = new TrackingDictionary();
        var store = new FakeStore(1, RegistryValueKind.DWord);
        Assert.True(StandaloneTaskbarWidgetsService
            .EnsureSuppressedCore(values, store).Succeeded);
        values.ThrowOnRemoveKey =
            StandaloneTaskbarWidgetsService.CapturedSettingKey;

        StandaloneTaskbarWidgetsResult first =
            StandaloneTaskbarWidgetsService.RestoreCore(values, store);

        Assert.False(first.Succeeded);
        Assert.True(first.RegistryChanged);
        Assert.Equal(1, store.Value);
        Assert.True(StandaloneTaskbarWidgetsService.HasCapturedTaskbarEntry(values));

        values.ThrowOnRemoveKey = null;
        StandaloneTaskbarWidgetsResult retry =
            StandaloneTaskbarWidgetsService.RestoreCore(values, store);
        Assert.True(retry.Succeeded);
        Assert.False(retry.RegistryChanged);
        Assert.Equal(
            StandaloneTaskbarWidgetsResultKind.ExternalChangePreserved,
            retry.Kind);
        Assert.Equal(1, store.Value);
    }

    private sealed class FakeStore : ITaskbarDaRegistryStore
    {
        public FakeStore()
        {
            IsPresent = false;
            Kind = RegistryValueKind.DWord;
        }

        public FakeStore(object value, RegistryValueKind kind)
        {
            IsPresent = true;
            Value = value;
            Kind = kind;
        }

        public bool IsPresent { get; private set; }
        public object? Value { get; private set; }
        public RegistryValueKind Kind { get; private set; }
        public bool ThrowOnSet { get; init; }
        public int DeleteCount { get; private set; }
        public TaskbarDaRegistryValue? ReplaceAfterFirstRead { get; init; }
        public List<(object Value, RegistryValueKind Kind)> Writes { get; } = new();
        private int _readCount;

        public TaskbarDaRegistryValue Read()
        {
            _readCount++;
            if (_readCount == 2 && ReplaceAfterFirstRead is TaskbarDaRegistryValue replacement)
            {
                IsPresent = replacement.IsPresent;
                Value = replacement.Value;
                Kind = replacement.Kind;
            }
            return new TaskbarDaRegistryValue(IsPresent, Value, Kind);
        }

        public void Set(object value, RegistryValueKind kind)
        {
            if (ThrowOnSet)
                throw new InvalidOperationException("Simulated registry failure.");
            IsPresent = true;
            Value = value;
            Kind = kind;
            Writes.Add((value, kind));
        }

        public void Delete()
        {
            IsPresent = false;
            Value = null;
            Kind = RegistryValueKind.DWord;
            DeleteCount++;
        }

        public void ReplaceExternally(object value, RegistryValueKind kind)
        {
            IsPresent = true;
            Value = value;
            Kind = kind;
        }
    }

    private sealed class TrackingDictionary : IDictionary<string, object>
    {
        private readonly Dictionary<string, object> _values = new();

        public List<string> SetKeys { get; } = new();
        public string? ThrowOnSetKey { get; set; }
        public bool ThrowOnAppliedTrue { get; set; }
        public string? ThrowOnRemoveKey { get; set; }

        public object this[string key]
        {
            get => _values[key];
            set
            {
                SetKeys.Add(key);
                if (string.Equals(key, ThrowOnSetKey, StringComparison.Ordinal))
                    throw new InvalidOperationException("Simulated settings failure.");
                if (ThrowOnAppliedTrue &&
                    string.Equals(
                        key,
                        StandaloneTaskbarWidgetsService.CapturedAppliedKey,
                        StringComparison.Ordinal) &&
                    value is true)
                {
                    throw new InvalidOperationException(
                        "Simulated applied marker failure.");
                }
                _values[key] = value;
            }
        }

        public ICollection<string> Keys => _values.Keys;
        public ICollection<object> Values => _values.Values;
        public int Count => _values.Count;
        public bool IsReadOnly => false;
        public void Add(string key, object value) => _values.Add(key, value);
        public void Add(KeyValuePair<string, object> item) =>
            ((ICollection<KeyValuePair<string, object>>)_values).Add(item);
        public void Clear() => _values.Clear();
        public bool Contains(KeyValuePair<string, object> item) =>
            ((ICollection<KeyValuePair<string, object>>)_values).Contains(item);
        public bool ContainsKey(string key) => _values.ContainsKey(key);
        public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex) =>
            ((ICollection<KeyValuePair<string, object>>)_values).CopyTo(array, arrayIndex);
        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() =>
            _values.GetEnumerator();
        public bool Remove(string key)
        {
            if (string.Equals(key, ThrowOnRemoveKey, StringComparison.Ordinal))
                throw new InvalidOperationException("Simulated settings failure.");
            return _values.Remove(key);
        }
        public bool Remove(KeyValuePair<string, object> item) =>
            ((ICollection<KeyValuePair<string, object>>)_values).Remove(item);
        public bool TryGetValue(string key, out object value) =>
            _values.TryGetValue(key, out value!);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
