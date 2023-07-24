using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Abstractions.TagHelpers;

[HtmlTargetElement(Attributes = "[permission]")]
[HtmlTargetElement(Attributes = "[not-permission]"  )]
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
    

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (string.IsNullOrEmpty(Permission) && string.IsNullOrEmpty(NotPermission))
            return;
        if (_httpContextAccessor.HttpContext is null)
            return;

        var expectedResult = !string.IsNullOrEmpty(Permission);
        var key = $"{Permission??NotPermission}_{PermissionResource}";
        if (!_httpContextAccessor.HttpContext.Items.TryGetValue(key, out var o) ||
            o is not AuthorizationResult res)
        {
            res = await _authorizationService.AuthorizeAsync(_httpContextAccessor.HttpContext.User,
                PermissionResource,
                Permission);
            _httpContextAccessor.HttpContext.Items.Add(key, res);
        }
        if (expectedResult != res.Succeeded)
        {
            output.SuppressOutput();
        }

    }
}
