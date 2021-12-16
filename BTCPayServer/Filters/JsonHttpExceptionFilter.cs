using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace BTCPayServer.Filters
{
    public class JsonHttpExceptionFilter : IExceptionFilter
    {
        public void OnException(ExceptionContext context)
        {
            if (context.Exception is JsonHttpException ex)
            {
                context.Result = ex.ActionResult;
                context.ExceptionHandled = true;
            }
        }
    }
}
