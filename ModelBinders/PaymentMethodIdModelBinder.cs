using System.Reflection;
using System.Threading.Tasks;
using BTCPayServer.Payments;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BTCPayServer.ModelBinders
{
    public class PaymentMethodIdModelBinder : IModelBinder
    {
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            if (!typeof(PaymentMethodId).GetTypeInfo().IsAssignableFrom(bindingContext.ModelType))
            {
                return Task.CompletedTask;
            }

            ValueProviderResult val = bindingContext.ValueProvider.GetValue(
                bindingContext.ModelName);
            string key = val.FirstValue as string;
            if (key == null)
            {
                return Task.CompletedTask;
            }

            if (PaymentMethodId.TryParse(key, out var paymentId))
            {
                bindingContext.Result = ModelBindingResult.Success(paymentId);
            }
            else
            {
                bindingContext.Result = ModelBindingResult.Failed();
                bindingContext.ModelState.AddModelError(bindingContext.ModelName, "Invalid payment id");
            }
            return Task.CompletedTask;
        }
    }
}
