using BTCPayServer.Hosting;
using Xunit;

namespace BTCPayServer.Tests;

[Trait("Fast", "Fast")]
public class HtmlSanitizerTests
{
    [Fact]
    public void RestrictsCssProperties()
    {
        var sanitizer = HtmlSanitizerFactory.CreateRichText();

        var sanitized = sanitizer.Sanitize(
            "<style>p { color: red; background-image: url(https://example.com/pixel); position: fixed; }</style>" +
            "<p style=\"font-weight: bold; background-image: url(https://example.com/pixel); position: fixed\">Test</p>");

        Assert.Contains("color:", sanitized);
        Assert.Contains("font-weight: bold", sanitized);
        Assert.DoesNotContain("background-image", sanitized);
        Assert.DoesNotContain("position", sanitized);
    }

    [Fact]
    public void EnforcesIframeSecurityAttributes()
    {
        var sanitizer = HtmlSanitizerFactory.CreateRichText();

        var sanitized = sanitizer.Sanitize(
            "<iframe src=\"https://www.youtube.com/embed/jF3W7EkXd4k\" " +
            "sandbox=\"allow-top-navigation\" referrerpolicy=\"unsafe-url\" " +
            "allow=\"accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share\" " +
            "loading=\"lazy\" frameborder=\"0\" allowfullscreen></iframe>");

        Assert.Contains("sandbox=\"allow-scripts allow-forms allow-popups allow-same-origin\"", sanitized);
        Assert.Contains("allow=\"accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share\"", sanitized);
        Assert.Contains("referrerpolicy=\"strict-origin-when-cross-origin\"", sanitized);
        Assert.Contains("loading=\"lazy\"", sanitized);
        Assert.Contains("frameborder=\"0\"", sanitized);
        Assert.Contains("allowfullscreen=\"\"", sanitized);
        Assert.DoesNotContain("allow-top-navigation", sanitized);
        Assert.DoesNotContain("unsafe-url", sanitized);
    }

    [Fact]
    public void RemovesUnsupportedIframeAttributeValues()
    {
        var sanitizer = HtmlSanitizerFactory.CreateRichText();

        var sanitized = sanitizer.Sanitize(
            "<iframe src=\"https://example.com/embed\" allow=\"autoplay; camera\" " +
            "loading=\"immediate\" frameborder=\"1\"></iframe>");

        Assert.DoesNotContain(" allow=", sanitized);
        Assert.DoesNotContain(" loading=", sanitized);
        Assert.DoesNotContain(" frameborder=", sanitized);
        Assert.Contains("sandbox=\"allow-scripts allow-forms allow-popups\"", sanitized);
        Assert.DoesNotContain("allow-same-origin", sanitized);
    }

    [Fact]
    public void RestrictsClipboardWriteToTrustedIframeHosts()
    {
        var sanitizer = HtmlSanitizerFactory.CreateRichText();

        var sanitized = sanitizer.Sanitize(
            "<iframe src=\"https://example.com/embed\" allow=\"autoplay; clipboard-write\"></iframe>");

        Assert.DoesNotContain(" allow=", sanitized);
    }

    [Theory]
    [InlineData("https://www.youtube.com/embed/video")]
    [InlineData("//www.youtube-nocookie.com/embed/video")]
    [InlineData("https://www.facebook.com/plugins/video.php")]
    [InlineData("https://vine.co/v/video/embed/simple")]
    [InlineData("https://instagram.com/p/video/embed/")]
    [InlineData("https://player.vimeo.com/video/123")]
    [InlineData("https://www.dailymotion.com/embed/video/123")]
    [InlineData("https://player.youku.com/embed/video")]
    [InlineData("https://jumpingbean.tv/videos/embed/video")]
    [InlineData("https://v.qq.com/txp/iframe/player.html")]
    [InlineData("https://rumble.com/embed/v123456")]
    [InlineData("https://twitter.com/embed")]
    [InlineData("https://syndication.twitter.com/embed")]
    public void AllowsSameOriginForSummernoteIframeHosts(string source)
    {
        var sanitizer = HtmlSanitizerFactory.CreateRichText();

        var sanitized = sanitizer.Sanitize($"<iframe src=\"{source}\"></iframe>");

        Assert.Contains("sandbox=\"allow-scripts allow-forms allow-popups allow-same-origin\"", sanitized);
    }

    [Theory]
    [InlineData("/relative")]
    [InlineData("https://example.com/embed")]
    [InlineData("https://peertube.example/videos/embed/abc123")]
    [InlineData("https://www.youtube.com.example.com/embed/video")]
    [InlineData("https://user@www.youtube.com/embed/video")]
    public void DoesNotAllowSameOriginForOtherIframeSources(string source)
    {
        var sanitizer = HtmlSanitizerFactory.CreateRichText();

        var sanitized = sanitizer.Sanitize($"<iframe src=\"{source}\"></iframe>");

        Assert.Contains("sandbox=\"allow-scripts allow-forms allow-popups\"", sanitized);
        Assert.DoesNotContain("allow-same-origin", sanitized);
    }

    [Fact]
    public void DefaultSanitizerRemovesRichContent()
    {
        var sanitizer = HtmlSanitizerFactory.Create();

        var sanitized = sanitizer.Sanitize(
            "<style>p { color: red; }</style>" +
            "<p class=\"alert\" style=\"color: red\">Text</p>" +
            "<img src=\"https://example.com/image.png\">" +
            "<iframe src=\"https://www.youtube.com/embed/video\"></iframe>" +
            "<video controls src=\"https://example.com/video.mp4\"></video>");

        Assert.Equal("<p class=\"alert\">Text</p>", sanitized);
    }

    [Fact]
    public void RichTextSanitizerAllowsImagesAndVideos()
    {
        var sanitizer = HtmlSanitizerFactory.CreateRichText();

        var sanitized = sanitizer.Sanitize(
            "<img src=\"https://example.com/image.png\">" +
            "<video controls src=\"https://example.com/video.mp4\"></video>");

        Assert.Contains("<img src=\"https://example.com/image.png\" class=\"img-fluid\">", sanitized);
        Assert.Contains("<video controls=\"\" src=\"https://example.com/video.mp4\"></video>", sanitized);
    }

    [Fact]
    public void RichTextSanitizerDoesNotRestoreRejectedImageUrls()
    {
        var sanitizer = HtmlSanitizerFactory.CreateRichText();

        var sanitized = sanitizer.Sanitize("<img src=\"javascript:alert(1)\">");

        Assert.DoesNotContain("javascript:", sanitized);
    }
}
