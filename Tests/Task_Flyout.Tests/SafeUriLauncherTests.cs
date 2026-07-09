using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class SafeUriLauncherTests
{
    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com/path?q=1")]
    [InlineData("HTTPS://EXAMPLE.COM")]
    public void Accepts_http_and_https(string input)
    {
        Assert.True(SafeUriLauncher.TryCreateExternalHttpUri(input, out var uri));
        Assert.NotNull(uri);
    }

    [Theory]
    [InlineData("ftp://example.com/file")]
    [InlineData("file:///c:/windows/system32")]
    [InlineData("javascript:alert(1)")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("mailto:user@example.com")]
    public void Rejects_non_http_schemes(string input)
    {
        Assert.False(SafeUriLauncher.TryCreateExternalHttpUri(input, out _));
    }

    [Theory]
    [InlineData("https://localhost/admin")]
    [InlineData("https://app.localhost/callback")]
    [InlineData("https://printer.local/status")]
    [InlineData("http://127.0.0.1:8080/")]
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://172.16.0.1/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[fc00::1]/")]
    public void Rejects_local_or_private_hosts(string input)
    {
        Assert.False(SafeUriLauncher.TryCreateExternalHttpUri(input, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    [InlineData("example.com")] // no scheme -> not an absolute URI
    public void Rejects_empty_or_non_absolute(string? input)
    {
        Assert.False(SafeUriLauncher.TryCreateExternalHttpUri(input, out _));
    }

    [Fact]
    public void Rejects_overlong_uri()
    {
        var huge = "https://example.com/" + new string('a', 5000);
        Assert.False(SafeUriLauncher.TryCreateExternalHttpUri(huge, out _));
    }
}
