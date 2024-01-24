#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using NBitcoin;
using NBitcoin.DataEncoders;
using Newtonsoft.Json;
namespace BTCPayServer.Services.WalletFileParsing;
public class WasabiWalletFileParser : IWalletFileParser
{
    public string[] SourceHandles => ["WasabiFile"];

    class WasabiFormat
    {
        public string? ExtPubKey { get; set; }
        public string? MasterFingerprint { get; set; }
        public string? AccountKeyPath { get; set; }
        public string? ColdCardFirmwareVersion { get; set; }
        public string? CoboVaultFirmwareVersion { get; set; }
    }

    public bool TryParse(BTCPayNetwork network, string data, [MaybeNullWhen(false)] out DerivationSchemeSettings derivationSchemeSettings, [MaybeNullWhen(true)] out string error)
    {
        error = null;
        derivationSchemeSettings = null;
        var jobj = JsonConvert.DeserializeObject<WasabiFormat>(data);
        var derivationSchemeParser = network.GetDerivationSchemeParser();
        var result = new DerivationSchemeSettings { Network = network, Source = SourceHandles.First() };

        if (jobj is null || !derivationSchemeParser.TryParseXpub(jobj.ExtPubKey, ref result, out error))
        {
            error ??= "Could not parse xpub";
            return false;
        }

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

        derivationSchemeSettings = result;
        return true;
    }
}
