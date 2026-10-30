using Ganss.Xss;

namespace BTCPayServer.Abstractions.Services;

public sealed class RichTextHtmlSanitizer(HtmlSanitizer sanitizer)
{
    public string Sanitize(string html) => sanitizer.Sanitize(html);
}
