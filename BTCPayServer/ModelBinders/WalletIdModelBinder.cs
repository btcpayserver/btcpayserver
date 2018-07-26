using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BTCPayServer.ModelBinders
{
    public class WalletIdModelBinder : IModelBinder
    {
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            if (!typeof(WalletId).GetTypeInfo().IsAssignableFrom(bindingContext.ModelType))
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

            if(WalletId.TryParse(key, out var walletId))
            {
                bindingContext.Result = ModelBindingResult.Success(walletId);
            }
            return Task.CompletedTask;
        }
    }
}
