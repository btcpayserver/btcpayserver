using BTCPayServer.Data;

namespace BTCPayServer.PayoutProcessors.Lightning;

public class LightningAutomatedPayoutBlob : AutomatedPayoutBlob
{
    public int? CancelPayoutAfterFailures { get; set; } = null;
}
