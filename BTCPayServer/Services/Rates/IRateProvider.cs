using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace BTCPayServer.Services.Rates
{
	public class Rate
	{
		public Rate()
		{

		}
		public Rate(string currency, decimal value)
		{
			Value = value;
			Currency = currency;
		}
		public string Currency
		{
			get; set;
		}
		public decimal Value
		{
			get; set;
		}
	}
    public interface IRateProvider
    {
		Task<decimal> GetRateAsync(string currency);
		Task<ICollection<Rate>> GetRatesAsync();
	}
}
