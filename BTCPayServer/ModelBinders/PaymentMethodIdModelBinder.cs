﻿using System;
using System.Collections.Generic;
using System.Linq;
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
            if (!typeof(PaymentMethodIdModelBinder).GetTypeInfo().IsAssignableFrom(bindingContext.ModelType))
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

            if (PaymentMethodId.TryParse(key, out var paymentId))
            {
                bindingContext.Result = ModelBindingResult.Success(paymentId);
            }
            return Task.CompletedTask;
        }
    }
}
