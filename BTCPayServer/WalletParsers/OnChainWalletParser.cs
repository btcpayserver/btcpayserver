#nullable enable
using BTCPayServer;

public interface OnChainWalletParser
{
    (BTCPayServer.DerivationSchemeSettings? DerivationSchemeSettings, string? Error) TryParse(BTCPayNetwork network, string data);
}
