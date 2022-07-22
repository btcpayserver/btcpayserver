using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Security;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace BTCPayServer.Abstractions.TagHelpers;

/// <summary>
/// Add 'unsafe-hashes' and sha256- to allow inline event handlers in CSP
/// </summary>
[HtmlTargetElement(Attributes = "onclick")]
[HtmlTargetElement(Attributes = "onkeypress")]
[HtmlTargetElement(Attributes = "onchange")]
[HtmlTargetElement(Attributes = "onsubmit")]
public class CSPEventTagHelper : TagHelper
{
    public const string EventNames = "onclick,onkeypress,onchange,onsubmit";
    private readonly ContentSecurityPolicies _csp;

    readonly static HashSet<string> EventSet = EventNames.Split(',')
                                                .ToHashSet();
    public CSPEventTagHelper(ContentSecurityPolicies csp)
    {
        _csp = csp;
    }
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        foreach (var attr in output.Attributes)
        {
            var n = attr.Name.ToLowerInvariant();
            if (EventSet.Contains(n))
            {
                _csp.AllowUnsafeHashes(attr.Value.ToString());
            }
        }
    }
}
