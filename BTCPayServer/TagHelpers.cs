using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Security;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc.Razor.TagHelpers;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using NBitcoin;
using NBitcoin.Crypto;

namespace BTCPayServer.TagHelpers
{
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

    // Make sure that <svg><use href=/ are correctly working if rootpath is present
    [HtmlTargetElement("use", Attributes = "href")]
    public class SVGUse : UrlResolutionTagHelper
    {
        private readonly IFileVersionProvider _fileVersionProvider;

        public SVGUse(IUrlHelperFactory urlHelperFactory, HtmlEncoder htmlEncoder, IFileVersionProvider fileVersionProvider):base(urlHelperFactory, htmlEncoder)
        {
            _fileVersionProvider = fileVersionProvider;
        }
        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            var attr = output.Attributes["href"].Value.ToString();
            attr = _fileVersionProvider.AddFileVersionToPath(ViewContext.HttpContext.Request.PathBase, attr);
            output.Attributes.SetAttribute("href", attr);
        }
    }
}
