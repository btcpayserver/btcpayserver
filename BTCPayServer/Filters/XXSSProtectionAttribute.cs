using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.ViewFeatures.Internal;

namespace BTCPayServer.Filters
{
    public class XXSSProtectionAttribute : Attribute, IActionFilter
    {
        public void OnActionExecuted(ActionExecutedContext context)
        {
        }

        public void OnActionExecuting(ActionExecutingContext context)
        {
            var existing = context.HttpContext.Response.Headers["X-XSS-Protection"].FirstOrDefault();
            if (existing != null)
                context.HttpContext.Response.Headers.Remove("X-XSS-Protection");
            else
                context.HttpContext.Response.Headers["X-XSS-Protection"] = "1; mode=block";
        }
        
    }
}
