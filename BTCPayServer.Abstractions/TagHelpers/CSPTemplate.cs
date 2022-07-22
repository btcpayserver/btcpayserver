using System.Threading.Tasks;
using BTCPayServer.Security;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace BTCPayServer.Abstractions.TagHelpers;

/// <summary>
/// Add sha256- to allow inline event handlers in CSP
/// </summary>
[HtmlTargetElement("template", Attributes = "csp-allow")]
public class CSPTemplate : TagHelper
{
    private readonly ContentSecurityPolicies _csp;
    public CSPTemplate(ContentSecurityPolicies csp)
    {
        _csp = csp;
    }
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.Attributes.RemoveAll("csp-allow");
        var childContent = await output.GetChildContentAsync();
        var content = childContent.GetContent();
        _csp.AllowInline(content);
    }
}
