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

    [Fact]
    public void Untrusted_strips_remote_image_tracking_pixels()
    {
        var result = MailHtmlSanitizer.SanitizeUntrusted("<p>Hi</p><img src=\"https://tracker.example/p.gif\">");
        Assert.DoesNotContain("tracker.example", result);
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
}
