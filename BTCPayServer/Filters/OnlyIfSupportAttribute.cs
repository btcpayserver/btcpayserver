using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BTCPayServer.Filters
{
    public class OnlyIfSupportAttribute : Attribute, IAsyncActionFilter
    {
        private readonly string _cryptoCode;

        public OnlyIfSupportAttribute(string cryptoCode)
        {
            _cryptoCode = cryptoCode;
        }
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var options = context.HttpContext.RequestServices.GetService(typeof(BTCPayServerOptions)) as BTCPayServerOptions;
            if (options.NetworkProvider.GetNetwork(_cryptoCode) == null)
            {
                context.Result = new NotFoundResult();
                return;
            }
            await next();   
        }
    }
}
