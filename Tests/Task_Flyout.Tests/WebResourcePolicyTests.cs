using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class WebResourcePolicyTests
{
    [Theory]
    [InlineData("https://example.com/image.png")]
    [InlineData("about:blank")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("data:text/html,<p>x</p>")]
    public void Embedded_policy_allows_expected_safe_resources(string uri)
    {
        Assert.True(WebResourcePolicy.IsAllowedEmbeddedResource(uri, allowInsecureHttp: false));
    }

    [Theory]
    [InlineData("file:///c:/windows/win.ini")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/javascript,alert(1)")]
    [InlineData("data:application/svg+xml,<svg></svg>")]
    [InlineData("not a uri")]
    public void Embedded_policy_blocks_unsafe_resources(string uri)
    {
        Assert.False(WebResourcePolicy.IsAllowedEmbeddedResource(uri, allowInsecureHttp: false));
    }

    [Theory]
    [InlineData("https://localhost/a.png")]
    [InlineData("https://127.0.0.1/a.png")]
    [InlineData("https://10.0.0.1/a.png")]
    [InlineData("https://printer.local/a.png")]
    [InlineData("http://localhost/a.png")]
    public void Embedded_policy_blocks_private_and_local_remote_hosts(string uri)
    {
        Assert.False(WebResourcePolicy.IsAllowedEmbeddedResource(uri, allowInsecureHttp: true));
    }

    [Fact]
    public void Embedded_policy_allows_http_only_when_setting_enabled()
    {
        Assert.False(WebResourcePolicy.IsAllowedEmbeddedResource("http://example.com/a.png", allowInsecureHttp: false));
        Assert.True(WebResourcePolicy.IsAllowedEmbeddedResource("http://example.com/a.png", allowInsecureHttp: true));
    }

    [Theory]
    [InlineData("about:blank")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("data:text/html,<p>x</p>")]
    public void Rss_non_remote_policy_allows_only_about_and_safe_data(string uri)
    {
        Assert.True(WebResourcePolicy.IsAllowedRssNonRemoteResource(uri));
    }

    [Theory]
    [InlineData("https://example.com/image.png")]
    [InlineData("http://example.com/image.png")]
    [InlineData("file:///c:/x")]
    [InlineData("data:text/javascript,alert(1)")]
    public void Rss_non_remote_policy_blocks_remote_and_unsafe_data(string uri)
    {
        Assert.False(WebResourcePolicy.IsAllowedRssNonRemoteResource(uri));
    }

    [Theory]
    [InlineData("https://example.com/image.png")]
    [InlineData("https://8.8.8.8/image.png")]
    public void Rss_remote_policy_allows_public_https(string uri)
    {
        Assert.True(WebResourcePolicy.IsAllowedRssRemoteResource(uri));
    }

    [Theory]
    [InlineData("http://example.com/image.png")]
    [InlineData("https://localhost/image.png")]
    [InlineData("https://127.0.0.1/image.png")]
    [InlineData("https://10.0.0.1/image.png")]
    [InlineData("https://printer.local/image.png")]
    public void Rss_remote_policy_blocks_non_https_and_private_hosts(string uri)
    {
        Assert.False(WebResourcePolicy.IsAllowedRssRemoteResource(uri));
    }

    [Theory]
    [InlineData("https://example.com/image.png")]
    [InlineData("https://8.8.8.8/image.png")]
    public void Mail_remote_image_policy_allows_public_https(string uri)
    {
        Assert.True(WebResourcePolicy.ShouldProxyMailRemoteImage(uri));
    }

    [Theory]
    [InlineData("http://example.com/image.png")]
    [InlineData("https://localhost/image.png")]
    [InlineData("https://10.0.0.1/image.png")]
    [InlineData("file:///c:/image.png")]
    public void Mail_remote_image_policy_blocks_non_public_https(string uri)
    {
        Assert.False(WebResourcePolicy.ShouldProxyMailRemoteImage(uri));
    }
}
