#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using BTCPayServer;
using NBitcoin;
using NBitcoin.DataEncoders;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace BTCPayServer.Services.WalletFileParsing;
public class WasabiWalletFileParser : IWalletFileParser
{
    class WasabiFormat
    {
        public string? ExtPubKey { get; set; }
        public string? MasterFingerprint { get; set; }
        public string? AccountKeyPath { get; set; }
        public string? ColdCardFirmwareVersion { get; set; }
        public string? CoboVaultFirmwareVersion { get; set; }
        public string? DerivationPath { get; set; }
        public string? Source { get; set; }
    }
    public bool TryParse(BTCPayNetwork network, string data, [MaybeNullWhen(false)] out DerivationSchemeSettings derivationSchemeSettings)
    {
        derivationSchemeSettings = null;
        var jobj = JsonConvert.DeserializeObject<WasabiFormat>(data);
        var derivationSchemeParser = network.GetDerivationSchemeParser();
        var result = new DerivationSchemeSettings();

        if (jobj is null || !derivationSchemeParser.TryParseXpub(jobj.ExtPubKey, ref result))
            return false;

        if (jobj.MasterFingerprint is not null)
        {
            // https://github.com/zkSNACKs/WalletWasabi/pull/1663#issuecomment-508073066
            if (uint.TryParse(jobj.MasterFingerprint, out var fingerprint))
            {
                result.AccountKeySettings[0].RootFingerprint = new HDFingerprint(fingerprint);
            }
            else
            {
                var bytes = Encoders.Hex.DecodeData(jobj.MasterFingerprint);
                var shouldReverseMfp = jobj.ColdCardFirmwareVersion == "2.1.0";
                if (shouldReverseMfp) // Bug in previous version of coldcard
                    bytes = bytes.Reverse().ToArray();
                result.AccountKeySettings[0].RootFingerprint = new HDFingerprint(bytes);
            }
        }

        if (jobj.AccountKeyPath is not null)
            result.AccountKeySettings[0].AccountKeyPath = new KeyPath(jobj.AccountKeyPath);

        if (jobj.ColdCardFirmwareVersion is not null)
        {
            result.Source = "ColdCard";
        }
        else if (jobj.CoboVaultFirmwareVersion is not null)
        {
            result.Source = "CoboVault";
        }
        else
            result.Source = jobj.Source ?? "WasabiFile";

        derivationSchemeSettings = result;
        return true;
    }
}
