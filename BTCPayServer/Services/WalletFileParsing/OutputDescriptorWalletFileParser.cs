#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using BTCPayServer;
namespace BTCPayServer.Services.WalletFileParsing;
public class OutputDescriptorWalletFileParser : IWalletFileParser
{
    public bool TryParse(BTCPayNetwork network, string data, [MaybeNullWhen(false)] out DerivationSchemeSettings derivationSchemeSettings)
    {
        derivationSchemeSettings = null;
        var maybeOutputDesc = !data.Trim().StartsWith("{", StringComparison.OrdinalIgnoreCase);
        if (!maybeOutputDesc)
            return false;
        if (!DerivationSchemeParser.MaybeOD(data))
            return false;
        var derivationSchemeParser = network.GetDerivationSchemeParser();
        derivationSchemeSettings = derivationSchemeParser.ParseOD(data);
        derivationSchemeSettings.Source = "OutputDescriptor";
        return true;
    }
}
