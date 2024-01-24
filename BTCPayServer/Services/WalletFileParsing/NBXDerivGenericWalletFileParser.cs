#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace BTCPayServer.Services.WalletFileParsing;
public class NBXDerivGenericWalletFileParser : IWalletFileParser
{
    public string[] SourceHandles => ["GenericFile"];
    public bool TryParse(BTCPayNetwork network, string data, [MaybeNullWhen(false)] out DerivationSchemeSettings derivationSchemeSettings, [MaybeNullWhen(true)] out string error)
    {
        var result = new DerivationSchemeSettings { Network = network };
        var parser = network.GetDerivationSchemeParser();
        if (parser.TryParseXpub(data, ref result, out error, electrum: true) ||
            parser.TryParseXpub(data, ref result, out error, electrum: false))
        {
            derivationSchemeSettings = result;
            derivationSchemeSettings.Source = SourceHandles.First();
            return true;
        }
        derivationSchemeSettings = null;
        return false;
    }
}
