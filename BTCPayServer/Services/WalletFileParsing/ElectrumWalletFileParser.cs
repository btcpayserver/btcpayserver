#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.IO;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace BTCPayServer.Services.WalletFileParsing;
public class ElectrumWalletFileParser : IWalletFileParser
{
    public string[] SourceHandles => ["ElectrumFile", "ColdCard", "CoboVault"];

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

    public bool TryParse(BTCPayNetwork network, string data, [MaybeNullWhen(false)] out DerivationSchemeSettings derivationSchemeSettings, [MaybeNullWhen(true)] out string error)
    {
        error = null;
        derivationSchemeSettings = null;
        var jobj = DeserializeObject<ElectrumFormat>(data);
        if (string.IsNullOrEmpty(jobj?.keystore?.xpub))
        {
            error = "Missing xpub";
            return false;
        }

        var derivationSchemeParser = network.GetDerivationSchemeParser();
        var result = new DerivationSchemeSettings { Network = network, Source = SourceHandles.First() };

        if (!derivationSchemeParser.TryParseXpub(jobj.keystore.xpub, ref result, out error, true))
            return false;
        
        if (!string.IsNullOrEmpty(jobj.keystore.label))
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
