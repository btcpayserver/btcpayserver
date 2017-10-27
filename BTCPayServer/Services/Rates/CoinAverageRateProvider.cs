using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace BTCPayServer.Services.Rates
{
	public class CoinAverageException : Exception
	{
		public CoinAverageException(string message) : base(message)
		{

		}
	}
	public class CoinAverageRateProvider : IRateProvider
	{
		public class RatesJson
		{
			public class RateJson
			{
				public string Code
				{
					get; set;
				}
				public decimal Rate
				{
					get; set;
				}
			}

			[JsonProperty("rates")]
			public JObject RatesInternal
			{
				get; set;
			}
			[JsonIgnore]
			public List<RateJson> Rates
			{
				get; set;
			}

			[JsonIgnore]
			public Dictionary<string, decimal> RatesByCurrency
			{
				get; set;
			}

			public decimal GetRate(string currency)
			{
				if(!RatesByCurrency.TryGetValue(currency.ToUpperInvariant(), out decimal currUSD))
					throw new RateUnavailableException(currency);

				if(!RatesByCurrency.TryGetValue("BTC", out decimal btcUSD))
					throw new RateUnavailableException(currency);

				return currUSD / btcUSD;
			}
			public void CalculateDictionary()
			{
				RatesByCurrency = new Dictionary<string, decimal>();
				Rates = new List<RateJson>();
				foreach(var rate in RatesInternal.OfType<JProperty>())
				{
					var rateJson = new RateJson();
					rateJson.Code = rate.Name;
					rateJson.Rate = rate.Value["rate"].Value<decimal>();
					RatesByCurrency.Add(rate.Name, rateJson.Rate);
					Rates.Add(rateJson);
				}
			}
		}
		static HttpClient _Client = new HttpClient();

		public string Market
		{
			get; set;
		} = "global";
		public async Task<decimal> GetRateAsync(string currency)
		{
			RatesJson rates = await GetRatesCore();
			return rates.GetRate(currency);
		}

		private async Task<RatesJson> GetRatesCore()
		{
			var resp = await _Client.GetAsync("https://apiv2.bitcoinaverage.com/constants/exchangerates/" + Market);
			using(resp)
			{

				if((int)resp.StatusCode == 401)
					throw new CoinAverageException("Unauthorized access to the API");
				if((int)resp.StatusCode == 429)
					throw new CoinAverageException("Exceed API limits");
				if((int)resp.StatusCode == 403)
					throw new CoinAverageException("Unauthorized access to the API, premium plan needed");
				resp.EnsureSuccessStatusCode();
				var rates = JsonConvert.DeserializeObject<RatesJson>(await resp.Content.ReadAsStringAsync());
				rates.CalculateDictionary();
				return rates;
			}
		}

		public async Task<ICollection<Rate>> GetRatesAsync()
		{
			RatesJson rates = await GetRatesCore();
			return rates.Rates.Select(o => new Rate()
			{
				Currency = o.Code,
				Value = rates.GetRate(o.Code)
			}).ToList();
		}
	}
}
