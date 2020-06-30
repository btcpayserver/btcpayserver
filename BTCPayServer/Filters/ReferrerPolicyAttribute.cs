using System;
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
                context.HttpContext.Response.SetHeaderOnStarting("Referrer-Policy", Value);
            }
        }
    }
}
