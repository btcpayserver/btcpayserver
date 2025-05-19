using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
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
#nullable enable
    public class RateFetcher
    {
        private readonly RateProviderFactory _rateProviderFactory;

        public RateFetcher(RateProviderFactory rateProviderFactory)
        {
            _rateProviderFactory = rateProviderFactory;
        }

        public RateProviderFactory RateProviderFactory => _rateProviderFactory;

        public Task<RateResult> FetchRate(CurrencyPair pair, RateRules rules, IRateContext? context, CancellationToken cancellationToken)
        => FetchRate(pair, new RateRulesCollection(rules, null), context, cancellationToken);
        public async Task<RateResult> FetchRate(CurrencyPair pair, RateRulesCollection rules, IRateContext? context, CancellationToken cancellationToken)
        {
            return await FetchRates(new HashSet<CurrencyPair>(new[] { pair }), rules, context, cancellationToken).First().Value;
        }

        public Dictionary<CurrencyPair, Task<RateResult>> FetchRates(HashSet<CurrencyPair> pairs, RateRules rules, IRateContext? context, CancellationToken cancellationToken)
        => FetchRates(pairs, new RateRulesCollection(rules, null), context, cancellationToken);

        record FetchContext(
            Dictionary<CurrencyPair, RateRule> Query,
            Dictionary<CurrencyPair, Task<RateResult>> FetchingRates,
            Dictionary<string, Task<QueryRateResult>> FetchingExchanges,
            IRateContext? RateContext,
            CancellationToken CancellationToken)
        {
            public FetchContext(IRateContext? context, CancellationToken cancellationToken)
            :this(new(), new(), new(), context, cancellationToken)
            {

            }
        }

        public Dictionary<CurrencyPair, Task<RateResult>> FetchRates(HashSet<CurrencyPair> pairs, RateRulesCollection rules, IRateContext? context,
            CancellationToken cancellationToken)
        {
            var ctx = new FetchContext(context, cancellationToken);
            void SetQuery(RateRules rateRules)
            {
                ctx.Query.Clear();
                foreach (var p in pairs.Select(p => (Pair: p, RateRule: rateRules.GetRuleFor(p))))
                    ctx.Query.Add(p.Pair, p.RateRule);
            }
            SetQuery(rules.Primary);
            FetchRates(ctx);
            if (rules.Fallback is not null)
            {
                SetQuery(rules.Fallback);
                FetchRates(ctx);
            }
            return ctx.FetchingRates;
        }
        private void FetchRates(FetchContext ctx)
        {
            foreach (var i in ctx.Query)
            {
                var dependentQueries = new List<Task<QueryRateResult>>();
                foreach (var requiredExchange in i.Value.ExchangeRates)
                {
                    if (!ctx.FetchingExchanges.TryGetValue(requiredExchange.Exchange, out var fetching))
                    {
                        fetching = _rateProviderFactory.QueryRates(requiredExchange.Exchange, ctx.RateContext, ctx.CancellationToken);
                        ctx.FetchingExchanges.Add(requiredExchange.Exchange, fetching);
                    }
                    dependentQueries.Add(fetching);
                }

                if (ctx.FetchingRates.TryGetValue(i.Key, out var primaryFetch))
                {
                    ctx.FetchingRates[i.Key] = FallbackGetRuleValue(dependentQueries, i.Value, primaryFetch);
                }
                else
                    ctx.FetchingRates.Add(i.Key, GetRuleValue(dependentQueries, i.Value));
            }
        }

        private async Task<RateResult> FallbackGetRuleValue(List<Task<QueryRateResult>> dependentQueries, RateRule fallbackRateRule, Task<RateResult> primaryFetch)
        {
            var primaryResult = await primaryFetch;
            if (primaryResult.BidAsk != null)
                return primaryResult;
            return await GetRuleValue(dependentQueries, fallbackRateRule);
        }

        public Task<RateResult> FetchRate(RateRule rateRule, IRateContext? context, CancellationToken cancellationToken)
            => FetchRate(new RateRuleCollection(rateRule, null), context, cancellationToken);

        public Task<RateResult> FetchRate(RateRuleCollection rateRule, IRateContext? context, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(rateRule);
            var ctx = new FetchContext(context, cancellationToken);
            void SetQuery(RateRule r)
            {
                ctx.Query.Clear();
                ctx.Query.Add(new("AAA","AAA"), r);
            }
            SetQuery(rateRule.Primary);
            FetchRates(ctx);
            if (rateRule.Fallback is not null)
            {
                SetQuery(rateRule.Fallback);
                FetchRates(ctx);
            }
            return ctx.FetchingRates.First().Value;
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
                foreach (var rule in query.PairRates)
                {
                    rateRule.ExchangeRates.SetRate(query.Exchange, rule.CurrencyPair, rule.BidAsk);
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
