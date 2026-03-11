#nullable enable
using System.Net.WebSockets;
using System.Threading.Tasks;
using BTCPayServer.Plugins.Bitpay.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BTCPayServer.Plugins.Bitpay;

public class BitpayFilterAttribute : ActionFilterAttribute
{
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var result = await next();
        if (result.Exception is WebSocketException)
        {
            result.ExceptionHandled = true;
        }
        else if (result.Exception is BitpayHttpException ex)
        {
            result.Result = new JsonResult(new BitpayErrorsModel(ex)) { StatusCode = ex.StatusCode };
            result.ExceptionHandled = true;
        }
    }
}
