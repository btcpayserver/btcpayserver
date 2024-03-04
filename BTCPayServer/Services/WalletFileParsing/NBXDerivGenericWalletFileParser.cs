#nullable enable
using System.Diagnostics.CodeAnalysis;
namespace BTCPayServer.Services.WalletFileParsing;
public class NBXDerivGenericWalletFileParser : IWalletFileParser
{
    public bool TryParse(BTCPayNetwork network, string data, [MaybeNullWhen(false)] out DerivationSchemeSettings derivationSchemeSettings)
    {
        var result = new DerivationSchemeSettings();
        var parser = network.GetDerivationSchemeParser();
        if (parser.TryParseXpub(data, ref result, electrum: true) ||
            parser.TryParseXpub(data, ref result))
        {
            derivationSchemeSettings = result;
            derivationSchemeSettings.Source = "GenericFile";
            return true;
        }
        derivationSchemeSettings = null;
        return false;
    }
}
