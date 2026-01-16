using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BTCPayServer.Payments;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BTCPayServer.ModelBinders
{
    public class DefaultModelBinderProvider : IModelBinderProvider
    {
        public IModelBinder GetBinder(ModelBinderProviderContext context)
        {
            if (context.Metadata.ModelType == typeof(decimal) || context.Metadata.ModelType == typeof(decimal?))
                return new InvariantDecimalModelBinder();
            if (context.Metadata.ModelType == typeof(PaymentMethodId))
                return new PaymentMethodIdModelBinder();
            if (context.Metadata.ModelType == typeof(WalletIdModelBinder))
                return new ModelBinders.WalletIdModelBinder();
            return null;
        }
    }
}
