#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
        var jobj = DeserializeObject<ElectrumFormat>(data);
        if (jobj?.keystore is null)
            return false;

        var result = new DerivationSchemeSettings();
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

    private T? DeserializeObject<T>(string data)
    {
        // We can't call JsonConvert.DeserializeObject directly
        // because some export of Electrum file can have more than one
        // JSON object separated by commas in the file
        JsonTextReader reader = new JsonTextReader(new StringReader(data));
        var o = JObject.ReadFrom(reader);
        return JsonConvert.DeserializeObject<T>(o.ToString());
    }
}
