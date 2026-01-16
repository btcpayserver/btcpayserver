using System.Reflection;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BTCPayServer.ModelBinders;

public class CustomerSelectorModelBinder : IModelBinder
{
    public const string InvalidFormat = "Invalid customer selector. Expected formats are `cust_customerId`, `[externalRef]` or `cust@email.com`";
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        if (!typeof(CustomerSelector).GetTypeInfo().IsAssignableFrom(bindingContext.ModelType))
        {
            return Task.CompletedTask;
        }
        var val = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        var v = val.FirstValue;
        if (v is null)
            return Task.CompletedTask;
        if (CustomerSelector.TryParse(v, out var res))
            bindingContext.Result = ModelBindingResult.Success(res);
        else
        {
            bindingContext.Result = ModelBindingResult.Failed();
            bindingContext.ModelState.AddModelError(bindingContext.ModelName, InvalidFormat);
        }
        return Task.CompletedTask;
    }
}
