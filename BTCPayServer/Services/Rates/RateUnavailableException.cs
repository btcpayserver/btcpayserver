using System;
using System.Collections.Generic;
using System.Text;

namespace BTCPayServer.Services.Rates
{
    public class RateUnavailableException : Exception
    {
		public RateUnavailableException(string currency) : base("Rate unavailable for currency " + currency)
		{
			if(currency == null)
				throw new ArgumentNullException(nameof(currency));
			Currency = currency;
		}

		public string Currency
		{
			get; set;
		}
	}
}
