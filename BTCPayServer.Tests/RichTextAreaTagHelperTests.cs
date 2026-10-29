using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.Hosting;
using BTCPayServer.TagHelpers;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Xunit;

namespace BTCPayServer.Tests;

[Trait("Fast", "Fast")]
public class RichTextAreaTagHelperTests
{
    [Fact]
    public void SanitizesAndEncodesTextareaContent()
    {
        var attributes = new TagHelperAttributeList
        {
            { "asp-for", "Description" },
            { "rich-text", null }
        };
        var context = new TagHelperContext(attributes, new Dictionary<object, object>(), "test");
        var output = new TagHelperOutput("textarea", attributes,
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        output.Content.SetHtmlContent(
            "&lt;p data-val=&quot;true&quot;&gt;Test &amp;amp; more&lt;/p&gt;" +
            "&lt;script&gt;alert(1)&lt;/script&gt;");
        var helper = new RichTextAreaTagHelper(
            new RichTextHtmlSanitizer(HtmlSanitizerFactory.CreateRichText()));

        helper.Process(context, output);

        Assert.False(output.Attributes.ContainsName("rich-text"));
        Assert.Equal("rich-text-editor", output.Attributes["class"].Value);
        Assert.Equal("&lt;p&gt;Test &amp;amp; more&lt;/p&gt;", output.Content.GetContent());
    }
}
