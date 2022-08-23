using BTCPayServer.PayoutProcessors.Settings;

namespace BTCPayServer.PayoutProcessors.OnChain;

public class OnChainAutomatedPayoutBlob : AutomatedPayoutBlob
{
    public int FeeTargetBlock { get; set; } = 1;
}
