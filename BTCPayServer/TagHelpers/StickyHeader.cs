using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace BTCPayServer.TagHelpers;

[HtmlTargetElement("sticky-header")]
public class StickyHeader : TagHelper
{
    private readonly HtmlEncoder _htmlEncoder;
    
    [HtmlAttributeName("class")]
    public string CssClass { get; set; }
    
    public StickyHeader(HtmlEncoder htmlEncoder)
    {
        _htmlEncoder = htmlEncoder;
    }
    
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var content = await output.GetChildContentAsync(NullHtmlEncoder.Default);
        var additionalClasses = string.IsNullOrEmpty(CssClass) ? "" : $" {_htmlEncoder.Encode(CssClass)}";
        output.TagName = null;
        output.Content.SetHtmlContent("<div class=\"sticky-header-setup\"></div>" +
            $"<header class=\"sticky-header{additionalClasses}\">{content.GetContent()}</header>" +
            "<script>const { offsetHeight } = document.querySelector('.sticky-header-setup + .sticky-header');document.documentElement.style.scrollPaddingTop = `calc(${offsetHeight}px + var(--btcpay-space-m))`;</script>");
    }
}
