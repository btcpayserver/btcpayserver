using BTCPayServer.Services;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace BTCPayServer.TagHelpers;

// Razor does not omit data-* attributes for null/false values. Preserve the
// development-only marker when HTML roots are processed by Tag Helpers.
[HtmlTargetElement("html", Attributes = "asp-devenv")]
public class DevelopmentEnvironmentTagHelper(BTCPayServerEnvironment env) : TagHelper
{
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.Attributes.RemoveAll("asp-devenv");
        if (env.IsDeveloping)
            output.Attributes.SetAttribute("data-devenv", "");
    }
}
