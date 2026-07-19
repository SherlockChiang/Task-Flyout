using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class ProviderAuthorizationScopePolicyTests
{
    [Fact]
    public void Google_initial_consent_covers_all_current_features()
    {
        Assert.Equal(5, ProviderAuthorizationScopePolicy.GoogleAllFeatures.Distinct().Count());
        Assert.Contains("https://www.googleapis.com/auth/calendar", ProviderAuthorizationScopePolicy.GoogleAllFeatures);
        Assert.Contains("https://www.googleapis.com/auth/tasks", ProviderAuthorizationScopePolicy.GoogleAllFeatures);
        Assert.Contains("https://www.googleapis.com/auth/gmail.readonly", ProviderAuthorizationScopePolicy.GoogleAllFeatures);
        Assert.Contains("https://www.googleapis.com/auth/gmail.modify", ProviderAuthorizationScopePolicy.GoogleAllFeatures);
        Assert.Contains("https://www.googleapis.com/auth/gmail.send", ProviderAuthorizationScopePolicy.GoogleAllFeatures);
    }

    [Fact]
    public void Microsoft_initial_consent_covers_all_current_features()
    {
        Assert.Equal(5, ProviderAuthorizationScopePolicy.MicrosoftAllFeatures.Distinct().Count());
        Assert.Contains("User.Read", ProviderAuthorizationScopePolicy.MicrosoftAllFeatures);
        Assert.Contains("Calendars.ReadWrite", ProviderAuthorizationScopePolicy.MicrosoftAllFeatures);
        Assert.Contains("Tasks.ReadWrite", ProviderAuthorizationScopePolicy.MicrosoftAllFeatures);
        Assert.Contains("Mail.ReadWrite", ProviderAuthorizationScopePolicy.MicrosoftAllFeatures);
        Assert.Contains("Mail.Send", ProviderAuthorizationScopePolicy.MicrosoftAllFeatures);
        Assert.DoesNotContain("Mail.Read", ProviderAuthorizationScopePolicy.MicrosoftAllFeatures);
    }
}
