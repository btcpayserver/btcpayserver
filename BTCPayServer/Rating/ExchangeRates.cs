using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Rating
{
    public class ExchangeRates : IEnumerable<ExchangeRate>
    {
        Dictionary<string, ExchangeRate> _AllRates = new Dictionary<string, ExchangeRate>();
        public ExchangeRates()
        {

        }
        public ExchangeRates(IEnumerable<ExchangeRate> rates)
        {
            foreach (var rate in rates)
            {
                Add(rate);
            }
        }
        List<ExchangeRate> _Rates = new List<ExchangeRate>();
        public MultiValueDictionary<string, ExchangeRate> ByExchange
        {
            get;
            private set;
        } = new MultiValueDictionary<string, ExchangeRate>();

        public void Add(ExchangeRate rate)
        {
            // 1 DOGE is always 1 DOGE
            if (rate.CurrencyPair.Left == rate.CurrencyPair.Right)
                return;
            var key = $"({rate.Exchange}) {rate.CurrencyPair}";
            if (_AllRates.TryAdd(key, rate))
            {
                _Rates.Add(rate);
                ByExchange.Add(rate.Exchange, rate);
            }
            else
            {
                if (rate.Value.HasValue)
                {
                    _AllRates[key].Value = rate.Value;
                }
            }
        }

        public IEnumerator<ExchangeRate> GetEnumerator()
        {
            return _Rates.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void SetRate(string exchangeName, CurrencyPair currencyPair, decimal value)
        {
            if (ByExchange.TryGetValue(exchangeName, out var rates))
            {
                var rate = rates.FirstOrDefault(r => r.CurrencyPair == currencyPair);
                if (rate != null)
                    rate.Value = value;
            }
        }
        public decimal? GetRate(string exchangeName, CurrencyPair currencyPair)
        {
            if (currencyPair.Left == currencyPair.Right)
                return 1.0m;
            if (ByExchange.TryGetValue(exchangeName, out var rates))
            {
                var rate = rates.FirstOrDefault(r => r.CurrencyPair == currencyPair);
                if (rate != null)
                    return rate.Value;
            }
            return null;
        }
    }
    public class ExchangeRate
    {
        public string Exchange { get; set; }
        public CurrencyPair CurrencyPair { get; set; }
        public decimal? Value { get; set; }

        public override string ToString()
        {
            if (Value == null)
                return $"{Exchange}({CurrencyPair})";
            return $"{Exchange}({CurrencyPair}) == {Value.Value.ToString(CultureInfo.InvariantCulture)}";
        }
    }
}
