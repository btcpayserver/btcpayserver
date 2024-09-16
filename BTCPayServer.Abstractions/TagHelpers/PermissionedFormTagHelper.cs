using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace BTCPayServer.Abstractions.TagHelpers;

[HtmlTargetElement("form", Attributes = "[permissioned]")]
public partial class PermissionedFormTagHelper(
    IAuthorizationService authorizationService,
    IHttpContextAccessor httpContextAccessor)
    : TagHelper
{
    public string Permissioned { get; set; }
    public string PermissionResource { get; set; }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (httpContextAccessor.HttpContext is null || string.IsNullOrEmpty(Permissioned))
            return;

        var res = await authorizationService.AuthorizeAsync(httpContextAccessor.HttpContext.User,
            PermissionResource, Permissioned);
        if (!res.Succeeded)
        {
            var content = await output.GetChildContentAsync();
            var html = SubmitButtonRegex().Replace(content.GetContent(), "");
            output.Content.SetHtmlContent($"<fieldset disabled>{html}</fieldset>");
        }
    }

    [GeneratedRegex("<(button|input).*?type=\"submit\".*?>.*?</\\1>")]
    private static partial Regex SubmitButtonRegex();
}
