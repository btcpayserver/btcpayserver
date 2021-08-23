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
                if (theme.CssUri != null && Uri.TryCreate(theme.CssUri, UriKind.Absolute, out var uri))
                {
                    policies.Clear();
                }
            }
        }
    }
}
