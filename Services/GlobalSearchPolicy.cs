using System;
using System.Collections.Generic;
using System.Linq;

namespace Task_Flyout.Services
{
    public enum GlobalSearchGroupKind
    {
        Commands,
        Tasks,
        Calendar,
        Mail,
        Rss
    }

    public sealed record GlobalSearchCandidate(
        string Id,
        GlobalSearchGroupKind Group,
        string Title,
        string Detail,
        string SearchText,
        DateTimeOffset? SortTime = null);

    public static class GlobalSearchPolicy
    {
        public const int ContentGroupCap = 8;

        public static IReadOnlyList<GlobalSearchCandidate> Search(
            IEnumerable<GlobalSearchCandidate> candidates,
            string? query)
        {
            query = query?.Trim() ?? "";
            var source = candidates ?? Array.Empty<GlobalSearchCandidate>();
            if (query.Length == 0)
            {
                return source
                    .Where(candidate => candidate.Group == GlobalSearchGroupKind.Commands)
                    .OrderBy(candidate => candidate.Id, StringComparer.Ordinal)
                    .ToList();
            }

            return source
                .Select(candidate => (Candidate: candidate, Score: Score(candidate, query)))
                .Where(match => match.Score > 0)
                .GroupBy(match => match.Candidate.Group)
                .OrderBy(group => group.Key)
                .SelectMany(group => group
                    .OrderByDescending(match => match.Score)
                    .ThenByDescending(match => match.Candidate.SortTime)
                    .ThenBy(match => match.Candidate.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(match => match.Candidate.Id, StringComparer.Ordinal)
                    .Take(group.Key == GlobalSearchGroupKind.Commands ? int.MaxValue : ContentGroupCap)
                    .Select(match => match.Candidate))
                .ToList();
        }

        private static int Score(GlobalSearchCandidate candidate, string query)
        {
            if (string.Equals(candidate.Title, query, StringComparison.CurrentCultureIgnoreCase)) return 1000;
            if (candidate.Title.StartsWith(query, StringComparison.CurrentCultureIgnoreCase)) return 800;
            if (candidate.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)) return 600;
            if (candidate.SearchText.Contains(query, StringComparison.CurrentCultureIgnoreCase)) return 400;

            var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return tokens.Length > 1 && tokens.All(token => candidate.SearchText.Contains(token, StringComparison.CurrentCultureIgnoreCase))
                ? 200 + tokens.Length
                : 0;
        }
    }
}
