#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using BTCPayServer;
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

        var result = new BTCPayServer.DerivationSchemeSettings() { Network = network };
        var derivationSchemeParser = network.GetDerivationSchemeParser();
        result.Source = "ElectrumFile";

        if (jobj.keystore.xpub is null || jobj.keystore.ckcc_xfp is null || jobj.keystore.derivation is null)
            return false;

        var strategy = derivationSchemeParser.Parse(jobj.keystore.xpub, false, false, true);
        result.AccountDerivation = strategy;
        result.AccountOriginal = jobj.keystore.xpub;
        result.GetSigningAccountKeySettings();

        if (jobj.keystore.label is not null)
            result.Label = jobj.keystore.label;

        result.AccountKeySettings[0].RootFingerprint = new HDFingerprint(jobj.keystore.ckcc_xfp.Value);
        result.AccountKeySettings[0].AccountKeyPath = new KeyPath(jobj.keystore.derivation);


        if (jobj.keystore.ColdCardFirmwareVersion is not null)
        {
            result.Source = "ColdCard";
        }
        else if (jobj.keystore.CoboVaultFirmwareVersion is not null)
        {
            result.Source = "CoboVault";
        }
        derivationSchemeSettings = result;
        return true;
    }
}
