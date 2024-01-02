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
            if (!jobj.ContainsKey("ExtPubKey"))
                return (null, null);

            var derivationSchemeParser = network.GetDerivationSchemeParser();
            var result = new DerivationSchemeSettings()
            {
                Network = network
            };

            if (!derivationSchemeParser.TryParseXpub(jobj["ExtPubKey"].Value<string>(), ref result, out var error))
            {
                return (null, error);
            }

            if (jobj.ContainsKey("MasterFingerprint"))
            {
                try
                {
                    var mfpString = jobj["MasterFingerprint"].ToString().Trim();
                    // https://github.com/zkSNACKs/WalletWasabi/pull/1663#issuecomment-508073066

                    if (uint.TryParse(mfpString, out var fingerprint))
                    {
                        result.AccountKeySettings[0].RootFingerprint = new HDFingerprint(fingerprint);
                    }
                    else
                    {
                        var shouldReverseMfp = jobj.ContainsKey("ColdCardFirmwareVersion") &&
                                               jobj["ColdCardFirmwareVersion"].ToString() == "2.1.0";
                        var bytes = Encoders.Hex.DecodeData(mfpString);
                        result.AccountKeySettings[0].RootFingerprint = shouldReverseMfp
                            ? new HDFingerprint(bytes.Reverse().ToArray())
                            : new HDFingerprint(bytes);
                    }
                }

                catch
                {
                    return (null, "MasterFingerprint was not valid");
                }
            }

            if (jobj.ContainsKey("AccountKeyPath"))
            {
                try
                {
                    result.AccountKeySettings[0].AccountKeyPath =
                        new KeyPath(jobj["AccountKeyPath"].Value<string>());
                }
                catch
                {
                    return (null, "AccountKeyPath was not valid");
                }
            }

            if (jobj.ContainsKey("DerivationPath"))
            {
                try
                {
                    result.AccountKeySettings[0].AccountKeyPath =
                        new KeyPath(jobj["DerivationPath"].Value<string>().ToLowerInvariant());
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
