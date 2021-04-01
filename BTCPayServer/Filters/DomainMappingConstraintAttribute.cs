using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.HostedServices;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
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
        public AppType? AppType { get; set; }

        public bool Accept(ActionConstraintContext context)
        {
            if (context.RouteContext.RouteData.Values.ContainsKey("appId"))
                return true;
            var css = context.RouteContext.HttpContext.RequestServices.GetService<CssThemeManager>();
            if (css?.DomainToAppMapping is List<PoliciesSettings.DomainToAppMappingItem> mapping)
            {
                var matchedDomainMapping = css.DomainToAppMapping.FirstOrDefault(item =>
                item.Domain.Equals(context.RouteContext.HttpContext.Request.Host.Host, StringComparison.InvariantCultureIgnoreCase));
                if (matchedDomainMapping is PoliciesSettings.DomainToAppMappingItem)
                {
                    if (!(AppType is AppType appType))
                        return false;
                    if (appType != matchedDomainMapping.AppType)
                        return false;
                    context.RouteContext.RouteData.Values.Add("appId", matchedDomainMapping.AppId);
                    return true;
                }

                if (AppType == css.RootAppType) {
                    context.RouteContext.RouteData.Values.Add("appId", css.RootAppId);

                    return true;
                }

                return AppType is null;
            }
            else
            {
                return AppType is null;
            }
        }
    }
}
