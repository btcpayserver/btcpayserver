#nullable enable
using System.Diagnostics.CodeAnalysis;
using NBitcoin;
using Newtonsoft.Json;
namespace BTCPayServer.Services.WalletFileParsing;
public class ElectrumWalletFileParser : IWalletFileParser
{
    class ElectrumFormat
    {
        internal class KeyStoreFormat
        {
            public string? xpub { get; set; }
            public string? label { get; set; }
            public string? root_fingerprint { get; set; }
            public uint? ckcc_xfp { get; set; }
            public string? derivation { get; set; }
            public string? ColdCardFirmwareVersion { get; set; }
            public string? CoboVaultFirmwareVersion { get; set; }
        }
        public KeyStoreFormat? keystore { get; set; }
    }
    public bool TryParse(BTCPayNetwork network, string data, [MaybeNullWhen(false)] out DerivationSchemeSettings derivationSchemeSettings)
    {
        derivationSchemeSettings = null;
        var jobj = JsonConvert.DeserializeObject<ElectrumFormat>(data);
        if (jobj?.keystore is null)
            return false;

        var result = new DerivationSchemeSettings { Network = network };
        var derivationSchemeParser = network.GetDerivationSchemeParser();
        result.Source = "ElectrumFile";

        if (jobj.keystore.xpub is null)
            return false;

        if (!derivationSchemeParser.TryParseXpub(jobj.keystore.xpub, ref result, true))
            return false;
        
        if (jobj.keystore.label is not null)
            result.Label = jobj.keystore.label;

        if (jobj.keystore.ckcc_xfp is not null)
            result.AccountKeySettings[0].RootFingerprint = new HDFingerprint(jobj.keystore.ckcc_xfp.Value);
        if (jobj.keystore.root_fingerprint is not null)
            result.AccountKeySettings[0].RootFingerprint = HDFingerprint.Parse(jobj.keystore.root_fingerprint);
        if (jobj.keystore.derivation is not null)
            result.AccountKeySettings[0].AccountKeyPath = new KeyPath(jobj.keystore.derivation);
        if (jobj.keystore.ColdCardFirmwareVersion is not null)
            result.Source = "ColdCard";
        else if (jobj.keystore.CoboVaultFirmwareVersion is not null)
            result.Source = "CoboVault";
        derivationSchemeSettings = result;
        return true;
    }
}
