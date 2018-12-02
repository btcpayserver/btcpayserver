using System;
using NBitcoin;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Text;
using System.Globalization;

namespace BTCPayServer.Services.Rates
{
    public class CurrencyData
    {
        public string Name
        {
            get;
            internal set;
        }
        public string Code
        {
            get;
            internal set;
        }
        public int Divisibility
        {
            get;
            internal set;
        }
        public string Symbol
        {
            get;
            internal set;
        }
        public bool Crypto { get; set; }
    }
    public class CurrencyNameTable
    {
        public CurrencyNameTable()
        {
            _Currencies = LoadCurrency().ToDictionary(k => k.Code);
        }

        static Dictionary<string, IFormatProvider> _CurrencyProviders = new Dictionary<string, IFormatProvider>();

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

                    foreach (var network in new BTCPayNetworkProvider(NetworkType.Mainnet).GetAll())
                    {
                        AddCurrency(_CurrencyProviders, network.CryptoCode, 8, network.CryptoCode);
                    }
                }
                return _CurrencyProviders.TryGet(currency);
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
            while (true)
            {
                var rounded = decimal.Round(value, divisibility, MidpointRounding.AwayFromZero);
                if ((Math.Abs(rounded - value) / value) < 0.001m)
                {
                    value = rounded;
                    break;
                }
                divisibility++;
            }
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

        Dictionary<string, CurrencyData> _Currencies;

        static CurrencyData[] LoadCurrency()
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BTCPayServer.Currencies.txt");
            string content = null;
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                content = reader.ReadToEnd();
            }
            var currencies = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            Dictionary<string, CurrencyData> dico = new Dictionary<string, CurrencyData>();
            foreach (var currency in currencies)
            {
                var splitted = currency.Split(new[] { '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (splitted.Length < 3)
                    continue;
                CurrencyData info = new CurrencyData();
                info.Name = splitted[0];
                info.Code = splitted[1];
                int divisibility;
                if (!int.TryParse(splitted[2], out divisibility))
                    continue;
                info.Divisibility = divisibility;
                if (!dico.ContainsKey(info.Code))
                    dico.Add(info.Code, info);
                if (splitted.Length >= 4)
                {
                    info.Symbol = splitted[3];
                }
            }

            foreach (var network in new BTCPayNetworkProvider(NetworkType.Mainnet).GetAll())
            {
                if (!dico.TryAdd(network.CryptoCode, new CurrencyData()
                {
                    Code = network.CryptoCode,
                    Divisibility = 8,
                    Name = network.CryptoCode,
                    Crypto = true
                }))
                {
                    dico[network.CryptoCode].Crypto = true;
                }
            }

            return dico.Values.ToArray();
        }

        public CurrencyData GetCurrencyData(string currency, bool useFallback)
        {
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
