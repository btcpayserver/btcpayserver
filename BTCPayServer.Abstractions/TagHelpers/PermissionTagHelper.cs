using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Razor.TagHelpers;
using System;
using System.Linq;

namespace BTCPayServer.Abstractions.TagHelpers;

[HtmlTargetElement(Attributes = "[permission]")]
[HtmlTargetElement(Attributes = "[not-permission]")]
public class PermissionTagHelper : TagHelper
{
    private readonly IAuthorizationService _authorizationService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PermissionTagHelper(IAuthorizationService authorizationService, IHttpContextAccessor httpContextAccessor)
    {
        _authorizationService = authorizationService;
        _httpContextAccessor = httpContextAccessor;
    }

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
        if (_httpContextAccessor.HttpContext is null)
            return;

        bool shouldRender = true; // Assume tag should be rendered unless a check fails

        // Process 'Permission' - User must have these permissions
        if (permissions.Any())
        {
            bool finalResult = AndMode;
            foreach (var perm in permissions)
            {
                var key = $"{perm}_{PermissionResource}";
                AuthorizationResult res = await GetOrAddAuthorizationResult(key, perm);

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
                var key = $"{notPerm}_{PermissionResource}";
                AuthorizationResult res = await GetOrAddAuthorizationResult(key, notPerm);

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

    private async Task<AuthorizationResult> GetOrAddAuthorizationResult(string key, string permission)
    {
        if (!_httpContextAccessor.HttpContext.Items.TryGetValue(key, out var cachedResult))
        {
            var res = await _authorizationService.AuthorizeAsync(_httpContextAccessor.HttpContext.User,
                PermissionResource, permission);
            _httpContextAccessor.HttpContext.Items[key] = res;
            return res;
        }

        return cachedResult as AuthorizationResult;
    }
}
