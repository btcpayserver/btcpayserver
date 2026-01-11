using BTCPayServer.Data;

namespace BTCPayServer.PayoutProcessors.OnChain;

public class OnChainAutomatedPayoutBlob : AutomatedPayoutBlob
{
    public int FeeTargetBlock { get; set; } = 1;
    public decimal Threshold { get; set; } = 0;
}
