#nullable enable
using System.Collections.Generic;
using BTCPayServer.Payments;

namespace BTCPayServer.Services
{
    public class PrettyNameProvider
    {
        private readonly Dictionary<PaymentMethodId, IPaymentModelExtension> _extensions;

        public PrettyNameProvider(Dictionary<PaymentMethodId, IPaymentModelExtension> extensions)
        {
            _extensions = extensions;
        }
        public string PrettyName(PaymentMethodId paymentMethodId)
        {
            if (paymentMethodId is null)
                return "<NULL>";
            _extensions.TryGetValue(paymentMethodId, out var n);
            return n?.DisplayName ?? paymentMethodId.ToString();
        }
    }
}
