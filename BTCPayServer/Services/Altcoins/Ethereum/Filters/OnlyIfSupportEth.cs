#if ALTCOINS_RELEASE || DEBUG
using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BTCPayServer.Services.Altcoins.Ethereum.Filters
{
        public class OnlyIfSupportEthAttribute : Attribute, IAsyncActionFilter
        {

            public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
            {
                var options = context.HttpContext.RequestServices.GetService(typeof(BTCPayServerOptions)) as BTCPayServerOptions;
                if (!options.NetworkProvider.GetAll().OfType<EthereumBTCPayNetwork>().Any())
                {
                    context.Result = new NotFoundResult();
                    return;
                }
                await next();
            }
        }
    
}
#endif
