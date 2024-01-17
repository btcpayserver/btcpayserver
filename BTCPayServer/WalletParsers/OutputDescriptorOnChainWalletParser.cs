﻿#nullable enable
using System;
using System.Linq;
using BTCPayServer;

public class OutputDescriptorOnChainWalletParser : OnChainWalletParser
{
    public (BTCPayServer.DerivationSchemeSettings? DerivationSchemeSettings, string? Error) TryParse(BTCPayNetwork network,
        string data)
    {
        try
        {
            if (!Extensions.IsOutputDescriptor(data))
                return (null, null);

            var derivationSchemeParser = network.GetDerivationSchemeParser();

            var descriptor = derivationSchemeParser.ParseOutputDescriptor(data);

            var derivationSchemeSettings = new DerivationSchemeSettings()
            {
                Network = network,
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
            return (derivationSchemeSettings, null);
        }
        catch (Exception exception)
        {
            return (null, exception.Message);
        }
    }
}
