#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Newtonsoft.Json;
namespace BTCPayServer.Services.WalletFileParsing;
public class SpecterWalletFileParser(OutputDescriptorWalletFileParser outputDescriptorOnChainWalletParser) : IWalletFileParser
{
    public string[] SourceHandles => ["SpecterFile"];

    class SpecterFormat
    {
        [JsonProperty("descriptor")]
        public string? Descriptor { get; set; }
        [JsonProperty("label")]
        public string? Label { get; set; }
        [JsonProperty("blockheight")]
        public int? Blockheight { get; set; }
    }

    public bool TryParse(BTCPayNetwork network, string data, [MaybeNullWhen(false)] out DerivationSchemeSettings derivationSchemeSettings, [MaybeNullWhen(true)] out string error)
    {
        derivationSchemeSettings = null;
        var jobj = JsonConvert.DeserializeObject<SpecterFormat>(data);
        if (string.IsNullOrEmpty(jobj?.Descriptor) || jobj.Blockheight is null)
        {
            error = "Not a Specter file";
            return false;
        }
        
        if (!outputDescriptorOnChainWalletParser.TryParse(network, jobj.Descriptor, out derivationSchemeSettings, out error))
            return false;

        derivationSchemeSettings.Source = SourceHandles.First();
        if (!string.IsNullOrEmpty(jobj.Label))
            derivationSchemeSettings.Label = jobj.Label;

        return true;
    }
}
