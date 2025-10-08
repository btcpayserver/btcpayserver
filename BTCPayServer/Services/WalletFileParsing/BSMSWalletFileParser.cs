#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using BTCPayServer;
using NBitcoin;
using NBXplorer;
using NBXplorer.DerivationStrategy;
using AccountKeySettings = BTCPayServer.AccountKeySettings;
using BTCPayNetwork = BTCPayServer.BTCPayNetwork;

namespace BTCPayServer.Services.WalletFileParsing;
public class BSMSWalletFileParser : IWalletFileParser
{
    public bool TryParse(BTCPayNetwork network, string data, [MaybeNullWhen(false)] out DerivationSchemeSettings derivationSchemeSettings)
    {
        derivationSchemeSettings = null;
        string[] lines = data.Split(
            new[] { "\r\n", "\r", "\n" },
            StringSplitOptions.None
        );

        if (lines.Length < 4 || !lines[0].Trim().Equals("BSMS 1.0"))
            return false;

        var descriptor = lines[1];
        var derivationPath = lines[2].Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ??
                             "/0/*";
        if (derivationPath == "No path restrictions")
            derivationPath = "/0/*";

        if (derivationPath != "/0/*")
            return false;


        descriptor = descriptor.Replace("/**", derivationPath);
        var testAddress = BitcoinAddress.Create(lines[3], network.NBitcoinNetwork);

        derivationSchemeSettings = network.GetDerivationSchemeParser().ParseOD(descriptor);
        derivationSchemeSettings.Source = "BSMS";
        var line = derivationSchemeSettings.AccountDerivation.GetLineFor(DerivationFeature.Deposit).Derive(0);
        return testAddress.ScriptPubKey == line.ScriptPubKey;
    }
}
