using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BTCPayServer.Filters
{
    public class XContentTypeOptionsAttribute : Attribute, IActionFilter
    {
        public XContentTypeOptionsAttribute(string value)
        {
            Value = value;
        }
        public void OnActionExecuted(ActionExecutedContext context)
        {
        }

        public string Value { get; set; }
        public void OnActionExecuting(ActionExecutingContext context)
        {
            var existing = context.HttpContext.Response.Headers["x-content-type-options"].FirstOrDefault();
            if (existing != null && Value == null)
                context.HttpContext.Response.Headers.Remove("x-content-type-options");
            else
                context.HttpContext.Response.Headers["x-content-type-options"] = Value;
        }
    }
}
