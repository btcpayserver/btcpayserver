using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Text;

namespace BTCPayServer.RateProvider
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
	}
	public class CurrencyNameTable
    {
		public CurrencyNameTable()
		{
			_Currencies = LoadCurrency().ToDictionary(k => k.Code);
		}


		Dictionary<string, CurrencyData> _Currencies;

		static CurrencyData[] LoadCurrency()
		{
			var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BTCPayServer.Currencies.txt");
			string content = null;
			using(var reader = new StreamReader(stream, Encoding.UTF8))
			{
				content = reader.ReadToEnd();
			}
			var currencies = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
			Dictionary<string, CurrencyData> dico = new Dictionary<string, CurrencyData>();
			foreach(var currency in currencies)
			{
				var splitted = currency.Split(new[] { '\t' }, StringSplitOptions.RemoveEmptyEntries);
				if(splitted.Length < 3)
					continue;
				CurrencyData info = new CurrencyData();
				info.Name = splitted[0];
				info.Code = splitted[1];
				int divisibility;
				if(!int.TryParse(splitted[2], out divisibility))
					continue;
				info.Divisibility = divisibility;
				if(!dico.ContainsKey(info.Code))
					dico.Add(info.Code, info);
				if(splitted.Length >= 4)
				{
					info.Symbol = splitted[3];
				}
			}
			return dico.Values.ToArray();
		}

		public CurrencyData GetCurrencyData(string currency)
		{
			CurrencyData result;
			_Currencies.TryGetValue(currency.ToUpperInvariant(), out result);
			return result;
		}
		
	}
}
