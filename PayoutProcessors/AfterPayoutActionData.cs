using System.Collections.Generic;
using BTCPayServer.Data;

namespace BTCPayServer.PayoutProcessors;

public record AfterPayoutActionData(StoreData Store, PayoutProcessorData ProcessorData,
    IEnumerable<PayoutData> Payouts);
