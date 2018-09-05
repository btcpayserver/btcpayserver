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
                if (rate.BidAsk != null)
                {
                    _AllRates[key].BidAsk = rate.BidAsk;
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

        public void SetRate(string exchangeName, CurrencyPair currencyPair, BidAsk bidAsk)
        {
            if (ByExchange.TryGetValue(exchangeName, out var rates))
            {
                var rate = rates.FirstOrDefault(r => r.CurrencyPair == currencyPair);
                if(rate != null)
                {
                    rate.BidAsk = bidAsk;
                }
                var invPair = currencyPair.Inverse();
                var invRate = rates.FirstOrDefault(r => r.CurrencyPair == invPair);
                if (invRate != null)
                {
                    invRate.BidAsk = bidAsk?.Inverse();
                }
            }
        }
        public BidAsk GetRate(string exchangeName, CurrencyPair currencyPair)
        {
            if (currencyPair.Left == currencyPair.Right)
                return BidAsk.One;
            if (ByExchange.TryGetValue(exchangeName, out var rates))
            {
                var rate = rates.FirstOrDefault(r => r.CurrencyPair == currencyPair);
                if (rate != null)
                    return rate.BidAsk;
            }
            return null;
        }
    }
    public class BidAsk
    {

        private readonly static BidAsk _One = new BidAsk(1.0m);
        public static BidAsk One
        {
            get
            {
                return _One;
            }
        }

        private readonly static BidAsk _Zero = new BidAsk(0.0m);
        public static BidAsk Zero
        {
            get
            {
                return _Zero;
            }
        }
        public BidAsk(decimal bid, decimal ask)
        {
            if (bid > ask)
                throw new ArgumentException("the bid should be lower than ask", nameof(bid));
            _Ask = ask;
            _Bid = bid;
        }
        public BidAsk(decimal v) : this(v, v)
        {

        }

        private readonly decimal _Bid;
        public decimal Bid
        {
            get
            {
                return _Bid;
            }
        }


        private readonly decimal _Ask;
        public decimal Ask
        {
            get
            {
                return _Ask;
            }
        }

        public decimal Center => (Ask + Bid) / 2.0m;

        public BidAsk Inverse()
        {
            return new BidAsk(1.0m / Ask, 1.0m / Bid);
        }

        public static BidAsk operator +(BidAsk a, BidAsk b)
        {
            return new BidAsk(a.Bid + b.Bid, a.Ask + b.Ask);
        }

        public static BidAsk operator +(BidAsk a)
        {
            return new BidAsk(a.Bid, a.Ask);
        }

        public static BidAsk operator -(BidAsk a)
        {
            return new BidAsk(-a.Bid, -a.Ask);
        }

        public static BidAsk operator *(BidAsk a, BidAsk b)
        {
            return new BidAsk(a.Bid * b.Bid, a.Ask * b.Ask);
        }

        public static BidAsk operator /(BidAsk a, BidAsk b)
        {
            // This one is tricky.
            // BTC_EUR = (6000, 6100)
            // Implicit rule give
            // EUR_BTC = 1 / BTC_EUR
            // Or
            // EUR_BTC = (1, 1) / BTC_EUR
            // Naive calculation would give us ( 1/6000, 1/6100) = (0.000166, 0.000163)
            // However, this is an invalid BidAsk!!! because 0.000166 > 0.000163
            // So instead, we need to calculate (1/6100, 1/6000)
            return new BidAsk(a.Bid / b.Ask, a.Ask / b.Bid);
        }

        public static BidAsk operator -(BidAsk a, BidAsk b)
        {
            return new BidAsk(a.Bid - b.Bid, a.Ask - b.Ask);
        }


        public override bool Equals(object obj)
        {
            BidAsk item = obj as BidAsk;
            if (item == null)
                return false;
            return Bid == item.Bid && Ask == item.Ask;
        }
        public static bool operator ==(BidAsk a, BidAsk b)
        {
            if (System.Object.ReferenceEquals(a, b))
                return true;
            if (((object)a == null) || ((object)b == null))
                return false;
            return a.Bid == b.Bid && a.Ask == b.Ask;
        }

        public static bool operator !=(BidAsk a, BidAsk b)
        {
            return !(a == b);
        }

        public override int GetHashCode()
        {
            return ToString().GetHashCode(StringComparison.InvariantCulture);
        }

        public override string ToString()
        {
            if (Bid == Ask)
                return Bid.ToString(CultureInfo.InvariantCulture);
            return $"({Bid.ToString(CultureInfo.InvariantCulture)} , {Ask.ToString(CultureInfo.InvariantCulture)})";
        }
    }
    public class ExchangeRate
    {
        public ExchangeRate()
        {

        }
        public ExchangeRate(string exchange, CurrencyPair currencyPair, BidAsk bidAsk)
        {
            this.Exchange = exchange;
            this.CurrencyPair = currencyPair;
            this.BidAsk = bidAsk;
        }
        public string Exchange { get; set; }
        public CurrencyPair CurrencyPair { get; set; }
        public BidAsk BidAsk { get; set; }

        public override string ToString()
        {
            if (BidAsk == null)
                return $"{Exchange}({CurrencyPair})";
            return $"{Exchange}({CurrencyPair}) == {BidAsk.ToString()}";
        }
    }
}
