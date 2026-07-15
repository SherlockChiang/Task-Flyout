using System;
using System.Collections.Generic;

namespace Task_Flyout.Services
{
    internal static class LocalSearchMatcher
    {
        public static bool Matches(string? query, params string?[] fields)
        {
            query = query?.Trim();
            if (string.IsNullOrEmpty(query)) return true;
            foreach (var field in fields)
            {
                if (!string.IsNullOrEmpty(field)
                    && field.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
