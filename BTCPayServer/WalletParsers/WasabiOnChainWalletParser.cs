#nullable enable
using System;
using System.Linq;
using BTCPayServer;
using NBitcoin;
using NBitcoin.DataEncoders;
using Newtonsoft.Json.Linq;

public class WasabiOnChainWalletParser : OnChainWalletParser
{

    public (DerivationSchemeSettings? DerivationSchemeSettings, string? Error) TryParse(BTCPayNetwork network,
        string data)
    {
        try
        {
            var jobj = JObject.Parse(data);
            if (jobj["ExtPubKey"]?.Value<string>() is not string extPubKey)
                return (null, null);

            var derivationSchemeParser = network.GetDerivationSchemeParser();
            var result = new DerivationSchemeSettings()
            {
                Network = network
            };

            if (!derivationSchemeParser.TryParseXpub(extPubKey, ref result, out var error))
            {
                return (null, error);
            }

            if (jobj["MasterFingerprint"]?.ToString()?.Trim() is string mfpString)
            {
                try
                {
                    // https://github.com/zkSNACKs/WalletWasabi/pull/1663#issuecomment-508073066
                    if (uint.TryParse(mfpString, out var fingerprint))
                    {
                        result.AccountKeySettings[0].RootFingerprint = new HDFingerprint(fingerprint);
                    }
                    else
                    {
                        var bytes = Encoders.Hex.DecodeData(mfpString);
                        var shouldReverseMfp = jobj["ColdCardFirmwareVersion"]?.Value<string>() == "2.1.0";
                        if (shouldReverseMfp) // Bug in previous version of coldcard
                            bytes = bytes.Reverse().ToArray();
                        result.AccountKeySettings[0].RootFingerprint = new HDFingerprint(bytes);
                    }
                }

                catch
                {
                    return (null, "MasterFingerprint was not valid");
                }
            }

            if (jobj["AccountKeyPath"]?.Value<string>() is string accountKeyPath)
            {
                try
                {
                    result.AccountKeySettings[0].AccountKeyPath = new KeyPath(accountKeyPath);
                }
                catch
                {
                    return (null, "AccountKeyPath was not valid");
                }
            }

            if (jobj["DerivationPath"]?.Value<string>()?.ToLowerInvariant() is string derivationPath)
            {
                try
                {
                    result.AccountKeySettings[0].AccountKeyPath = new KeyPath(derivationPath);
                }
                catch
                {
                    return (null, "Derivation path was not valid");
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
            else if (jobj.TryGetValue("Source", StringComparison.InvariantCultureIgnoreCase, out var source))
            {
                result.Source = source.Value<string>();
            }
            else
                result.Source = "WasabiFile";


            return (result, null);
        }
        catch (Exception)
        {
            return (null, null);
        }

    }
}
