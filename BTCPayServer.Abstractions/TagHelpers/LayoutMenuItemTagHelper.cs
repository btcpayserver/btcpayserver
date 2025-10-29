using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace BTCPayServer.Abstractions.TagHelpers;

[HtmlTargetElement(Attributes = "[layout-menu-item]")]
public class LayoutMenuItemTagHelper : TagHelper
{
    private const string ActivePageKey = "ActivePage";
    private const string ActiveClass = "active";
    [ViewContext]
    public ViewContext ViewContext { get; set; }
    public string LayoutMenuItem { get; set; }
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.Attributes.Add("id", $"menu-item-{LayoutMenuItem}");
        var viewData = ViewContext.ViewData;
        var match = viewData.ContainsKey(ActivePageKey) && viewData[ActivePageKey]?.ToString() == LayoutMenuItem;
        output.Attributes.Add("class", $"menu-item nav-link {(match ? ActiveClass : "")}");
    }
}
