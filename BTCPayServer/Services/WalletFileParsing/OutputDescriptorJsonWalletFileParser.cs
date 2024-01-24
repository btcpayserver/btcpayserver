#nullable enable
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;
namespace BTCPayServer.Services.WalletFileParsing;
public class OutputDescriptorJsonWalletFileParser(OutputDescriptorWalletFileParser outputDescriptorOnChainWalletParser)
    : IWalletFileParser
{
    class OutputDescriptorJsonWalletFileFormat
    {
        [JsonProperty("Descriptor")]
        public string? Descriptor { get; set; }
        [JsonProperty("Source")]
        public string? Source { get; set; }
    }

    public bool TryParse(BTCPayNetwork network, string data, [MaybeNullWhen(false)] out DerivationSchemeSettings derivationSchemeSettings, [MaybeNullWhen(true)] out string error)
    {
        derivationSchemeSettings = null;
        var jobj = JsonConvert.DeserializeObject<OutputDescriptorJsonWalletFileFormat>(data);
        if (string.IsNullOrEmpty(jobj?.Descriptor))
        {
            error = "Missing descriptor";
            return false;
        }

        if (!outputDescriptorOnChainWalletParser.TryParse(network, jobj.Descriptor, out derivationSchemeSettings, out error))
            return false;
        if (!string.IsNullOrEmpty(jobj.Source))
            derivationSchemeSettings.Source = jobj.Source;
        return true;
    }
}
