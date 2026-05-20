using Microsoft.Windows.ApplicationModel.Resources;
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
        private readonly ResourceLoader _loader = new();
        private const string StoreScope = "mail";
        private const string TrustedSourcesKey = "trusted_sources";

        public MailTrustStore()
        {
            Load();
        }

        public bool IsTrusted(MailItem item)
        {
            var address = GetSourceKey(item);
            if (address == null) return false;

            if (_trustedSources.Contains(address))
                return true;

            var domain = GetDomainKey(address);
            return domain != null && _trustedSources.Contains(domain);
        }

        public bool Trust(MailItem item)
        {
            var address = GetSourceKey(item);
            if (address == null) return false;

            var changed = _trustedSources.Add(address);
            if (changed) Save();
            return true;
        }

        public bool Untrust(MailItem item)
        {
            var address = GetSourceKey(item);
            if (address == null) return false;

            var changed = _trustedSources.Remove(address);
            if (changed) Save();
            return true;
        }

        public bool TrustDomain(MailItem item)
        {
            var address = GetSourceKey(item);
            var domain = address != null ? GetDomainKey(address) : null;
            if (domain == null) return false;

            var changed = _trustedSources.Add(domain);
            if (changed) Save();
            return true;
        }

        public bool UntrustDomain(MailItem item)
        {
            var address = GetSourceKey(item);
            var domain = address != null ? GetDomainKey(address) : null;
            if (domain == null) return false;

            var changed = _trustedSources.Remove(domain);
            if (changed) Save();
            return true;
        }

        public bool IsDomainTrusted(MailItem item)
        {
            var address = GetSourceKey(item);
            var domain = address != null ? GetDomainKey(address) : null;
            return domain != null && _trustedSources.Contains(domain);
        }

        public string GetDisplaySource(MailItem item)
        {
            return GetSourceKey(item) ?? (_loader.GetString("TextUnknownSource") ?? "Unknown source");
        }

        public string? GetDomain(MailItem item)
        {
            var address = GetSourceKey(item);
            return address != null ? GetDomainKey(address) : null;
        }

        private void Load()
        {
            try
            {
                var json = LocalSqliteStore.ReadProtectedText(StoreScope, TrustedSourcesKey);
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
            LocalSqliteStore.WriteProtectedText(StoreScope, TrustedSourcesKey, json);
        }

        private static string? GetSourceKey(MailItem item)
        {
            var address = ExtractAddress(item.SenderAddress);
            if (address == null)
                address = ExtractAddress(item.Sender);

            return address?.ToLowerInvariant();
        }

        private static string? GetDomainKey(string address)
        {
            var atIndex = address.LastIndexOf('@');
            if (atIndex < 0 || atIndex == address.Length - 1) return null;
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
