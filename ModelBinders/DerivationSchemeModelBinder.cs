using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NBXplorer.DerivationStrategy;

namespace BTCPayServer.ModelBinders
{
    public class DerivationSchemeModelBinder : IModelBinder
    {
        public DerivationSchemeModelBinder()
        {

        }

        #region IModelBinder Members

        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            if (!typeof(DerivationStrategyBase).GetTypeInfo().IsAssignableFrom(bindingContext.ModelType))
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

            var networkProvider = (BTCPayNetworkProvider)bindingContext.HttpContext.RequestServices.GetService(typeof(BTCPayNetworkProvider));
            var cryptoCode = bindingContext.ValueProvider.GetValue("cryptoCode").FirstValue;
            var network = networkProvider.GetNetwork<BTCPayNetwork>(cryptoCode ?? networkProvider.DefaultNetwork.CryptoCode);
            try
            {
                var data = network.NBXplorerNetwork.DerivationStrategyFactory.Parse(key);
                if (!bindingContext.ModelType.IsInstanceOfType(data))
                {
                    bindingContext.Result = ModelBindingResult.Failed();
                    bindingContext.ModelState.AddModelError(bindingContext.ModelName, "Invalid derivation scheme");
                    return Task.CompletedTask;
                }
                bindingContext.Result = ModelBindingResult.Success(data);
            }
            catch
            {
                bindingContext.Result = ModelBindingResult.Failed();
                bindingContext.ModelState.AddModelError(bindingContext.ModelName, "Invalid derivation scheme");
            }
            return Task.CompletedTask;
        }

        #endregion
    }
}
