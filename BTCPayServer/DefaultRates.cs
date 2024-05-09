using System;
using System.Linq;
using BTCPayServer.Rating;

namespace BTCPayServer;
public record DefaultRates(RateRules Rules)
{
    public DefaultRates(string[] Rules) : this(RateRules.Combine(Rules.Select(r => RateRules.Parse(r)).ToArray()))
    {
    }
}
