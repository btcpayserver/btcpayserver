#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Data;
using BTCPayServer.Rating;
using Newtonsoft.Json.Linq;

namespace BTCPayServer;
public record DefaultRules(RateRules Rules)
{
    public record Recommendation : DefaultRules
    {
        public Recommendation(string currency, string exchange) : base($"X_{currency.ToUpperInvariant()} = {exchange.ToLowerInvariant()}(X_{currency.ToUpperInvariant()});")
        {
            Currency = currency.ToUpperInvariant();
            Exchange = exchange.ToLowerInvariant();
        }
        public string Currency { get; }
        public string Exchange { get; }
    }
    public const int HardcodedRecommendedExchangeOrder = 10;
    public DefaultRules(string Rules) : this(RateRules.Parse(Rules))
    {
    }
    public DefaultRules(string[] Rules) : this(RateRules.Combine(Rules.Select(r => RateRules.Parse(r)).ToArray()))
    {
    }
    /// <summary>
    /// Rules are applied in order, the lower the order, the higher the priority. Default is 0.
    /// </summary>
    public int Order { get; set; }
}

public class DefaultRulesCollection
{
    public DefaultRulesCollection(IEnumerable<DefaultRules> defaultRules)
    {
        defaultRules = defaultRules.OrderBy(o => o.Order).ToList();
        Consolidated = RateRules.Combine(defaultRules.Select(r => r.Rules));
        ConsolidatedWithoutRecommendation = RateRules.Combine(defaultRules.Where(r => r is not DefaultRules.Recommendation).Select(r => r.Rules));

        foreach (var recommendation in defaultRules.OfType<DefaultRules.Recommendation>())
        {
            RecommendedExchanges.TryAdd(recommendation.Currency, recommendation.Exchange);
        }
    }

    public RateRules Consolidated { get; private set; }
    public RateRules ConsolidatedWithoutRecommendation { get; private set; }

    public RateRules WithPreferredExchange(string? preferredExchange)
    {
        if (string.IsNullOrEmpty(preferredExchange))
        {
            return Consolidated;
        }
        else
        {
            var catchAll = RateRules.Parse($"X_X = {preferredExchange}(X_X);");
            return RateRules.Combine([catchAll, ConsolidatedWithoutRecommendation]);
        }
    }

    public Dictionary<string, string> RecommendedExchanges { get; } = new Dictionary<string, string>();

    public string GetRecommendedExchange(string currency) =>
        RecommendedExchanges.TryGetValue(currency, out var ex) ? ex : "coingecko";

    public override string ToString() => Consolidated.ToString();
}
