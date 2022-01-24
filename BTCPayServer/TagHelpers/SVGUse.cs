using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Mvc.Razor.TagHelpers;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace BTCPayServer.TagHelpers;

// Make sure that <svg><use href=/ are correctly working if rootpath is present
[HtmlTargetElement("use", Attributes = "href")]
public class SVGUse : UrlResolutionTagHelper
{
    private readonly IFileVersionProvider _fileVersionProvider;

    public SVGUse(IUrlHelperFactory urlHelperFactory, HtmlEncoder htmlEncoder, IFileVersionProvider fileVersionProvider) : base(urlHelperFactory, htmlEncoder)
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
