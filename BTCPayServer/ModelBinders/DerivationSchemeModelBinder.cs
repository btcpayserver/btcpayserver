using Microsoft.AspNetCore.Mvc.ModelBinding;
using NBitcoin;
using System.Reflection;
using System;
using System.Threading.Tasks;
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
            if (val == null)
            {
                return Task.CompletedTask;
            }

            string key = val.FirstValue as string;
            if (key == null)
            {
                return Task.CompletedTask;
            }

            var networkProvider = (BTCPayNetworkProvider)bindingContext.HttpContext.RequestServices.GetService(typeof(BTCPayNetworkProvider));
            var cryptoCode = bindingContext.ValueProvider.GetValue("cryptoCode").FirstValue;
            var network = networkProvider.GetNetwork<BTCPayNetwork>(cryptoCode ?? "BTC");
            try
            {
                var data =network.NBXplorerNetwork.DerivationStrategyFactory.Parse(key);
                if (!bindingContext.ModelType.IsInstanceOfType(data))
                {
                    throw new FormatException("Invalid destination type");
                }
                bindingContext.Result = ModelBindingResult.Success(data);
            }
            catch { throw new FormatException("Invalid derivation scheme"); }
            return Task.CompletedTask;
        }

        #endregion
    }
}
