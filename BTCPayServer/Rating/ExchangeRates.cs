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
        List<ExchangeRate> _Rates = new List<ExchangeRate>();
        public MultiValueDictionary<string, ExchangeRate> ByExchange
        {
            get;
            private set;
        } = new MultiValueDictionary<string, ExchangeRate>();

        public void Add(ExchangeRate rate)
        {
            _Rates.Add(rate);
            ByExchange.Add(rate.Exchange, rate);
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
            if(ByExchange.TryGetValue(exchangeName, out var rates))
            {
                var rate = rates.FirstOrDefault(r => r.CurrencyPair == currencyPair);
                if (rate != null)
                    rate.Value = value;
            }
        }
        public decimal? GetRate(string exchangeName, CurrencyPair currencyPair)
        {
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
