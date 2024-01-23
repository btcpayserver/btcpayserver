#nullable enable
using System.Diagnostics.CodeAnalysis;
namespace BTCPayServer.Services.WalletFileParsing;
public class NBXDerivGenericWalletFileParser : IWalletFileParser
{
    public bool TryParse(BTCPayNetwork network, string data, [MaybeNullWhen(false)] out DerivationSchemeSettings derivationSchemeSettings)
    {
        derivationSchemeSettings = DerivationSchemeSettings.Parse(data, network);
        derivationSchemeSettings.Source = "Generic";
        return true;
    }
}
