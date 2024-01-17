#nullable enable
using System;
using BTCPayServer;
using Newtonsoft.Json.Linq;

public class OutputDescriptorJsonOnChainWalletParser : OnChainWalletParser
{
    private readonly OutputDescriptorOnChainWalletParser _outputDescriptorOnChainWalletParser;

    public OutputDescriptorJsonOnChainWalletParser(OutputDescriptorOnChainWalletParser outputDescriptorOnChainWalletParser)
    {
        _outputDescriptorOnChainWalletParser = outputDescriptorOnChainWalletParser;
    }
    public (DerivationSchemeSettings? DerivationSchemeSettings, string? Error) TryParse(BTCPayNetwork network,
        string data)
    {
        try
        {
            var jobj = JObject.Parse(data);
            if (!jobj.TryGetValue("Descriptor", StringComparison.InvariantCultureIgnoreCase, out var descriptorToken) ||
                descriptorToken?.Value<string>() is not string desc)
                return (null, null);

                
            var result =  _outputDescriptorOnChainWalletParser.TryParse(network, desc);
            if (result.DerivationSchemeSettings is not null && jobj.TryGetValue("Source", StringComparison.InvariantCultureIgnoreCase, out var sourceToken))
                result.DerivationSchemeSettings.Source = sourceToken.Value<string>();
            return result;
                
        }
        catch (Exception)
        {
            return (null, null);
        }
    }
}
