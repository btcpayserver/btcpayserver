#nullable enable
using System;
using BTCPayServer;
using Newtonsoft.Json.Linq;
namespace BTCPayServer.Services.WalletFileParsing;
public class SpecterWalletFileParser : IWalletFileParser
{
    private readonly OutputDescriptorWalletFileParser _outputDescriptorOnChainWalletParser;

    public SpecterWalletFileParser(OutputDescriptorWalletFileParser outputDescriptorOnChainWalletParser)
    {
        _outputDescriptorOnChainWalletParser = outputDescriptorOnChainWalletParser;
    }
    public (DerivationSchemeSettings? DerivationSchemeSettings, string? Error) TryParse(BTCPayNetwork network,
        string data)
    {
        try
        {
            var jobj = JObject.Parse(data);
            if (!jobj.TryGetValue("descriptor", StringComparison.InvariantCultureIgnoreCase, out var descriptorObj)
                || !jobj.ContainsKey("blockheight")
                || descriptorObj?.Value<string>() is not string desc)
                return (null, null);

                
            var result =  _outputDescriptorOnChainWalletParser.TryParse(network, desc);
            if (result.DerivationSchemeSettings is not null)
                result.DerivationSchemeSettings.Source = "Specter";

            if (result.DerivationSchemeSettings is not null && jobj.TryGetValue("label",
                    StringComparison.InvariantCultureIgnoreCase, out var label) && label?.Value<string>() is string labelValue)
                result.DerivationSchemeSettings.Label = labelValue;
            return result;
                
        }
        catch (Exception)
        {
            return (null, null);
        }
    }
}
