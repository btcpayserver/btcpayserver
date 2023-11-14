using System;
using System.Linq;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Filters
{
    public class DomainMappingConstraintAttribute : Attribute, IActionConstraint
    {
        public DomainMappingConstraintAttribute()
        {
        }

        public DomainMappingConstraintAttribute(string appType)
        {
            AppType = appType;
        }

        public int Order => 100;
        private string AppType { get; }

        public bool Accept(ActionConstraintContext context)
        {
            var hasAppId = context.RouteContext.RouteData.Values.ContainsKey("appId");
            var policies = context.RouteContext.HttpContext.RequestServices.GetService<PoliciesSettings>();
            var mapping = policies?.DomainToAppMapping;
            var hasDomainMapping = mapping is { Count: > 0 };
            var matchingRootAppId = AppType == policies?.RootAppType && !string.IsNullOrEmpty(policies?.RootAppId) ? policies.RootAppId : null;

            if (hasAppId)
            {
                var appId = (string)context.RouteContext.RouteData.Values["appId"];
                var req = context.RouteContext.HttpContext.Request;
                string redirectDomain = null;
                if (hasDomainMapping)
                {
                    redirectDomain = mapping.FirstOrDefault(item => item.AppId == appId)?.Domain;
                }
                else if (matchingRootAppId == appId)
                {
                    redirectDomain = req.Host.Host;
                }
                    
                // App is accessed via path, redirect to canonical domain
                if (!string.IsNullOrEmpty(redirectDomain) && req.Method != "POST" && !req.HasFormContentType)
                {
                    var uri = new UriBuilder(req.Scheme, redirectDomain);
                    if (req.Host.Port.HasValue)
                        uri.Port = req.Host.Port.Value;
                    context.RouteContext.HttpContext.Response.Redirect(uri.ToString());
                    return true;
                }
            }

            if (hasDomainMapping)
            {
                var matchedDomainMapping = mapping.FirstOrDefault(item =>
                    item.Domain.Equals(context.RouteContext.HttpContext.Request.Host.Host,
                        StringComparison.InvariantCultureIgnoreCase));
                if (matchedDomainMapping != null)
                {
                    if (AppType is not { } appType)
                        return false;
                    if (appType != matchedDomainMapping.AppType)
                        return false;
                    if (!hasAppId)
                    {
                        context.RouteContext.RouteData.Values.Add("appId", matchedDomainMapping.AppId);
                        return true;
                    }
                }
            }

            if (!hasAppId && !string.IsNullOrEmpty(matchingRootAppId))
            {
                context.RouteContext.RouteData.Values.Add("appId", matchingRootAppId);
                return true;
            }

            return hasAppId || AppType is null;
        }
    }
}
