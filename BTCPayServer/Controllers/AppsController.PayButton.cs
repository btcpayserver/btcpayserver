using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Services.Apps;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    public partial class AppsController
    {
        // TODO: Need to have talk about how architect default currency implementation
        // For now we have also hardcoded USD for Store creation and then Invoice creation
        const string DEFAULT_CURRENCY = "USD";

        [Route("{appId}/paybutton")]
        public async Task<IActionResult> ViewPayButton(string appId)
        {
            var app = await GetApp(appId, AppType.PayButton);
            if (app == null)
                return NotFound();

            var store = await GetStore(app);
            var currencyDropdown = supportedCurrencies(store);

            var appUrl = HttpContext.Request.GetAbsoluteRoot();
            var model = new PayButtonViewModel
            {
                Price = 10,
                Currency = DEFAULT_CURRENCY,
                ButtonSize = 2,
                UrlRoot = appUrl,
                CurrencyDropdown = currencyDropdown
            };
            return View(model);
        }

        private List<string> supportedCurrencies(StoreData store)
        {
            var paymentMethods = store.GetSupportedPaymentMethods(_NetworkProvider)
                            .Select(a => a.PaymentId.ToString()).ToList();
            var currencyDropdown = new List<string>();
            currencyDropdown.Add(DEFAULT_CURRENCY);
            currencyDropdown.AddRange(paymentMethods);
            return currencyDropdown;
        }

        [HttpPost]
        [Route("{appId}/pay")]
        [IgnoreAntiforgeryToken]
        [EnableCors(CorsPolicies.All)]
        public async Task<IActionResult> PayButtonHandle(string appId, [FromForm]PayButtonViewModel model)
        {
            var app = await GetApp(appId, AppType.PayButton);
            var settings = app.GetSettings<PointOfSaleSettings>();
            var store = await GetStore(app);

            // TODO: extract validation to model
            if (model.Price <= 0)
                ModelState.AddModelError("Price", "Price must be greater than 0");

            var curr = supportedCurrencies(store);
            if (!curr.Contains(model.Currency))
                ModelState.AddModelError("Currency", $"Selected currency {model.Currency} is not supported in this store");
            //

            if (!ModelState.IsValid)
                return View();

            var invoice = await _InvoiceController.CreateInvoiceCore(new NBitpayClient.Invoice()
            {
                Price = model.Price,
                Currency = model.Currency,
                ItemDesc = model.CheckoutDesc,
                OrderId = model.OrderId,
                BuyerEmail = model.NotifyEmail,
                NotificationURL = model.ServerIpn,
                RedirectURL = model.BrowserRedirect,
                FullNotifications = true
            }, store, HttpContext.Request.GetAbsoluteRoot());
            return Redirect(invoice.Data.Url);
        }

        [HttpGet]
        [Route("{appId}/paybuttontest")]
        public IActionResult PayButtonTest(string appId)
        {
            return View();
        }
    }
}
