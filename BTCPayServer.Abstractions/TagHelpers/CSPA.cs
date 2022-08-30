using System;
using BTCPayServer.Security;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace BTCPayServer.Abstractions.TagHelpers;

/// <summary>
/// Add sha256- to allow inline event handlers in a:href=javascript:
/// </summary>
[HtmlTargetElement("a", Attributes = "csp-allow")]
public class CSPA : TagHelper
{
    private readonly ContentSecurityPolicies _csp;
    public CSPA(ContentSecurityPolicies csp)
    {
        _csp = csp;
    }
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.Attributes.RemoveAll("csp-allow");
        if (output.Attributes.TryGetAttribute("href", out var attr))
        {
            var v = attr.Value.ToString();
            if (v.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
            {
                _csp.AllowUnsafeHashes(v);
            }
        }
    }
}
