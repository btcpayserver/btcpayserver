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
    public class BitpayDateTimeOffsetModelBinder : IModelBinder
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
                var sec = DateTimeOffset.ParseExact(v, "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
                bindingContext.Result = ModelBindingResult.Success(sec);
            }
            catch
            {
                bindingContext.Result = ModelBindingResult.Failed();
                bindingContext.ModelState.AddModelError(bindingContext.ModelName, "Invalid date (MM/dd/yyyy)");
            }
            return Task.CompletedTask;
        }
    }
}
