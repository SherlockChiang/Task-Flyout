using Microsoft.Win32;
using System.Collections;
using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class WindowsWidgetsServiceTests
{
    [Fact]
    public void Snapshot_commit_marker_is_written_after_the_complete_payload()
    {
        var values = new TrackingDictionary();

        WindowsWidgetsService.WriteCapturedTaskbarEntry(values, 0, RegistryValueKind.DWord);

        Assert.Equal(
            new[]
            {
                WindowsWidgetsService.CapturedPresentKey,
                WindowsWidgetsService.CapturedValueKey,
                WindowsWidgetsService.CapturedKindKey,
                WindowsWidgetsService.CapturedSettingKey
            },
            values.SetKeys);
        Assert.True(WindowsWidgetsService.HasCapturedTaskbarEntry(values));
        Assert.True(WindowsWidgetsService.TryReadCapturedTaskbarEntry(values, out TaskbarEntrySnapshot snapshot));
        Assert.True(snapshot.WasPresent);
        Assert.Equal("0", snapshot.SerializedValue);
        Assert.Equal(RegistryValueKind.DWord, snapshot.Kind);
    }

    [Fact]
    public void Interrupted_snapshot_write_does_not_leave_an_ownership_marker()
    {
        var values = new TrackingDictionary(WindowsWidgetsService.CapturedSettingKey);

        Assert.Throws<InvalidOperationException>(() =>
            WindowsWidgetsService.WriteCapturedTaskbarEntry(values, 0, RegistryValueKind.DWord));

        Assert.Equal(WindowsWidgetsService.CapturedSettingKey, values.SetKeys[^1]);
        Assert.False(WindowsWidgetsService.HasCapturedTaskbarEntry(values));
        Assert.DoesNotContain(WindowsWidgetsService.CapturedPresentKey, values.Keys);
        Assert.DoesNotContain(WindowsWidgetsService.CapturedValueKey, values.Keys);
        Assert.DoesNotContain(WindowsWidgetsService.CapturedKindKey, values.Keys);
    }

    [Fact]
    public void Persisted_snapshot_can_be_read_by_a_later_process_instance()
    {
        var firstProcessValues = new Dictionary<string, object>();
        WindowsWidgetsService.WriteCapturedTaskbarEntry(
            firstProcessValues,
            existingValue: null,
            RegistryValueKind.DWord);
        var laterProcessValues = new Dictionary<string, object>(firstProcessValues);

        Assert.True(WindowsWidgetsService.HasCapturedTaskbarEntry(laterProcessValues));
        Assert.True(WindowsWidgetsService.TryReadCapturedTaskbarEntry(
            laterProcessValues,
            out TaskbarEntrySnapshot snapshot));
        Assert.False(snapshot.WasPresent);
    }

    [Fact]
    public void Malformed_committed_snapshot_is_rejected_instead_of_guessing_a_value()
    {
        var values = new Dictionary<string, object>
        {
            [WindowsWidgetsService.CapturedSettingKey] = true,
            [WindowsWidgetsService.CapturedPresentKey] = true,
            [WindowsWidgetsService.CapturedValueKey] = "not-a-number",
            [WindowsWidgetsService.CapturedKindKey] = nameof(RegistryValueKind.DWord)
        };

        Assert.False(WindowsWidgetsService.TryReadCapturedTaskbarEntry(values, out _));
    }

    [Fact]
    public void Ownership_requires_the_exact_dword_written_by_the_service()
    {
        Assert.True(WindowsWidgetsService.IsServiceOwnedTaskbarValue(1, RegistryValueKind.DWord));
        Assert.False(WindowsWidgetsService.IsServiceOwnedTaskbarValue(2, RegistryValueKind.DWord));
        Assert.False(WindowsWidgetsService.IsServiceOwnedTaskbarValue(1L, RegistryValueKind.QWord));
        Assert.False(WindowsWidgetsService.IsServiceOwnedTaskbarValue("1", RegistryValueKind.String));
        Assert.False(WindowsWidgetsService.IsServiceOwnedTaskbarValue(1, RegistryValueKind.QWord));
    }

    [Fact]
    public void Snapshot_round_trips_non_default_registry_value_types_without_loss()
    {
        AssertRoundTrip(new[] { "weather", "widgets" }, RegistryValueKind.MultiString);
        AssertRoundTrip(new byte[] { 0, 1, 127, 255 }, RegistryValueKind.Binary);
        AssertRoundTrip(4_294_967_296L, RegistryValueKind.QWord);
        AssertRoundTrip("%SystemRoot%", RegistryValueKind.ExpandString);
    }

    [Fact]
    public void Native_bridge_geometry_rejects_generic_or_unrelated_xaml_bridges()
    {
        var taskbar = new WindowsWidgetsService.WindowRect(0, 1040, 1920, 1080);

        Assert.True(WindowsWidgetsService.IsPlausibleNativeWidgetsBridge(
            taskbar,
            new WindowsWidgetsService.WindowRect(0, 1040, 280, 1080),
            isVisible: true));
        Assert.False(WindowsWidgetsService.IsPlausibleNativeWidgetsBridge(
            taskbar,
            new WindowsWidgetsService.WindowRect(0, 1040, 280, 1080),
            isVisible: false));
        Assert.False(WindowsWidgetsService.IsPlausibleNativeWidgetsBridge(
            taskbar,
            new WindowsWidgetsService.WindowRect(0, 900, 280, 1000),
            isVisible: true));
        Assert.False(WindowsWidgetsService.IsPlausibleNativeWidgetsBridge(
            taskbar,
            new WindowsWidgetsService.WindowRect(1000, 1040, 1280, 1080),
            isVisible: true));
        Assert.False(WindowsWidgetsService.IsPlausibleNativeWidgetsBridge(
            taskbar,
            new WindowsWidgetsService.WindowRect(0, 1040, 1920, 1080),
            isVisible: true));
    }

    [Fact]
    public void Clearing_ownership_removes_the_commit_marker_before_payload()
    {
        var values = new TrackingDictionary();
        WindowsWidgetsService.WriteCapturedTaskbarEntry(values, 0, RegistryValueKind.DWord);
        values.RemovedKeys.Clear();

        Assert.True(WindowsWidgetsService.TryClearCapturedTaskbarEntry(values));

        Assert.Equal(WindowsWidgetsService.CapturedSettingKey, values.RemovedKeys[0]);
        Assert.False(WindowsWidgetsService.HasCapturedTaskbarEntry(values));
    }

    [Fact]
    public void Failed_marker_removal_keeps_the_snapshot_for_a_later_retry()
    {
        var values = new TrackingDictionary
        {
            ThrowOnRemoveKey = WindowsWidgetsService.CapturedSettingKey
        };
        WindowsWidgetsService.WriteCapturedTaskbarEntry(values, 0, RegistryValueKind.DWord);

        Assert.False(WindowsWidgetsService.TryClearCapturedTaskbarEntry(values));

        Assert.True(WindowsWidgetsService.HasCapturedTaskbarEntry(values));
        Assert.True(WindowsWidgetsService.TryReadCapturedTaskbarEntry(values, out _));
    }

    private static void AssertRoundTrip(object original, RegistryValueKind kind)
    {
        Assert.True(WindowsWidgetsService.TrySerializeRegistryValue(original, kind, out string serialized));
        Assert.True(WindowsWidgetsService.TryDeserializeRegistryValue(serialized, kind, out object? restored));

        if (original is Array expectedArray && restored is Array actualArray)
        {
            Assert.Equal(expectedArray.Cast<object>(), actualArray.Cast<object>());
        }
        else
        {
            Assert.Equal(original, restored);
        }
    }

    private sealed class TrackingDictionary : IDictionary<string, object>
    {
        private readonly Dictionary<string, object> _values = new();
        private readonly string? _throwOnSetKey;

        public TrackingDictionary(string? throwOnSetKey = null)
        {
            _throwOnSetKey = throwOnSetKey;
        }

        public List<string> SetKeys { get; } = new();
        public List<string> RemovedKeys { get; } = new();
        public string? ThrowOnRemoveKey { get; init; }

        public object this[string key]
        {
            get => _values[key];
            set
            {
                SetKeys.Add(key);
                if (string.Equals(key, _throwOnSetKey, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Simulated interrupted settings write.");
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
        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => _values.GetEnumerator();
        public bool Remove(string key)
        {
            RemovedKeys.Add(key);
            if (string.Equals(key, ThrowOnRemoveKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Simulated interrupted settings cleanup.");
            }

            return _values.Remove(key);
        }
        public bool Remove(KeyValuePair<string, object> item) =>
            ((ICollection<KeyValuePair<string, object>>)_values).Remove(item);
        public bool TryGetValue(string key, out object value) => _values.TryGetValue(key, out value!);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
