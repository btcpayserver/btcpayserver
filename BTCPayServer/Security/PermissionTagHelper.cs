using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Security
{
    [HtmlTargetElement(Attributes = nameof(Permission))]
    public class PermissionTagHelper : TagHelper
    {
        private readonly IAuthorizationService _authorizationService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<PermissionTagHelper> _logger;

        public PermissionTagHelper(IAuthorizationService authorizationService, IHttpContextAccessor httpContextAccessor, ILogger<PermissionTagHelper> logger)
        {
            _authorizationService = authorizationService;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        public string Permission { get; set; }
        public string PermissionResource { get; set; }

        public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        {
            if (string.IsNullOrEmpty(Permission))
            {
                return;
            }

            var key = $"{Permission}_{PermissionResource}";
            if (!_httpContextAccessor.HttpContext.Items.TryGetValue(key,out var cachedResult))
            {
                var result = await _authorizationService.AuthorizeAsync(_httpContextAccessor.HttpContext.User,
                    PermissionResource,
                    Permission);

                cachedResult = result;
                _httpContextAccessor.HttpContext.Items.Add(key, result);

            }
            if (!((AuthorizationResult)cachedResult).Succeeded)
            {
                output.SuppressOutput();
            }
            
        }
    }
}
