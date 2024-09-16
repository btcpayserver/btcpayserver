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
        var derivationSchemeParser = network.GetDerivationSchemeParser();
        var descriptor = derivationSchemeParser.ParseOutputDescriptor(data);
        derivationSchemeSettings = new DerivationSchemeSettings()
        {
            Source = "OutputDescriptor",
            AccountOriginal = data.Trim(),
            AccountDerivation = descriptor.Item1,
            AccountKeySettings = descriptor.Item2.Select((path, i) => new AccountKeySettings()
            {
                RootFingerprint = path?.MasterFingerprint,
                AccountKeyPath = path?.KeyPath,
                AccountKey =
                    descriptor.Item1.GetExtPubKeys().ElementAt(i).GetWif(derivationSchemeParser.Network)
            }).ToArray()
        };
        return true;
    }
}
