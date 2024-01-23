#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using BTCPayServer;
namespace BTCPayServer.Services.WalletFileParsing;
public class NBXDerivGenericWalletFileParser : IWalletFileParser
{
    public bool TryParse(BTCPayNetwork network, string data, [MaybeNullWhen(false)] out DerivationSchemeSettings derivationSchemeSettings)
    {
        derivationSchemeSettings = BTCPayServer.DerivationSchemeSettings.Parse(data, network);
        derivationSchemeSettings.Source = "Generic";
        return true;
    }
}
