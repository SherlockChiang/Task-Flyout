using System;
using System.Collections.Generic;
using System.Linq;

namespace Task_Flyout.Services
{
    internal static class RecurrencePolicy
    {
        public static string ToGoogleFrequency(string? recurrenceKind) => recurrenceKind switch
        {
            "Daily" => "DAILY",
            "Weekly" => "WEEKLY",
            "Monthly" => "MONTHLY",
            "Yearly" => "YEARLY",
            _ => "DAILY"
        };

        public static string ToDisplayKindFromGoogleRRules(IEnumerable<string>? recurrence)
        {
            var rule = recurrence?.FirstOrDefault(item => item.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(rule)) return "None";

            if (rule.Contains("FREQ=DAILY", StringComparison.OrdinalIgnoreCase)) return "Daily";
            if (rule.Contains("FREQ=WEEKLY", StringComparison.OrdinalIgnoreCase)) return "Weekly";
            if (rule.Contains("FREQ=MONTHLY", StringComparison.OrdinalIgnoreCase)) return "Monthly";
            if (rule.Contains("FREQ=YEARLY", StringComparison.OrdinalIgnoreCase)) return "Yearly";
            return "None";
        }

        public static string ToDisplayKindFromMicrosoftPattern(string? patternType)
            => patternType switch
            {
                "Daily" => "Daily",
                "Weekly" => "Weekly",
                "AbsoluteMonthly" or "RelativeMonthly" => "Monthly",
                "AbsoluteYearly" or "RelativeYearly" => "Yearly",
                _ => "None"
            };
    }
}
