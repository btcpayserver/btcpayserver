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
        public string Value
        {
            get; set;
        }
        public void OnActionExecuted(ActionExecutedContext context)
        {

        }

        public void OnActionExecuting(ActionExecutingContext context)
        {
            var existing = context.HttpContext.Response.Headers["x-frame-options"].FirstOrDefault();
            if (existing != null && Value == null)
                context.HttpContext.Response.Headers.Remove("x-frame-options");
            else
                context.HttpContext.Response.Headers["x-frame-options"] = Value;
        }
    }
}
