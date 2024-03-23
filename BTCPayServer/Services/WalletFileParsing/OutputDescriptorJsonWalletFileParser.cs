#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using BTCPayServer;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace BTCPayServer.Services.WalletFileParsing;
public class OutputDescriptorJsonWalletFileParser : IWalletFileParser
{
    private readonly OutputDescriptorWalletFileParser _outputDescriptorOnChainWalletParser;

    class OutputDescriptorJsonWalletFileFormat
    {
        public string? Descriptor { get; set; }
        public string? Source { get; set; }
    }
    public OutputDescriptorJsonWalletFileParser(OutputDescriptorWalletFileParser outputDescriptorOnChainWalletParser)
    {
        _outputDescriptorOnChainWalletParser = outputDescriptorOnChainWalletParser;
    }
    public bool TryParse(BTCPayNetwork network, string data, [MaybeNullWhen(false)] out DerivationSchemeSettings derivationSchemeSettings)
    {
        derivationSchemeSettings = null;
        var jobj = JsonConvert.DeserializeObject<OutputDescriptorJsonWalletFileFormat>(data);
        if (jobj?.Descriptor is null)
            return false;

        if (!_outputDescriptorOnChainWalletParser.TryParse(network, jobj.Descriptor, out derivationSchemeSettings))
            return false;
        if (jobj.Source is not null)
            derivationSchemeSettings.Source = jobj.Source;
        return true;
    }
}
