using BTCPayServer.Abstractions.Services;
using BTCPayServer.Security;
using Microsoft.AspNetCore.Razor.TagHelpers;
using NBitcoin;

namespace BTCPayServer.Abstractions.TagHelpers;

[HtmlTargetElement("srv-model")]
public class SrvModel : TagHelper
{
    private readonly Safe _safe;
    private readonly ContentSecurityPolicies _csp;

    public SrvModel(Safe safe, ContentSecurityPolicies csp)
    {
        _safe = safe;
        _csp = csp;
    }
    public string VarName { get; set; } = "srvModel";
    public object Model { get; set; }
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "script";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.Add(new TagHelperAttribute("type", "text/javascript"));
        var nonce = RandomUtils.GetUInt256().ToString().Substring(0, 32);
        output.Attributes.Add(new TagHelperAttribute("nonce", nonce));
        _csp.Add("script-src", $"'nonce-{nonce}'");
        output.Content.SetHtmlContent($"var {VarName} = {_safe.Json(Model)};");
    }
}
