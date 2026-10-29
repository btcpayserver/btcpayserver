using System.Web;
using System.Text.Encodings.Web;
using BTCPayServer.Abstractions.Services;
using Microsoft.AspNetCore.Mvc.TagHelpers;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace BTCPayServer.TagHelpers;

[HtmlTargetElement("textarea", Attributes = "asp-for,rich-text")]
public sealed class RichTextAreaTagHelper(RichTextHtmlSanitizer sanitizer) : TagHelper
{
    // Run after ASP.NET's TextAreaTagHelper, which has order -1000.
    public override int Order => 0;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.Attributes.RemoveAll("rich-text");
        output.AddClass("rich-text-editor", HtmlEncoder.Default);

        var value = HttpUtility.HtmlDecode(output.Content.GetContent());
        output.Content.SetContent(sanitizer.Sanitize(value));
    }
}
