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

            if (jobj.ContainsKey("keystore"))
            {
                result.Source = "ElectrumFile";
                jobj = (JObject)jobj["keystore"];

                string error = null;
                if (!jobj.TryGetValue("xpub", StringComparison.InvariantCultureIgnoreCase, out var xpubToken))
                {
                    return (null, "no xpub");
                }
                var strategy = derivationSchemeParser.Parse(xpubToken.Value<string>(), false, false, true);
                result.AccountDerivation = strategy;
                result.AccountOriginal = xpubToken.Value<string>();
                result.GetSigningAccountKeySettings();
                
                if (jobj.ContainsKey("label"))
                {
                    try
                    {
                        result.Label = jobj["label"].Value<string>();
                    }
                    catch
                    {
                        return (null, "Label was not a string");
                    }
                }

                if (jobj.ContainsKey("ckcc_xfp"))
                {
                    try
                    {
                        result.AccountKeySettings[0].RootFingerprint =
                            new HDFingerprint(jobj["ckcc_xfp"].Value<uint>());
                    }
                    catch
                    {
                        return (null, "fingerprint was not a uint");
                    }
                }

                if (jobj.ContainsKey("derivation"))
                {
                    try
                    {
                        result.AccountKeySettings[0].AccountKeyPath =
                            new KeyPath(jobj["derivation"].Value<string>());
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
        catch (FormatException e)
        {
            return (null, "invalid xpub");
        }
        return (null, null);
    }
}
