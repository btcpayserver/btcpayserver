using System;
using System.Linq;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Services;
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
            var req = context.RouteContext.HttpContext.Request;
            var policies = context.RouteContext.HttpContext.RequestServices.GetService<PoliciesSettings>();
            var mapping = policies?.DomainToAppMapping?.ToList() ?? [];
            if (policies is { RootAppId: { } rootAppId, RootAppType: { } rootAppType })
            {
                mapping.Add(new PoliciesSettings.DomainToAppMappingItem
                {
                    Domain = req.Host.Host,
                    AppId = rootAppId,
                    AppType = rootAppType
                });
            }

            // If we have an appId, we can redirect to the canonical domain
            if ((string)context.RouteContext.RouteData.Values["appId"] is { } appId)
            {
                var redirectDomain = mapping.FirstOrDefault(item => item.AppId == appId)?.Domain;
                // App is accessed via path, redirect to canonical domain
                if (!string.IsNullOrEmpty(redirectDomain) && req.Method != "POST" && !req.HasFormContentType && !req.IsOnion())
                {
                    var uri = new UriBuilder(req.Scheme, redirectDomain);
                    if (req.Host.Port.HasValue)
                        uri.Port = req.Host.Port.Value;
                    context.RouteContext.HttpContext.Response.Redirect(uri.ToString());
                }
                return true;
            }

            // If we don't have an appId, maybe the domain we are browsing is a domain of an app
            var matchedDomainMapping = mapping.FirstOrDefault(item => item.Domain.Equals(req.Host.Host, StringComparison.InvariantCultureIgnoreCase));
            if (matchedDomainMapping != null)
            {
                if (AppType is null || AppType != matchedDomainMapping.AppType)
                    return false;
                context.RouteContext.RouteData.Values.Add("appId", matchedDomainMapping.AppId);
                return true;
            }

            return AppType is null; // We should never prevent to go on home page
        }
    }
}
