using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Filters
{
    public class XFrameOptionsAttribute : Attribute, IActionFilter
    {
        public XFrameOptionsAttribute(string value)
        {
            Value = value;
        }

        public XFrameOptionsAttribute(XFrameOptions type, string allowFrom = null)
        {
            switch (type)
            {
                case XFrameOptions.Deny:
                    Value = "deny";
                    break;
                case XFrameOptions.SameOrigin:
                    Value = "deny";
                    break;
                case XFrameOptions.AllowFrom:
                    Value = $"allow-from {allowFrom}";
                    break;
                case XFrameOptions.AllowAll:
                    Value = "allow-all";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        public string Value { get; set; }

        public void OnActionExecuted(ActionExecutedContext context)
        {
        }

        public void OnActionExecuting(ActionExecutingContext context)
        {
            if (context.IsEffectivePolicy<XFrameOptionsAttribute>(this))
            {
                context.HttpContext.Response.SetHeaderOnStarting("X-Frame-Options", Value);
            }
        }

        public enum XFrameOptions
        {
            Deny,
            SameOrigin,
            AllowFrom,
            AllowAll
        }
    }
}
