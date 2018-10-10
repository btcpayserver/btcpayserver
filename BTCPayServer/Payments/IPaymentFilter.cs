using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Payments
{
    public interface IPaymentFilter
    {
        bool Match(PaymentMethodId paymentMethodId);
    }

    public class PaymentFilter
    {
        class NeverPaymentFilter : IPaymentFilter
        {

            private static readonly NeverPaymentFilter _Instance = new NeverPaymentFilter();
            public static NeverPaymentFilter Instance
            {
                get
                {
                    return _Instance;
                }
            }
            public bool Match(PaymentMethodId paymentMethodId)
            {
                return false;
            }
        }
        class CompositePaymentFilter : IPaymentFilter
        {
            private readonly IPaymentFilter[] _filters;

            public CompositePaymentFilter(IPaymentFilter[] filters)
            {
                _filters = filters;
            }
            public bool Match(PaymentMethodId paymentMethodId)
            {
                return _filters.Any(f => f.Match(paymentMethodId));
            }
        }
        class PaymentIdFilter : IPaymentFilter
        {
            private readonly PaymentMethodId _paymentMethodId;

            public PaymentIdFilter(PaymentMethodId paymentMethodId)
            {
                _paymentMethodId = paymentMethodId;
            }
            public bool Match(PaymentMethodId paymentMethodId)
            {
                return paymentMethodId == _paymentMethodId;
            }
        }
        public static IPaymentFilter Never()
        {
            return NeverPaymentFilter.Instance;
        }
        public static IPaymentFilter Any(IPaymentFilter[] filters)
        {
            if (filters == null)
                throw new ArgumentNullException(nameof(filters));
            return new CompositePaymentFilter(filters);
        }
        public static IPaymentFilter WhereIs(PaymentMethodId paymentMethodId)
        {
            if (paymentMethodId == null)
                throw new ArgumentNullException(nameof(paymentMethodId));
            return new PaymentIdFilter(paymentMethodId);
        }
    }
}
