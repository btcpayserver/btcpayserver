using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BTCPayServer.Filters
{
    public interface IReferrerPolicy : IFilterMetadata { }
    public class ReferrerPolicyAttribute : Attribute, IActionFilter
    {
        public ReferrerPolicyAttribute(string value)
        {
            Value = value;
        }
        public string Value { get; set; }
        public void OnActionExecuted(ActionExecutedContext context)
        {

        }

        public void OnActionExecuting(ActionExecutingContext context)
        {
            if (context.IsEffectivePolicy<ReferrerPolicyAttribute>(this))
            {
                var existing = context.HttpContext.Response.Headers["Referrer-Policy"].FirstOrDefault();
                if (existing != null && Value == null)
                    context.HttpContext.Response.Headers.Remove("Referrer-Policy");
                else
                    context.HttpContext.Response.Headers["Referrer-Policy"] = Value;
            }
        }
    }
}
