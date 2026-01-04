using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.VisualBasic.CompilerServices;

namespace BTCPayServer.ModelBinders
{
    public class DateTimeOffsetModelBinder : IModelBinder
    {
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            if (!typeof(DateTimeOffset).GetTypeInfo().IsAssignableFrom(bindingContext.ModelType) &&
                !typeof(DateTimeOffset?).GetTypeInfo().IsAssignableFrom(bindingContext.ModelType))
            {
                return Task.CompletedTask;
            }
            ValueProviderResult val = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
            string v = val.FirstValue as string;
            if (v == null)
            {
                return Task.CompletedTask;
            }

            try
            {
                var sec = long.Parse(v, CultureInfo.InvariantCulture);
                bindingContext.Result = ModelBindingResult.Success(NBitcoin.Utils.UnixTimeToDateTime(sec));
            }
            catch
            {
                bindingContext.Result = ModelBindingResult.Failed();
                bindingContext.ModelState.AddModelError(bindingContext.ModelName, "Invalid unix timestamp");
            }
            return Task.CompletedTask;
        }
    }
}
