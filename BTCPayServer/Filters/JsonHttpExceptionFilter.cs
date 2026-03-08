using Microsoft.AspNetCore.Mvc.Filters;

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
