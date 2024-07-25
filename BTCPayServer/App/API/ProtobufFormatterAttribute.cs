using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BTCPayServer.App.API;

public class ProtobufFormatterAttribute : ActionFilterAttribute, IControllerModelConvention, IActionModelConvention
{
    public void Apply(ControllerModel controller)
    {
        foreach (var action in controller.Actions)
        {
            Apply(action);
        }
    }

    public void Apply(ActionModel action)
    {
        // Set the model binder to NewtonsoftJsonBodyModelBinder for parameters that are bound to the request body.
        var parameters = action.Parameters.Where(p => p.BindingInfo?.BindingSource == BindingSource.Body);
        foreach (var p in parameters)
        {
            p.BindingInfo.BinderType = typeof(ProtobufFormatterModelBinder);
        }
    }

    public override void OnActionExecuted(ActionExecutedContext context)
    {
        if (context.Result is ObjectResult objectResult)
        {
            objectResult.Formatters.Clear();
            objectResult.Formatters.Add(new ProtobufOutputFormatter());
        }
        else
        {
            base.OnActionExecuted(context);
        }
    }
}