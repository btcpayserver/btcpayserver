using BTCPayServer.Data;
using BTCPayServer.Payments;

namespace BTCPayServer.PayoutProcessors;

public class BeforePayoutFilterData
{
    private readonly StoreData _store;
    private readonly ISupportedPaymentMethod _paymentMethod;

    public BeforePayoutFilterData(StoreData store, ISupportedPaymentMethod paymentMethod)
    {
        _store = store;
        _paymentMethod = paymentMethod;
    }
}
