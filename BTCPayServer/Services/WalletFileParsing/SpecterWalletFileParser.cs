#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using BTCPayServer;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace BTCPayServer.Services.WalletFileParsing;
public class SpecterWalletFileParser : IWalletFileParser
{
    private readonly OutputDescriptorWalletFileParser _outputDescriptorOnChainWalletParser;

    class SpecterFormat
    {
        public string? descriptor { get; set; }
        public int? blockheight { get; set; }
        public string? label { get; set; }
    }
    public SpecterWalletFileParser(OutputDescriptorWalletFileParser outputDescriptorOnChainWalletParser)
    {
        _outputDescriptorOnChainWalletParser = outputDescriptorOnChainWalletParser;
    }
    public bool TryParse(BTCPayNetwork network, string data, [MaybeNullWhen(false)] out DerivationSchemeSettings derivationSchemeSettings)
    {
        derivationSchemeSettings = null;
        var jobj = JsonConvert.DeserializeObject<SpecterFormat>(data);
        if (jobj?.descriptor is null || jobj.blockheight is null)
            return false;
        if (!_outputDescriptorOnChainWalletParser.TryParse(network, jobj.descriptor, out derivationSchemeSettings))
            return false;

        derivationSchemeSettings.Source = "Specter";
        if (jobj.label is not null)
            derivationSchemeSettings.Label = jobj.label;

        return true;
    }
}
