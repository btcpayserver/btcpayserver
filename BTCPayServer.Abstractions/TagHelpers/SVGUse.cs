using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor.TagHelpers;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace BTCPayServer.Abstractions.TagHelpers;

// Make sure that <svg><use href=/ are correctly working if rootpath is present
[HtmlTargetElement("use", Attributes = "href")]
public class SVGUse : UrlResolutionTagHelper2
{
    private readonly IFileVersionProvider _fileVersionProvider;

    public SVGUse(IUrlHelperFactory urlHelperFactory, HtmlEncoder htmlEncoder, IFileVersionProvider fileVersionProvider) : base(urlHelperFactory, htmlEncoder)
    {
        _fileVersionProvider = fileVersionProvider;
    }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var attr = output.Attributes["href"].Value.ToString();
        var symbolIndex = attr!.IndexOf("#", StringComparison.InvariantCulture);
        var start = attr.IndexOf("~", StringComparison.InvariantCulture) + 1;
        var length = (symbolIndex != -1 ? symbolIndex : attr.Length) - start;
        var filePath = attr.Substring(start, length);
        if (!string.IsNullOrEmpty(filePath))
        {
            var versioned = _fileVersionProvider.AddFileVersionToPath(ViewContext.HttpContext.Request.PathBase, filePath);
            attr = attr.Replace(filePath, versioned);
        }
        output.Attributes.SetAttribute("href", attr);
        base.Process(context, output);
    }
}
