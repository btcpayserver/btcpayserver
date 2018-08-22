using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using ExchangeSharp;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using static BTCPayServer.Services.Rates.RateProviderFactory;

namespace BTCPayServer.Services.Rates
{
    public class ExchangeException
    {
        public Exception Exception { get; set; }
        public string ExchangeName { get; set; }
    }
    public class RateResult
    {
        public List<ExchangeException> ExchangeExceptions { get; set; } = new List<ExchangeException>();
        public string Rule { get; set; }
        public string EvaluatedRule { get; set; }
        public HashSet<RateRulesErrors> Errors { get; set; }
        public BidAsk BidAsk { get; set; }
        public TimeSpan Latency { get; internal set; }
    }

    public class RateFetcher
    {
        private readonly RateProviderFactory _rateProviderFactory;

        public RateFetcher(RateProviderFactory rateProviderFactory)
        {
            _rateProviderFactory = rateProviderFactory;
        }

        public RateProviderFactory RateProviderFactory => _rateProviderFactory;

        public async Task<RateResult> FetchRate(CurrencyPair pair, RateRules rules)
        {
            return await FetchRates(new HashSet<CurrencyPair>(new[] { pair }), rules).First().Value;
        }

        public Dictionary<CurrencyPair, Task<RateResult>> FetchRates(HashSet<CurrencyPair> pairs, RateRules rules)
        {
            if (rules == null)
                throw new ArgumentNullException(nameof(rules));

            var fetchingRates = new Dictionary<CurrencyPair, Task<RateResult>>();
            var fetchingExchanges = new Dictionary<string, Task<QueryRateResult>>();
            var consolidatedRates = new ExchangeRates();

            foreach (var i in pairs.Select(p => (Pair: p, RateRule: rules.GetRuleFor(p))))
            {
                var dependentQueries = new List<Task<QueryRateResult>>();
                foreach (var requiredExchange in i.RateRule.ExchangeRates)
                {
                    if (!fetchingExchanges.TryGetValue(requiredExchange.Exchange, out var fetching))
                    {
                        fetching = _rateProviderFactory.QueryRates(requiredExchange.Exchange);
                        fetchingExchanges.Add(requiredExchange.Exchange, fetching);
                    }
                    dependentQueries.Add(fetching);
                }
                fetchingRates.Add(i.Pair, GetRuleValue(dependentQueries, i.RateRule));
            }
            return fetchingRates;
        }

        private async Task<RateResult> GetRuleValue(List<Task<QueryRateResult>> dependentQueries, RateRule rateRule)
        {
            var result = new RateResult();
            foreach (var queryAsync in dependentQueries)
            {
                var query = await queryAsync;
                result.Latency = query.Latency;
                if (query.Exception != null)
                    result.ExchangeExceptions.Add(query.Exception);
                foreach (var rule in query.ExchangeRates)
                {
                    rateRule.ExchangeRates.SetRate(rule.Exchange, rule.CurrencyPair, rule.BidAsk);
                }
            }
            rateRule.Reevaluate();
            result.BidAsk = rateRule.BidAsk;
            result.Errors = rateRule.Errors;
            result.EvaluatedRule = rateRule.ToString(true);
            result.Rule = rateRule.ToString(false);
            return result;
        }
    }
}
