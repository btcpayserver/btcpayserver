#nullable enable
using System;
using BTCPayServer;
namespace BTCPayServer.Services.WalletFileParsing;
public class NBXDerivGenericWalletFileParser : IWalletFileParser
{
    public (BTCPayServer.DerivationSchemeSettings? DerivationSchemeSettings, string? Error) TryParse(BTCPayNetwork network,
        string data)
    {
        try
        {
            var result =  BTCPayServer.DerivationSchemeSettings.Parse(data, network);
            result.Source = "Generic";
            return (result, null);
        }
        catch (Exception)
        {
            return (null, null);
        }
    }
}
