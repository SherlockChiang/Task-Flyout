using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Task_Flyout.Services
{
    public sealed class MailTrustStore
    {
        private readonly HashSet<string> _trustedSources = new(StringComparer.OrdinalIgnoreCase);
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TaskFlyout", "trusted_mail_sources.json");

        public MailTrustStore()
        {
            Load();
        }

        public bool IsTrusted(MailItem item)
        {
            var key = GetSourceKey(item);
            return key != null && _trustedSources.Contains(key);
        }

        public bool Trust(MailItem item)
        {
            var key = GetSourceKey(item);
            if (key == null) return false;

            var changed = _trustedSources.Add(key);
            if (changed) Save();
            return true;
        }

        public bool Untrust(MailItem item)
        {
            var key = GetSourceKey(item);
            if (key == null) return false;

            var changed = _trustedSources.Remove(key);
            if (changed) Save();
            return true;
        }

        public static string GetDisplaySource(MailItem item)
        {
            return GetSourceKey(item) ?? "未知来源";
        }

        private void Load()
        {
            try
            {
                var json = ProtectedLocalStore.ReadText(FilePath);
                if (string.IsNullOrWhiteSpace(json)) return;

                var values = JsonSerializer.Deserialize(json, AppJsonContext.Default.HashSetString);
                if (values == null) return;

                _trustedSources.Clear();
                foreach (var value in values.Where(v => !string.IsNullOrWhiteSpace(v)))
                    _trustedSources.Add(value);
            }
            catch
            {
                _trustedSources.Clear();
            }
        }

        private void Save()
        {
            var json = JsonSerializer.Serialize(_trustedSources, AppJsonContext.Default.HashSetString);
            ProtectedLocalStore.WriteText(FilePath, json);
        }

        private static string? GetSourceKey(MailItem item)
        {
            var address = ExtractAddress(item.SenderAddress);
            if (address == null)
                address = ExtractAddress(item.Sender);

            if (address == null) return null;

            var atIndex = address.LastIndexOf('@');
            if (atIndex < 0 || atIndex == address.Length - 1) return address.ToLowerInvariant();

            return address[(atIndex + 1)..].Trim().Trim('>', '.', ',').ToLowerInvariant();
        }

        private static string? ExtractAddress(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            var match = Regex.Match(value, @"[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase);
            if (match.Success) return match.Value;

            var trimmed = value.Trim().Trim('<', '>', '"', '\'');
            return trimmed.Contains('@', StringComparison.Ordinal) ? trimmed : null;
        }
    }
}
