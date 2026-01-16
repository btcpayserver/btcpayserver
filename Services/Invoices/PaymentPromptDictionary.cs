using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Payments;

namespace BTCPayServer.Services.Invoices
{
    public class PaymentPromptDictionary : IEnumerable<PaymentPrompt>
    {
        readonly Dictionary<PaymentMethodId, PaymentPrompt> _Inner = new Dictionary<PaymentMethodId, PaymentPrompt>();
        public PaymentPromptDictionary()
        {

        }
        public PaymentPromptDictionary(IEnumerable<PaymentPrompt> prompts)
        {
            _Inner = new Dictionary<PaymentMethodId, PaymentPrompt>(prompts.ToDictionary(p => p.PaymentMethodId));
        }

        public PaymentPrompt this[PaymentMethodId index]
        {
            get
            {
                return _Inner[index];
            }
        }

        public void Add(PaymentPrompt paymentPrompt)
        {
            _Inner.Add(paymentPrompt.PaymentMethodId, paymentPrompt);
        }

        public void Remove(PaymentPrompt paymentPrompt)
        {
            _Inner.Remove(paymentPrompt.PaymentMethodId);
        }
        public bool TryGetValue(PaymentMethodId paymentMethodId, out PaymentPrompt data)
        {
            ArgumentNullException.ThrowIfNull(paymentMethodId);
            return _Inner.TryGetValue(paymentMethodId, out data);
        }

        public void AddOrReplace(PaymentPrompt paymentMethod)
        {
            var key = paymentMethod.PaymentMethodId;
            _Inner.Remove(key);
            _Inner.Add(key, paymentMethod);
        }

        public IEnumerator<PaymentPrompt> GetEnumerator()
        {
            return _Inner.Values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public PaymentPrompt TryGet(PaymentMethodId paymentMethodId)
        {
            ArgumentNullException.ThrowIfNull(paymentMethodId);
            _Inner.TryGetValue(paymentMethodId, out var value);
            return value;
        }
    }
}
