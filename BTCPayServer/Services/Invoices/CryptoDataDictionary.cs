using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Payments;

namespace BTCPayServer.Services.Invoices
{
    public class PaymentMethodDictionary : IEnumerable<PaymentMethod>
    {
        Dictionary<PaymentMethodId, PaymentMethod> _Inner = new Dictionary<PaymentMethodId, PaymentMethod>();
        public PaymentMethodDictionary()
        {

        }

        public PaymentMethodDictionary(BTCPayNetworkProvider networkProvider)
        {
            NetworkProvider = networkProvider;
        }


        public BTCPayNetworkProvider NetworkProvider { get; set; }
        public PaymentMethod this[PaymentMethodId index]
        {
            get
            {
                return _Inner[index];
            }
        }

        public void Add(PaymentMethod paymentMethod)
        {
            _Inner.Add(paymentMethod.GetId(), paymentMethod);
        }

        public void Remove(PaymentMethod paymentMethod)
        {
            _Inner.Remove(paymentMethod.GetId());
        }
        public bool TryGetValue(PaymentMethodId paymentMethodId, out PaymentMethod data)
        {
            if (paymentMethodId == null)
                throw new ArgumentNullException(nameof(paymentMethodId));
            return _Inner.TryGetValue(paymentMethodId, out data);
        }

        public void AddOrReplace(PaymentMethod paymentMethod)
        {
            var key = paymentMethod.GetId();
            _Inner.Remove(key);
            _Inner.Add(key, paymentMethod);
        }

        public IEnumerator<PaymentMethod> GetEnumerator()
        {
            return _Inner.Values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public PaymentMethod TryGet(PaymentMethodId paymentMethodId)
        {
            if (paymentMethodId == null)
                throw new ArgumentNullException(nameof(paymentMethodId));
            _Inner.TryGetValue(paymentMethodId, out var value);
            return value;
        }
        public PaymentMethod TryGet(string network, PaymentTypes paymentType)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            var id = new PaymentMethodId(network, paymentType);
            return TryGet(id);
        }
    }
}
