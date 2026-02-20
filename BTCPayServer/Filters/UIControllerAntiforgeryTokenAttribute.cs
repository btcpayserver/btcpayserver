#nullable enable
using System;
using System.Threading.Tasks;
using BTCPayServer.Controllers;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Filters;

public class UIControllerAntiforgeryTokenAttribute :
    Attribute,
    IFilterMetadata,
    IAntiforgeryPolicy,
    IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.Result is AntiforgeryValidationFailedResult)
            AddErrorDetails(context.HttpContext);
        var antiForgery = context.HttpContext.RequestServices.GetService<IAntiforgery>();
        if (
            antiForgery is not null &&
            context.IsEffectivePolicy<IAntiforgeryPolicy>(this)
            && this.ShouldValidate(context))
        {
            try
            {
                await antiForgery.ValidateRequestAsync(context.HttpContext);
            }
            catch (AntiforgeryValidationException)
            {
                context.Result = new AntiforgeryValidationFailedResult();
                AddErrorDetails(context.HttpContext);
            }
        }
    }

    private void AddErrorDetails(HttpContext context)
    => context.Items[UIErrorController.ErrorDetailsKey] = "CSRF token validation failed.";

    private bool ShouldValidate(AuthorizationFilterContext context)
    {
        var isUI = IsUI(context);
        if (isUI is false)
            return false;
        var method = context.HttpContext.Request.Method;
        return !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsTrace(method) && !HttpMethods.IsOptions(method);
    }

    private bool? IsUI(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor controllerActionDescriptor)
            return null;
        if (controllerActionDescriptor.ControllerName.StartsWith("UI", StringComparison.OrdinalIgnoreCase))
            return true;
        if (controllerActionDescriptor.ControllerName.StartsWith("Greenfield", StringComparison.OrdinalIgnoreCase))
            return false;
        return typeof(Controller).IsAssignableFrom(controllerActionDescriptor.ControllerTypeInfo);
    }
}
