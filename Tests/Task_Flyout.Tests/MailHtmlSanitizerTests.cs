using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class MailHtmlSanitizerTests
{
    [Theory]
    [InlineData("<p>Hi</p><script>steal()</script>")]
    [InlineData("<p>Hi</p><script src='https://evil/x.js'></script>")]
    [InlineData("<iframe src='https://evil'></iframe><p>Hi</p>")]
    public void Untrusted_strips_script_and_iframe(string html)
    {
        var result = MailHtmlSanitizer.SanitizeUntrusted(html);
        Assert.DoesNotContain("<script", result, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", result, System.StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("<svg:svg onload=\"steal()\"><svg:script>steal()</svg:script></svg:svg><p>Hi</p>")]
    [InlineData("<x:iframe src=\"https://evil.example\"></x:iframe><p>Hi</p>")]
    [InlineData("<math:form action=\"https://evil.example\"><math:input></math:input></math:form><p>Hi</p>")]
    public void Strips_namespaced_dangerous_tags(string html)
    {
        var untrusted = MailHtmlSanitizer.SanitizeUntrusted(html);
        var trusted = MailHtmlSanitizer.SanitizeTrusted(html);

        Assert.DoesNotContain(":svg", untrusted, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(":script", untrusted, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(":iframe", untrusted, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(":form", untrusted, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(":svg", trusted, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(":script", trusted, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(":iframe", trusted, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(":form", trusted, System.StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("<script src=\"https://evil.example/x.js\"")]
    [InlineData("<script>alert(1)<p>Hi</p>")]
    [InlineData("<iframe src=\"https://evil.example\"><p>Hi</p>")]
    [InlineData("<svg onload=\"steal()\"><p>Hi</p>")]
    public void Malformed_dangerous_tags_render_inertly(string html)
    {
        var untrusted = MailHtmlSanitizer.SanitizeUntrusted(html);
        var trusted = MailHtmlSanitizer.SanitizeTrusted(html);

        Assert.DoesNotContain("<script", untrusted, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", untrusted, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<svg", untrusted, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onload", untrusted, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", trusted, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", trusted, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<svg", trusted, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onload", trusted, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Untrusted_strips_inline_event_handlers()
    {
        var result = MailHtmlSanitizer.SanitizeUntrusted("<img src=\"cid:1\" onerror=\"steal()\">");
        Assert.DoesNotContain("onerror", result, System.StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("<a href=\"javascript:steal()\">x</a>")]
    [InlineData("<a href=\"vbscript:msgbox()\">x</a>")]
    public void Untrusted_neutralizes_script_uris(string html)
    {
        var result = MailHtmlSanitizer.SanitizeUntrusted(html);
        Assert.DoesNotContain("javascript:", result, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vbscript:", result, System.StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("<a href=\"jav&#x61;script:steal()\">x</a>")]
    [InlineData("<a href='java&#115;cript:steal()'>x</a>")]
    [InlineData("<img src=jav&#x61;script:steal()>")]
    [InlineData("<form action=\"dat&#x61;:text/html,<script>x</script>\"></form>")]
    public void Neutralizes_entity_encoded_dangerous_urls(string html)
    {
        var untrusted = MailHtmlSanitizer.SanitizeUntrusted(html);
        var trusted = MailHtmlSanitizer.SanitizeTrusted(html);

        Assert.DoesNotContain("jav&#", untrusted, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("java&#", untrusted, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dat&#", untrusted, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jav&#", trusted, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("java&#", trusted, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dat&#", trusted, System.StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("<a href=\"java&#x0A;script:steal()\">x</a>")]
    [InlineData("<a href='vb&#x09;script:msgbox()'>x</a>")]
    [InlineData("<a href=\"da&#x0D;ta:text/html,<script>x</script>\">x</a>")]
    public void Neutralizes_whitespace_obfuscated_dangerous_url_schemes(string html)
    {
        var untrusted = MailHtmlSanitizer.SanitizeUntrusted(html);
        var trusted = MailHtmlSanitizer.SanitizeTrusted(html);

        Assert.DoesNotContain("script:", untrusted, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:", untrusted, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("script:", trusted, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:", trusted, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Untrusted_strips_remote_image_tracking_pixels()
    {
        var result = MailHtmlSanitizer.SanitizeUntrusted("<p>Hi</p><img src=\"https://tracker.example/p.gif\">");
        Assert.DoesNotContain("tracker.example", result);
    }

    [Theory]
    [InlineData("<img srcset=\"cid:logo 1x, https://tracker.example/logo.png 2x\" src=\"cid:logo\">")]
    [InlineData("<img srcset=cid:logo,https://tracker.example/logo.png src=cid:logo>")]
    public void Untrusted_strips_srcset_when_any_candidate_is_remote(string html)
    {
        var result = MailHtmlSanitizer.SanitizeUntrusted(html);
        Assert.DoesNotContain("srcset", result, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tracker.example", result, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Trusted_strips_meta_refresh_redirects()
    {
        var result = MailHtmlSanitizer.SanitizeTrusted("<meta http-equiv=\"refresh\" content=\"0; url=https://tracker.example\"><p>Hi</p>");
        Assert.DoesNotContain("http-equiv", result, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tracker.example", result, System.StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("<div style=\"width:e\\78 pression(alert(1))\">x</div>")]
    [InlineData("<div style='-moz-\\62 inding:url(http://evil/x.xml)'>x</div>")]
    [InlineData("<div style=\"behavior:url(#default#time2)\">x</div>")]
    public void Strips_css_escape_obfuscated_dangerous_styles(string html)
    {
        var untrusted = MailHtmlSanitizer.SanitizeUntrusted(html);
        var trusted = MailHtmlSanitizer.SanitizeTrusted(html);

        Assert.DoesNotContain("style=", untrusted, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style=", trusted, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Trusted_keeps_remote_images_but_still_drops_scripts()
    {
        var html = "<img src=\"https://cdn.example/logo.png\"><script>x()</script>";
        var result = MailHtmlSanitizer.SanitizeTrusted(html);
        Assert.Contains("cdn.example", result);                 // remote image kept for trusted sender
        Assert.DoesNotContain("<script", result, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plain_text_without_markup_is_html_encoded_in_a_pre_block()
    {
        var result = MailHtmlSanitizer.SanitizeUntrusted("plain & <dangerous> text");
        Assert.StartsWith("<pre>", result);
        Assert.Contains("&amp;", result);
        Assert.Contains("&lt;", result); // angle brackets escaped, not left as a tag
    }

    [Fact]
    public void Very_large_input_falls_back_to_encoded_plain_text()
    {
        var html = "<img src=\"https://tracker.example/p.gif\" onerror=\"steal()\">" + new string('a', 2 * 1024 * 1024 + 1);

        var result = MailHtmlSanitizer.SanitizeUntrusted(html);

        Assert.StartsWith("<pre>", result);
        Assert.DoesNotContain("<img", result, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", result, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tracker.example", result, System.StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_input_returns_empty(string? html)
    {
        Assert.Equal("", MailHtmlSanitizer.SanitizeUntrusted(html!));
        Assert.Equal("", MailHtmlSanitizer.SanitizeTrusted(html!));
    }

    [Fact]
    public void Keeps_benign_html_content()
    {
        var html = "<div><p>Hello <b>world</b></p></div>";
        var result = MailHtmlSanitizer.SanitizeUntrusted(html);
        Assert.Contains("Hello", result);
        Assert.Contains("<b>", result);
    }

    [Theory]
    [InlineData("<img src=\"https://tracker.example/p.gif\">")]
    [InlineData("<img src=\"//cdn.example/x.png\">")]           // protocol-relative
    [InlineData("<td background=\"http://x/bg.png\">")]
    [InlineData("<div style=\"background:url(https://x/a.png)\">")]
    [InlineData("<style>@import 'https://x/s.css';</style>")]
    public void HasRemoteResources_true(string html)
    {
        Assert.True(MailHtmlSanitizer.HasRemoteResources(html));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<p>plain text</p>")]
    [InlineData("<img src=\"cid:logo\">")]                       // inline attachment
    [InlineData("<img src=\"data:image/png;base64,AAAA\">")]    // data URI
    public void HasRemoteResources_false(string? html)
    {
        Assert.False(MailHtmlSanitizer.HasRemoteResources(html));
    }
}
