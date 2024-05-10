#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Rating;
using Newtonsoft.Json.Linq;

namespace BTCPayServer;
public record DefaultRules(RateRules Rules)
{
    public DefaultRules(string[] Rules) : this(RateRules.Combine(Rules.Select(r => RateRules.Parse(r)).ToArray()))
    {
    }
}

public class DefaultRulesCollection
{
    public DefaultRulesCollection(IEnumerable<DefaultRules> defaultRules)
    {
        Consolidated = RateRules.Combine(defaultRules.Select(r => r.Rules).ToArray());
    }

    public RateRules Consolidated { get; private set; }

    public RateRules WithPreferredExchange(string preferredExchange)
    {
        var preferredExchangeRule = RateRules.Parse($"X_X = {preferredExchange}(X_X);");
        return RateRules.Combine([Consolidated, preferredExchangeRule]);
    }

    public Dictionary<string, string> RecommendedExchanges = new()
        {
            { "EUR", "kraken" },
            { "USD", "kraken" },
            { "GBP", "kraken" },
            { "CHF", "kraken" },
            { "GTQ", "bitpay" },
            { "COP", "yadio" },
            { "ARS", "yadio" },
            { "JPY", "bitbank" },
            { "TRY", "btcturk" },
            { "UGX", "yadio"},
            { "RSD", "bitpay"},
            { "NGN", "bitnob"}
        };

    public string GetRecommendedExchange(string currency) =>
        RecommendedExchanges.TryGetValue(currency, out var ex) ? ex : "coingecko";

    public override string ToString() => Consolidated.ToString();
}
