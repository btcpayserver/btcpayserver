using System.Collections.Generic;
using BTCPayServer.Data;
using BTCPayServer.Payments;

namespace BTCPayServer.PayoutProcessors;

public class AfterPayoutFilterData
{
    private readonly StoreData _store;
    private readonly ISupportedPaymentMethod _paymentMethod;
    private readonly List<PayoutData> _payoutDatas;

    public AfterPayoutFilterData(StoreData store, ISupportedPaymentMethod paymentMethod, List<PayoutData> payoutDatas)
    {
        _store = store;
        _paymentMethod = paymentMethod;
        _payoutDatas = payoutDatas;
    }
}
