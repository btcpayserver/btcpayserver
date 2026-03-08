using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Razor.TagHelpers;
using System;
using System.Linq;

namespace BTCPayServer.Abstractions.TagHelpers;

[HtmlTargetElement(Attributes = "[permission]")]
[HtmlTargetElement(Attributes = "[not-permission]")]
public class PermissionTagHelper(IAuthorizationService authorizationService, IHttpContextAccessor httpContextAccessor)
    : TagHelper
{
    public string Permission { get; set; }
    public string NotPermission { get; set; }
    public string PermissionResource { get; set; }
    public bool AndMode { get; set; } = false;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var permissions = Permission?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
        var notPermissions = NotPermission?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();

        if (!permissions.Any() && !notPermissions.Any())
            return;
        if (httpContextAccessor.HttpContext is null)
            return;

        var shouldRender = true; // Assume tag should be rendered unless a check fails

        // Process 'Permission' - User must have these permissions
        if (permissions.Any())
        {
            bool finalResult = AndMode;
            foreach (var perm in permissions)
            {
                var res = await Check(perm);

                if (AndMode)
                    finalResult &= res.Succeeded;
                else
                    finalResult |= res.Succeeded;

                if (!AndMode && finalResult) break;
            }

            shouldRender = finalResult;
        }

        // Process 'NotPermission' - User must not have these permissions
        if (shouldRender && notPermissions.Any())
        {
            foreach (var notPerm in notPermissions)
            {
                var res = await Check(notPerm);

                if (res.Succeeded) // If the user has a 'NotPermission', they should not see the tag
                {
                    shouldRender = false;
                    break;
                }
            }
        }

        if (!shouldRender)
        {
            output.SuppressOutput();
        }
    }

    private async Task<AuthorizationResult> Check(string permission)
    {
        var res = await authorizationService.AuthorizeAsync(httpContextAccessor.HttpContext!.User,
            PermissionResource, permission);
        return res;
    }
}
