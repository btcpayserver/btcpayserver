using System;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Security;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BTCPayServer.HostedServices
{
    public class ContentSecurityPolicyCssThemeManager : Attribute, IActionFilter, IOrderedFilter
    {
        public int Order => 1001;

        public void OnActionExecuted(ActionExecutedContext context)
        {

        }

        public void OnActionExecuting(ActionExecutingContext context)
        {
            var settingsRepository = context.HttpContext.RequestServices.GetService(typeof(ISettingsRepository)) as ISettingsRepository;
            
            var policies = context.HttpContext.RequestServices.GetService(typeof(ContentSecurityPolicies)) as ContentSecurityPolicies;
            if (policies != null)
            {
                var theme = settingsRepository.GetTheme().GetAwaiter().GetResult();
                if (theme.CreativeStartCssUri != null && Uri.TryCreate(theme.CreativeStartCssUri, UriKind.Absolute, out var uri))
                {
                    policies.Clear();
                }
                if (theme.BootstrapCssUri != null && Uri.TryCreate(theme.BootstrapCssUri, UriKind.Absolute, out uri))
                {
                    policies.Clear();
                }
                if (theme.ThemeCssUri != null && Uri.TryCreate(theme.ThemeCssUri, UriKind.Absolute, out uri))
                {
                    policies.Clear();
                }
                if (theme.CustomThemeCssUri != null && Uri.TryCreate(theme.CustomThemeCssUri, UriKind.Absolute, out uri))
                {
                    policies.Clear();
                }
            }
        }
    }
}
