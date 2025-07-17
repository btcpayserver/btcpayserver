#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BTCPayServer.Data;
using BTCPayServer.Rating;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Invoices;

public class RateBook
{
    public static RateBook Parse(string rates, string defaultCurrency)
    {
        ArgumentNullException.ThrowIfNull(rates);
        ArgumentNullException.ThrowIfNull(defaultCurrency);
        if (rates == "")
            return new(defaultCurrency, new());
        var o = JObject.Parse(rates);
        var ratesDict = new Dictionary<string, decimal>();
        foreach (var property in o.Properties())
        {
            ratesDict.Add(property.Name, decimal.Parse(property.Value.ToString(), CultureInfo.InvariantCulture));
        }
        return new RateBook(defaultCurrency, ratesDict);
    }

    public RateBook()
    {
        Rates = new();
    }
    public RateBook(
        string defaultCurrency,
        Dictionary<string, decimal> rates
    )
    {
        Rates = new(rates.Count);
        foreach (var rate in rates)
        {
            if (!rate.Key.Contains('_', StringComparison.Ordinal))
                Rates.Add(new CurrencyPair(rate.Key, defaultCurrency), rate.Value);
            else
                Rates.Add(CurrencyPair.Parse(rate.Key), rate.Value);
        }
    }
    public Dictionary<CurrencyPair, decimal> Rates { get; }

    public decimal? TryGetRate(CurrencyPair pair)
    {
        if (GetFastLaneRate(pair, out var tryGetRate)) return tryGetRate;

        var rule = GetRateRules().GetRuleFor(pair);
        rule.Reevaluate();
        if (rule.BidAsk is null)
            return null;
        return rule.BidAsk.Bid;
    }

    private bool GetFastLaneRate(CurrencyPair pair, out decimal v)
    {
        ArgumentNullException.ThrowIfNull(pair);
        if (Rates.TryGetValue(pair, out var rate)) // Fast lane
        {
            v = rate;
            return true;
        }
        v = 0m;
        return false;
    }

    public decimal GetRate(CurrencyPair pair)
    {
        if (GetFastLaneRate(pair, out var v)) return v;
        var rule = GetRateRules().GetRuleFor(pair);
        rule.Reevaluate();
        if (rule.BidAsk is null)
            throw new InvalidOperationException($"Rate rule is not evaluated ({rule.Errors.First()})");
        return rule.BidAsk.Bid;
    }

    public bool TryGetRate(CurrencyPair pair, out decimal rate)
    {
        if (GetFastLaneRate(pair, out rate)) return true;
        var rule = GetRateRules().GetRuleFor(pair);
        rule.Reevaluate();
        if (rule.BidAsk is null)
        {
            rate = 0.0m;
            return false;
        }

        rate = rule.BidAsk.Bid;
        return true;
    }

    public RateRules GetRateRules()
    {
        var builder = new StringBuilder();
        foreach (var r in Rates)
        {
            builder.AppendLine($"{r.Key} = {r.Value.ToString(CultureInfo.InvariantCulture)};");
        }

        if (RateRules.TryParse(builder.ToString(), out var rules))
            return rules;
        throw new FormatException("Invalid rate rules");
    }

    public void AddRates(RateBook? otherBook)
    {
        if (otherBook is null)
            return;
        foreach (var rate in otherBook.Rates)
        {
            this.Rates.TryAdd(rate.Key, rate.Value);
        }
    }

    public static RateBook? FromTxWalletObject(WalletObjectData txObject)
    {
        var rates = txObject.GetData()?["rates"] as JObject;
        if (rates is null)
            return null;
        var cryptoCode = WalletId.Parse(txObject.WalletId).CryptoCode;
        return FromJObject(rates, cryptoCode);
    }

    public static RateBook? FromJObject(JObject rates, string cryptoCode)
    {
        var result = new RateBook();
        foreach (var property in rates.Properties())
        {
            var rate = decimal.Parse(property.Value.ToString(), CultureInfo.InvariantCulture);
            result.Rates.TryAdd(new CurrencyPair(cryptoCode, property.Name), rate);
        }
        return result;
    }

    public void AddCurrencies(HashSet<string> trackedCurrencies)
    {
        foreach (var r in Rates)
        {
            trackedCurrencies.Add(r.Key.Left);
            trackedCurrencies.Add(r.Key.Right);
        }
    }
}
