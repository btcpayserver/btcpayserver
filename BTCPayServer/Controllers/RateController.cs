using BTCPayServer.Models;
using BTCPayServer.RateProvider;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Filters;

namespace BTCPayServer.Controllers
{
	public class RateController : Controller
	{
		IRateProvider _RateProvider;
		CurrencyNameTable _CurrencyNameTable;
		public RateController(IRateProvider rateProvider, CurrencyNameTable currencyNameTable)
		{
			_RateProvider = rateProvider ?? throw new ArgumentNullException(nameof(rateProvider));
			_CurrencyNameTable = currencyNameTable ?? throw new ArgumentNullException(nameof(currencyNameTable));
		}

		[Route("rates")]
		[HttpGet]
		[BitpayAPIConstraint]
		public async Task<DataWrapper<NBitpayClient.Rate[]>> GetRates()
		{
			var allRates = (await _RateProvider.GetRatesAsync());
			return new DataWrapper<NBitpayClient.Rate[]>
					(allRates.Select(r =>
							new NBitpayClient.Rate()
							{
								Code = r.Currency,
								Name = _CurrencyNameTable.GetCurrencyData(r.Currency)?.Name,
								Value = r.Value
							}).Where(n => n.Name != null).ToArray());

		}
	}
}
