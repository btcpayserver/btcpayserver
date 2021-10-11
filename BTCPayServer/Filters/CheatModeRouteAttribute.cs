using System;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using BTCPayServer.Services;

namespace BTCPayServer.Filters
{
    public class CheatModeRouteAttribute : Attribute, IActionConstraint
    {
        public int Order => 100;

        public bool Accept(ActionConstraintContext context)
        {
            return context.RouteContext.HttpContext.RequestServices.GetRequiredService<BTCPayServerEnvironment>().CheatMode;
        }
    }
}
