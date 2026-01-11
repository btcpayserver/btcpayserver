using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BTCPayServer.Filters
{
    public class JsonObjectExceptionFilter : IExceptionFilter
    {
        public void OnException(ExceptionContext context)
        {
            if (context.Exception is NBitcoin.JsonConverters.JsonObjectException jsonObject)
            {
                context.Result = new ObjectResult(new[] { new GreenfieldValidationError(jsonObject.Path, jsonObject.Message) }) { StatusCode = 422 };
                context.ExceptionHandled = true;
            }
        }
    }
}
