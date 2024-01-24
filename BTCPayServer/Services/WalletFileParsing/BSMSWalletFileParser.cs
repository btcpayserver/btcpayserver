#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using BTCPayServer;
using NBitcoin;
using NBXplorer.DerivationStrategy;
using AccountKeySettings = BTCPayServer.AccountKeySettings;
using BTCPayNetwork = BTCPayServer.BTCPayNetwork;

namespace BTCPayServer.Services.WalletFileParsing;
public class BSMSWalletFileParser : IWalletFileParser
{
    public bool TryParse(BTCPayNetwork network, string data, [MaybeNullWhen(false)] out DerivationSchemeSettings derivationSchemeSettings, [MaybeNullWhen(true)] out string error)
    {
        error = null;
        derivationSchemeSettings = null;
        string[] lines = data.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        if (lines.Length < 4 || !lines[0].Trim().Equals("BSMS 1.0"))
        {
            error = "Missing 'BSMS 1.0' definition";
            return false;
        }

        var descriptor = lines[1];
        var derivationPath = lines[2].Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ??
                             "/0/*";
        if (derivationPath == "No path restrictions")
            derivationPath = "/0/*";

        if (derivationPath != "/0/*")
        {
            error = "Unexpected derivation path";
            return false;
        }

        descriptor = descriptor.Replace("/**", derivationPath);
        var testAddress = BitcoinAddress.Create(lines[3], network.NBitcoinNetwork);

        var result = network.GetDerivationSchemeParser().ParseOutputDescriptor(descriptor);

        var deposit = new NBXplorer.KeyPathTemplates(null).GetKeyPathTemplate(DerivationFeature.Deposit);
        var line = result.Item1.GetLineFor(deposit).Derive(0);

        if (testAddress.ScriptPubKey != line.ScriptPubKey)
        {
            error = "Test address does not math";
            return false;
        }

        derivationSchemeSettings = new DerivationSchemeSettings
        {
            Network = network,
            Source = "BSMS",
            AccountDerivation = result.Item1,
            AccountOriginal = descriptor.Trim(),
            AccountKeySettings = result.Item2.Select((path, i) => new AccountKeySettings
            {
                RootFingerprint = path?.MasterFingerprint,
                AccountKeyPath = path?.KeyPath,
                AccountKey = result.Item1.GetExtPubKeys().ElementAt(i).GetWif(network.NBitcoinNetwork)
            }).ToArray()
        };
        return true;
    }
}
