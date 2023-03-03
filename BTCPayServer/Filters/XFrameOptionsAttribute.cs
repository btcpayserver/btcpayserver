using System;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BTCPayServer.Filters
{
    public class XFrameOptionsAttribute : Attribute, IActionFilter
    {
        public XFrameOptionsAttribute(string value)
        {
            Value = value;
        }

        [Obsolete("Do not use second parameter ignored")]
        public XFrameOptionsAttribute(XFrameOptions type, string _ = null) : this(type)
        {

        }

        public XFrameOptionsAttribute(XFrameOptions type)
        {
            Value = type switch
            {
                XFrameOptions.Deny => "DENY",
                XFrameOptions.SameOrigin => "SAMEORIGIN",
                XFrameOptions.Unset => null,
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
            };
        }

        private string Value { get; set; }

        public void OnActionExecuted(ActionExecutedContext context)
        {
        }

        public void OnActionExecuting(ActionExecutingContext context)
        {
            if (context.IsEffectivePolicy(this))
            {
                context.HttpContext.Response.SetHeaderOnStarting("X-Frame-Options", Value);
            }
        }

        public enum XFrameOptions
        {
            Deny,
            SameOrigin,
            Unset
        }
    }
}
