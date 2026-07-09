using System;
using System.Collections.Generic;

namespace Task_Flyout.Services
{
    internal static class SyncPaginationPolicy
    {
        public static string? GetNextPageToken(string? currentToken, string? nextToken, ISet<string> seenTokens)
        {
            if (string.IsNullOrWhiteSpace(nextToken))
                return null;

            if (string.Equals(currentToken, nextToken, StringComparison.Ordinal))
                return null;

            return seenTokens.Add(nextToken) ? nextToken : null;
        }
    }
}
