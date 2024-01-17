#nullable enable
using System;
using System.Linq;
using BTCPayServer;
using NBitcoin;
using NBXplorer.DerivationStrategy;
using AccountKeySettings = BTCPayServer.AccountKeySettings;
using BTCPayNetwork = BTCPayServer.BTCPayNetwork;

namespace BTCPayServer.Services.WalletFileParsing;
public class BSMSWalletFileParser : IWalletFileParser
{
    public (BTCPayServer.DerivationSchemeSettings? DerivationSchemeSettings, string? Error) TryParse(
        BTCPayNetwork network,
        string data)
    {
        try
        {
            string[] lines = data.Split(
                new[] {"\r\n", "\r", "\n"},
                StringSplitOptions.None
            );

            if (!lines[0].Trim().Equals("BSMS 1.0"))
            {
                return (null, null);
            }

            var descriptor = lines[1];
            var derivationPath = lines[2].Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ??
                                 "/0/*";
            if (derivationPath == "No path restrictions")
            {
                derivationPath = "/0/*";
            }

            if (derivationPath != "/0/*")
            {
                return (null, "BTCPay Server can only derive address to the deposit and change paths");
            }


            descriptor = descriptor.Replace("/**", derivationPath);
            var testAddress = BitcoinAddress.Create(lines[3], network.NBitcoinNetwork);

            var result = network.GetDerivationSchemeParser().ParseOutputDescriptor(descriptor);

            var deposit = new NBXplorer.KeyPathTemplates(null).GetKeyPathTemplate(DerivationFeature.Deposit);
            var line = result.Item1.GetLineFor(deposit).Derive(0);

            if (testAddress.ScriptPubKey != line.ScriptPubKey)
            {
                return (null, "BSMS test address did not match our generated address");
            }

            var derivationSchemeSettings = new BTCPayServer.DerivationSchemeSettings()
            {
                Network = network,
                Source = "BSMS",
                AccountDerivation = result.Item1,
                AccountOriginal = descriptor.Trim(),
                AccountKeySettings = result.Item2.Select((path, i) => new AccountKeySettings()
                {
                    RootFingerprint = path?.MasterFingerprint,
                    AccountKeyPath = path?.KeyPath,
                    AccountKey = result.Item1.GetExtPubKeys().ElementAt(i).GetWif(network.NBitcoinNetwork)
                }).ToArray()
            };
            return (derivationSchemeSettings, null);
        }
        catch (Exception e)
        {
            return (null, $"BSMS parse error: {e.Message}");
        }
    }
}
