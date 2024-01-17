#nullable enable
using System;
using BTCPayServer;
using NBitcoin;
using Newtonsoft.Json.Linq;

public class ElectrumFileOnChainWalletParser : OnChainWalletParser
{
    public (BTCPayServer.DerivationSchemeSettings? DerivationSchemeSettings, string? Error) TryParse(BTCPayNetwork network,
        string data)
    {
        try
        {
            var derivationSchemeParser = network.GetDerivationSchemeParser();
            var jobj = JObject.Parse(data);
            var result = new BTCPayServer.DerivationSchemeSettings() {Network = network};

            if (jobj["keystore"] is JObject keyStore)
            {
                result.Source = "ElectrumFile";
                jobj = keyStore;

                if (!jobj.TryGetValue("xpub", StringComparison.InvariantCultureIgnoreCase, out var xpubToken))
                {
                    return (null, "no xpub");
                }
                var strategy = derivationSchemeParser.Parse(xpubToken.Value<string>(), false, false, true);
                result.AccountDerivation = strategy;
                result.AccountOriginal = xpubToken.Value<string>();
                result.GetSigningAccountKeySettings();
                
                if (jobj["label"]?.Value<string>() is string label)
                {
                    try
                    {
                        result.Label = label;
                    }
                    catch
                    {
                        return (null, "Label was not a string");
                    }
                }

                if (jobj["ckcc_xfp"]?.Value<uint>() is uint xfp)
                {
                    try
                    {
                        result.AccountKeySettings[0].RootFingerprint =
                            new HDFingerprint(xfp);
                    }
                    catch
                    {
                        return (null, "fingerprint was not a uint");
                    }
                }

                if (jobj["derivation"]?.Value<string>() is string derivation)
                {
                    try
                    {
                        result.AccountKeySettings[0].AccountKeyPath = new KeyPath(derivation);
                    }
                    catch
                    {
                        return (null, "derivation keypath was not valid");
                    }
                }
                
                
                if (jobj.ContainsKey("ColdCardFirmwareVersion"))
                {
                    result.Source = "ColdCard";
                }
                else if (jobj.ContainsKey("CoboVaultFirmwareVersion"))
                {
                    result.Source = "CoboVault";
                }
                return (result, null);
            }

        }
        catch (FormatException)
        {
            return (null, "invalid xpub");
        }
        return (null, null);
    }
}
