using BTCPayServer.Security;
using Microsoft.AspNetCore.Razor.TagHelpers;
using NBitcoin;

namespace BTCPayServer.Abstractions.TagHelpers;

/// <summary>
/// Add a nonce-* so the inline-script can pass CSP rule when they are rendered server-side
/// </summary>
[HtmlTargetElement("script")]
public class CSPInlineScriptTagHelper : TagHelper
{
    private readonly ContentSecurityPolicies _csp;

    public CSPInlineScriptTagHelper(ContentSecurityPolicies csp)
    {
        _csp = csp;
    }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (output.Attributes.ContainsName("src"))
            return;
        if (output.Attributes.TryGetAttribute("type", out var attr))
        {
            if (attr.Value?.ToString() != "text/javascript")
                return;
        }
        var nonce = RandomUtils.GetUInt256().ToString().Substring(0, 32);
        output.Attributes.Add(new TagHelperAttribute("nonce", nonce));
        _csp.Add("script-src", $"'nonce-{nonce}'");
    }
}
