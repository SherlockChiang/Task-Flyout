using System.Globalization;
using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public sealed class GlobalSearchAgendaIdentityTests
{
    [Fact]
    public void Equal_remote_ids_from_two_accounts_keep_both_search_results_and_targets()
    {
        var first = new Target("account-a", "Shared meeting");
        var second = new Target("account-b", "Shared meeting");
        var groups = new[] { first, second, first }
            .GroupBy(item => Id(accountId: item.AccountId), StringComparer.Ordinal);
        var payloads = groups.ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var candidates = payloads.Select(pair => new GlobalSearchCandidate(
            pair.Key, GlobalSearchGroupKind.Calendar, pair.Value.Title, "", pair.Value.Title));

        var results = GlobalSearchPolicy.Search(candidates, "Shared meeting");

        Assert.Equal(2, results.Count);
        Assert.Same(first, payloads[results.Single(item => item.Id == Id(accountId: first.AccountId)).Id]);
        Assert.Same(second, payloads[results.Single(item => item.Id == Id(accountId: second.AccountId)).Id]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_account_id_matches_the_existing_legacy_account(string? accountId)
        => Assert.Equal(Id(accountId: "legacy-google"), Id(accountId: accountId));

    [Fact]
    public void Legacy_account_does_not_merge_with_a_new_account()
        => Assert.NotEqual(Id(accountId: null), Id(accountId: "new-account"));

    [Fact]
    public void Provider_alias_and_account_casing_follow_account_routing()
        => Assert.Equal(
            Id(provider: "Outlook", accountId: " Account-A "),
            Id(provider: "Microsoft", accountId: "account-a"));

    [Theory]
    [InlineData("provider")]
    [InlineData("calendar")]
    [InlineData("item")]
    [InlineData("date")]
    [InlineData("kind")]
    public void Each_remaining_identity_dimension_is_distinct(string dimension)
    {
        string changed = dimension switch
        {
            "provider" => Id(provider: "Microsoft"),
            "calendar" => Id(calendarId: "PRIMARY"),
            "item" => Id(itemId: "SHARED-ID"),
            "date" => Id(dateKey: "2026-09-08"),
            _ => Id(isTask: true)
        };
        Assert.NotEqual(Id(), changed);
    }

    [Theory]
    [InlineData("|")]
    [InlineData(":")]
    [InlineData("\"],[\"")]
    public void Delimiters_in_provider_owned_ids_cannot_shift_field_boundaries(string separator)
        => Assert.NotEqual(
            Id(calendarId: "a" + separator + "b", itemId: "c"),
            Id(calendarId: "a", itemId: "b" + separator + "c"));

    [Fact]
    public void Identity_is_stable_across_ui_cultures()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            string first = Id(provider: "Microsoft", accountId: "identity-i");
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.Equal(first, Id(provider: "Microsoft", accountId: "identity-i"));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    private static string Id(
        string? provider = "Google", string? accountId = "account-a",
        string? calendarId = "primary", string? itemId = "shared-id",
        string? dateKey = "2026-09-07", bool isTask = false)
        => GlobalSearchPolicy.CreateAgendaId(provider, accountId, calendarId, itemId, dateKey, isTask);

    private sealed record Target(string AccountId, string Title);
}
