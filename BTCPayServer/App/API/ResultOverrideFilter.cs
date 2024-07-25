using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BTCPayServer.App.API;

public class ResultOverrideFilter : ResultFilterAttribute
{
    public void OnResultExecuted(ResultExecutedContext context)
    {
    }

    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.HttpContext.Items.TryGetValue("Result", out var result) && result is IActionResult value)
        {
            context.Result = value;
        }
        else if (context.Result is ObjectResult objectResult)
        {
        }
    }
}