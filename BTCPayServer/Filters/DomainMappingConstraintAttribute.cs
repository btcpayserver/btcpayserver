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
        
        public DomainMappingConstraintAttribute(AppType appType)
        {
            AppType = appType;
        }
        
        public int Order => 100;
        private AppType? AppType { get; }

        public bool Accept(ActionConstraintContext context)
        {
            var hasAppId = context.RouteContext.RouteData.Values.ContainsKey("appId");
            var policies = context.RouteContext.HttpContext.RequestServices.GetService<PoliciesSettings>();
            var mapping = policies?.DomainToAppMapping;
            var hasDomainMapping = mapping is { Count: > 0 };

            if (hasAppId && hasDomainMapping)
            {
                var appId = (string)context.RouteContext.RouteData.Values["appId"];
                var matchedDomainMapping = mapping.FirstOrDefault(item => item.AppId == appId);
                
                // App is accessed via path, redirect to canonical domain
                if (matchedDomainMapping != null && matchedDomainMapping.Domain.StartsWith("http"))
                {
                    context.RouteContext.HttpContext.Response.Redirect(matchedDomainMapping.Domain);
                    return true;
                }
            }
            
            if (hasDomainMapping)
            {
                var matchedDomainMapping = mapping.FirstOrDefault(item =>
                {
                    if (Uri.CheckHostName(item.Domain) != UriHostNameType.Unknown)
                    {
                        return item.Domain.Equals(context.RouteContext.HttpContext.Request.Host.Value,
                            StringComparison.InvariantCultureIgnoreCase);
                    }
                    if (Uri.TryCreate(item.Domain, UriKind.Absolute, out var uri))
                    {
                        return uri.Authority.Equals(context.RouteContext.HttpContext.Request.Host.Value,
                            StringComparison.InvariantCultureIgnoreCase);
                    }
                    return false;
                });
                if (matchedDomainMapping != null)
                {
                    if (AppType is not { } appType)
                        return false;
                    if (appType != matchedDomainMapping.AppType)
                        return false;
                    context.RouteContext.RouteData.Values.Add("appId", matchedDomainMapping.AppId);
                    return true;
                }
            }

            if (AppType == policies.RootAppType)
            {
                context.RouteContext.RouteData.Values.Add("appId", policies.RootAppId);

                return true;
            }

            return hasAppId || AppType is null;
        }
    }
}
