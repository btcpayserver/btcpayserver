#nullable enable
using BTCPayServer;
namespace BTCPayServer.Services.WalletFileParsing;
public interface IWalletFileParser
{
    (BTCPayServer.DerivationSchemeSettings? DerivationSchemeSettings, string? Error) TryParse(BTCPayNetwork network, string data);
}
