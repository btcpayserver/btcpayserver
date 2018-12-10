using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using BTCPayServer.Models;
using NBitcoin.DataEncoders;
using NBitcoin.Payment;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Controllers
{
    public class HomeController : Controller
    {
        public IHttpClientFactory HttpClientFactory { get; }

        public HomeController(IHttpClientFactory httpClientFactory)
        {
            HttpClientFactory = httpClientFactory;
        }
        public IActionResult Index()
        {
            return View("Home");
        }

        [Route("translate")]
        public IActionResult BitpayTranslator()
        {
            return View(new BitpayTranslatorViewModel());
        }

        [HttpPost]
        [Route("translate")]
        public async Task<IActionResult> BitpayTranslator(BitpayTranslatorViewModel vm)
        {
            if (!ModelState.IsValid)
                return View(vm);
            vm.BitpayLink = vm.BitpayLink ?? string.Empty;
            vm.BitpayLink = vm.BitpayLink.Trim();
            if (!vm.BitpayLink.StartsWith("bitcoin:", StringComparison.OrdinalIgnoreCase))
            {
                var invoiceId = vm.BitpayLink.Substring(vm.BitpayLink.LastIndexOf("=", StringComparison.OrdinalIgnoreCase) + 1);
                vm.BitpayLink = $"bitcoin:?r=https://bitpay.com/i/{invoiceId}";
            }

            try
            {
                BitcoinUrlBuilder urlBuilder = new BitcoinUrlBuilder(vm.BitpayLink);
#pragma warning disable CS0618 // Type or member is obsolete
                if (!urlBuilder.PaymentRequestUrl.DnsSafeHost.EndsWith("bitpay.com", StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception("This tool only work with bitpay");
                }

                var client = HttpClientFactory.CreateClient();
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, urlBuilder.PaymentRequestUrl);
#pragma warning restore CS0618 // Type or member is obsolete
                request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/payment-request"));
                var result = await client.SendAsync(request);
                // {"network":"main","currency":"BTC","requiredFeeRate":29.834,"outputs":[{"amount":255900,"address":"1PgPo5d4swD6pKfCgoXtoW61zqTfX9H7tj"}],"time":"2018-12-03T14:39:47.162Z","expires":"2018-12-03T14:54:47.162Z","memo":"Payment request for BitPay invoice HHfG8cprRMzZG6MErCqbjv for merchant VULTR Holdings LLC","paymentUrl":"https://bitpay.com/i/HHfG8cprRMzZG6MErCqbjv","paymentId":"HHfG8cprRMzZG6MErCqbjv"}
                var str = await result.Content.ReadAsStringAsync();
                try
                {
                    var jobj = JObject.Parse(str);
                    vm.Address = ((JArray)jobj["outputs"])[0]["address"].Value<string>();
                    var amount = Money.Satoshis(((JArray)jobj["outputs"])[0]["amount"].Value<long>());
                    vm.Amount = amount.ToString();
                    vm.BitcoinUri = $"bitcoin:{vm.Address}?amount={amount.ToString()}";
                }
                catch (JsonReaderException)
                {
                    ModelState.AddModelError(nameof(vm.BitpayLink), $"Invalid or expired bitpay invoice");
                    return View(vm);
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(nameof(vm.BitpayLink), $"Error while requesting {ex.Message}");
                return View(vm);
            }
            return View(vm);
        }

        public IActionResult About()
        {
            ViewData["Message"] = "Your application description page.";

            return View();
        }

        public IActionResult Contact()
        {
            ViewData["Message"] = "Your contact page.";

            return View();
        }

        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
