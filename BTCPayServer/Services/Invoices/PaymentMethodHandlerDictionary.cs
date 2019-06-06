using System.Collections;
using System.Collections.Generic;
using BTCPayServer.Payments;

namespace BTCPayServer.Services.Invoices
{
    public class PaymentMethodHandlerDictionary : IEnumerable<IPaymentMethodHandler>
    {
        private readonly Dictionary<PaymentMethodId, IPaymentMethodHandler> _mappedHandlers =
            new Dictionary<PaymentMethodId, IPaymentMethodHandler>();

        public PaymentMethodHandlerDictionary(IEnumerable<IPaymentMethodHandler> paymentMethodHandlers)
        {
            foreach (var paymentMethodHandler in paymentMethodHandlers)
            {
                foreach (var supportedPaymentMethod in paymentMethodHandler.GetSupportedPaymentMethods())
                {
                    _mappedHandlers.Add(supportedPaymentMethod, paymentMethodHandler);
                }
            }
        }

        public IPaymentMethodHandler this[PaymentMethodId index] => _mappedHandlers[index];
        public bool Support(PaymentMethodId paymentMethod) => _mappedHandlers.ContainsKey(paymentMethod);
        public IEnumerator<IPaymentMethodHandler> GetEnumerator()
        {
            return _mappedHandlers.Values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
