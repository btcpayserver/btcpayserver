using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Models;
using BTCPayServer.Payments.Changelly;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    [Route("[controller]/{storeId}")]
    public class ChangellyController : Controller
    {
        private readonly ChangellyClientProvider _changellyClientProvider;

        public ChangellyController(ChangellyClientProvider changellyClientProvider)
        {
            _changellyClientProvider = changellyClientProvider;
        }

        [HttpGet]
        [Route("currencies")]
        public async Task<IActionResult> GetCurrencyList(string storeId)
        {
            if (!TryGetChangellyClient(storeId, out var actionResult, out var client))
            {
                return actionResult;
            }

            var result = await client.GetCurrenciesFull();
            if (result.Item2)
            {
                return Ok(result);
            }

            return BadRequest(result);
        }

        [HttpGet]
        [Route("calculate")]
        public async Task<IActionResult> CalculateAmount(string storeId, string fromCurrency, string toCurrency,
            double toCurrencyAmount)
        {
            if (!TryGetChangellyClient(storeId, out var actionResult, out var client))
            {
                return actionResult;
            }

            double? currentAmount = null;
            var callCounter = 0;

            var response1 = await client.GetExchangeAmount(fromCurrency, toCurrency, 1);
            if (!response1.Item2) return BadRequest(response1);
            currentAmount = response1.Amount;

            while (true)
            {
                if (callCounter > 10)
                {
                    BadRequest();
                }
                
                var response2 =await client.GetExchangeAmount(fromCurrency, toCurrency, currentAmount.Value);
                callCounter++;
                if (!response2.Item2) return BadRequest(response2);
                if (response2.Amount < toCurrencyAmount)
                {
                    var newCurrentAmount = ((toCurrencyAmount / response2.Amount) * 1) * currentAmount.Value;
                    currentAmount = newCurrentAmount;
                }
                else
                {
                    return Ok(currentAmount.Value);
                }
            }
        }

        private bool TryGetChangellyClient(string storeId, out IActionResult actionResult,
            out Changelly changelly)
        {
            changelly = null;
            actionResult = null;
            storeId = storeId ?? HttpContext.GetStoreData()?.Id;

            if (!_changellyClientProvider.TryGetChangellyClient(storeId, out var error, out changelly))
            {
                actionResult = BadRequest(new BitpayErrorModel()
                {
                    Error = error
                });
                return false;
            }

            return true;
        }
    }
}
