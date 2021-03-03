#if ALTCOINS
using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BTCPayServer.Services.Altcoins.Matic.Filters
{
        public class OnlyIfSupportMaticAttribute : Attribute, IAsyncActionFilter
        {
            public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
            {
                var options = (BTCPayServerOptions) context.HttpContext.RequestServices.GetService(typeof(BTCPayServerOptions));
                if (!options.NetworkProvider.GetAll().OfType<MaticBTCPayNetwork>().Any())
                {
                    context.Result = new NotFoundResult();
                    return;
                }
                await next();
            }
        }
    
}
#endif
