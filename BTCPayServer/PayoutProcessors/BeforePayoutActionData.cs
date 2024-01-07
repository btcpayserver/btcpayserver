using System.Collections.Generic;
using BTCPayServer.Data;

namespace BTCPayServer.PayoutProcessors;

public record BeforePayoutActionData(StoreData Store, PayoutProcessorData ProcessorData, IEnumerable<PayoutData> Payouts);
