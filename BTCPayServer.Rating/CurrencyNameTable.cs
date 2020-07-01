using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BTCPayServer.Rating;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Services.Rates
{
    public class CurrencyData
    {
        public string Name { get; set; }
        public string Code { get; set; }
        public int Divisibility { get; set; }
        public string Symbol { get; set; }
        public bool Crypto { get; set; }
    }
    public class CurrencyNameTable
    {
        public static CurrencyNameTable Instance = new CurrencyNameTable();
        public CurrencyNameTable()
        {
            _Currencies = LoadCurrency().ToDictionary(k => k.Code);
        }

        static readonly Dictionary<string, IFormatProvider> _CurrencyProviders = new Dictionary<string, IFormatProvider>();

        public string FormatCurrency(string price, string currency)
        {
            return FormatCurrency(decimal.Parse(price, CultureInfo.InvariantCulture), currency);
        }
        public string FormatCurrency(decimal price, string currency)
        {
            return price.ToString("C", GetCurrencyProvider(currency));
        }

        public NumberFormatInfo GetNumberFormatInfo(string currency, bool useFallback)
        {
            var data = GetCurrencyProvider(currency);
            if (data is NumberFormatInfo nfi)
                return nfi;
            if (data is CultureInfo ci)
                return ci.NumberFormat;
            if (!useFallback)
                return null;
            return CreateFallbackCurrencyFormatInfo(currency);
        }

        private NumberFormatInfo CreateFallbackCurrencyFormatInfo(string currency)
        {
            var usd = GetNumberFormatInfo("USD", false);
            var currencyInfo = (NumberFormatInfo)usd.Clone();
            currencyInfo.CurrencySymbol = currency;
            return currencyInfo;
        }
        public NumberFormatInfo GetNumberFormatInfo(string currency)
        {
            var curr = GetCurrencyProvider(currency);
            if (curr is CultureInfo cu)
                return cu.NumberFormat;
            if (curr is NumberFormatInfo ni)
                return ni;
            return null;
        }
        public IFormatProvider GetCurrencyProvider(string currency)
        {
            lock (_CurrencyProviders)
            {
                if (_CurrencyProviders.Count == 0)
                {
                    foreach (var culture in CultureInfo.GetCultures(CultureTypes.AllCultures).Where(c => !c.IsNeutralCulture))
                    {
                        try
                        {
                            _CurrencyProviders.TryAdd(new RegionInfo(culture.LCID).ISOCurrencySymbol, culture);
                        }
                        catch { }
                    }

                    foreach (var curr in _Currencies.Where(pair => pair.Value.Crypto))
                    {
                        AddCurrency(_CurrencyProviders, curr.Key, curr.Value.Divisibility, curr.Value.Symbol ?? curr.Value.Code);
                    }
                }
                return _CurrencyProviders.TryGet(currency.ToUpperInvariant());
            }
        }

        private void AddCurrency(Dictionary<string, IFormatProvider> currencyProviders, string code, int divisibility, string symbol)
        {
            var culture = new CultureInfo("en-US");
            var number = new NumberFormatInfo();
            number.CurrencyDecimalDigits = divisibility;
            number.CurrencySymbol = symbol;
            number.CurrencyDecimalSeparator = culture.NumberFormat.CurrencyDecimalSeparator;
            number.CurrencyGroupSeparator = culture.NumberFormat.CurrencyGroupSeparator;
            number.CurrencyGroupSizes = culture.NumberFormat.CurrencyGroupSizes;
            number.CurrencyNegativePattern = 8;
            number.CurrencyPositivePattern = 3;
            number.NegativeSign = culture.NumberFormat.NegativeSign;
            currencyProviders.TryAdd(code, number);
        }

        /// <summary>
        /// Format a currency like "0.004 $ (USD)", round to significant divisibility
        /// </summary>
        /// <param name="value">The value</param>
        /// <param name="currency">Currency code</param>
        /// <param name="threeLetterSuffix">Add three letter suffix (like USD)</param>
        /// <returns></returns>
        public string DisplayFormatCurrency(decimal value, string currency, bool threeLetterSuffix = true)
        {
            var provider = GetNumberFormatInfo(currency, true);
            var currencyData = GetCurrencyData(currency, true);
            var divisibility = currencyData.Divisibility;
            value = value.RoundToSignificant(ref divisibility);
            if (divisibility != provider.CurrencyDecimalDigits)
            {
                provider = (NumberFormatInfo)provider.Clone();
                provider.CurrencyDecimalDigits = divisibility;
            }

            if (currencyData.Crypto)
                return value.ToString("C", provider);
            else
                return value.ToString("C", provider) + $" ({currency})";
        }

        readonly Dictionary<string, CurrencyData> _Currencies;

        static CurrencyData[] LoadCurrency()
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BTCPayServer.Rating.Currencies.json");
            string content = null;
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                content = reader.ReadToEnd();
            }

            var currencies = JsonConvert.DeserializeObject<CurrencyData[]>(content);
            return currencies;
        }

        public CurrencyData GetCurrencyData(string currency, bool useFallback)
        {
            if (currency == null)
                throw new ArgumentNullException(nameof(currency));
            CurrencyData result;
            if (!_Currencies.TryGetValue(currency.ToUpperInvariant(), out result))
            {
                if (useFallback)
                {
                    var usd = GetCurrencyData("USD", false);
                    result = new CurrencyData()
                    {
                        Code = currency,
                        Crypto = true,
                        Name = currency,
                        Divisibility = usd.Divisibility
                    };
                }
            }
            return result;
        }

    }
}
