using System;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BTCPayServer.Filters
{
    public class XXSSProtectionAttribute : Attribute, IActionFilter
    {
        public void OnActionExecuted(ActionExecutedContext context)
        {
        }

        public void OnActionExecuting(ActionExecutingContext context)
        {
            context.HttpContext.Response.SetHeaderOnStarting("X-XSS-Protection", "1; mode=block");
        }

    }
}
