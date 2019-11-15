using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using BTCPayServer.Models;
using NBitcoin.Payment;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using NBitcoin;
using Newtonsoft.Json;
using BTCPayServer.Services;
using BTCPayServer.HostedServices;
using BTCPayServer.Services.Apps;
using Microsoft.AspNetCore.Identity;
using BTCPayServer.Data;

namespace BTCPayServer.Controllers
{
    public class HomeController : Controller
    {
        private readonly CssThemeManager _cachedServerSettings;

        public IHttpClientFactory HttpClientFactory { get; }
        SignInManager<ApplicationUser> SignInManager { get;  }

        public HomeController(IHttpClientFactory httpClientFactory, 
                              CssThemeManager cachedServerSettings,
                              SignInManager<ApplicationUser> signInManager)
        {
            HttpClientFactory = httpClientFactory;
            _cachedServerSettings = cachedServerSettings;
            SignInManager = signInManager;
        }

        private async Task<ViewResult> GoToApp(string appId, AppType? appType)
        {
            if (appType.HasValue && !string.IsNullOrEmpty(appId))
            {
                switch (appType.Value)
                {
                    case AppType.Crowdfund:
                    {
                        var serviceProvider = HttpContext.RequestServices;
                        var controller = (AppsPublicController)serviceProvider.GetService(typeof(AppsPublicController));
                        controller.Url = Url;
                        controller.ControllerContext = ControllerContext;
                        var res = await controller.ViewCrowdfund(appId, null) as ViewResult;
                        if (res != null)
                        {
                            res.ViewName = "/Views/AppsPublic/ViewCrowdfund.cshtml";
                            return res; // return 
                        }

                        break;
                    }

                    case AppType.PointOfSale:
                    {
                        var serviceProvider = HttpContext.RequestServices;
                        var controller = (AppsPublicController)serviceProvider.GetService(typeof(AppsPublicController));
                        controller.Url = Url;
                        controller.ControllerContext = ControllerContext;
                        var res = await controller.ViewPointOfSale(appId) as ViewResult;
                        if (res != null)
                        {
                            res.ViewName = "/Views/AppsPublic/ViewPointOfSale.cshtml";
                            return res; // return 
                        }

                        break;
                    }
                }
            }
            return null;
        }

        public async Task<IActionResult> Index()
        {
            if (_cachedServerSettings.FirstRun)
            {
                return RedirectToAction(nameof(AccountController.Register), "Account");
            }
            var matchedDomainMapping = _cachedServerSettings.DomainToAppMapping.FirstOrDefault(item =>
                item.Domain.Equals(Request.Host.Host, StringComparison.InvariantCultureIgnoreCase));
            if (matchedDomainMapping != null)
            {
                return await GoToApp(matchedDomainMapping.AppId, matchedDomainMapping.AppType) ?? GoToHome(); 
            }

            return await GoToApp(_cachedServerSettings.RootAppId, _cachedServerSettings.RootAppType) ?? GoToHome(); 
        }

        private IActionResult GoToHome()
        {
            if (SignInManager.IsSignedIn(User))
                return View("Home");
            else
                return RedirectToAction(nameof(AccountController.Login), "Account");
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
                BitcoinUrlBuilder urlBuilder = new BitcoinUrlBuilder(vm.BitpayLink, Network.Main);
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

        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
