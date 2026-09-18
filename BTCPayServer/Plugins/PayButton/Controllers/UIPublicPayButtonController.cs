using System;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Controllers.Greenfield;
using BTCPayServer.Data;
using BTCPayServer.Plugins.PayButton.Models;
using BTCPayServer.Plugins.Tax;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;
using NicolasDorier.RateLimits;

namespace BTCPayServer.Plugins.PayButton.Controllers
{
    [Area(PayButtonPlugin.Area)]
    public class UIPublicPayButtonController(
        UIInvoiceController invoiceController,
        StoreRepository storeRepository,
        IStringLocalizer stringLocalizer,
        CurrencyNameTable currencyNameTable,
        TaxRateResolver taxRateResolver,
        LinkGenerator linkGenerator)
        : Controller
    {
        public IStringLocalizer StringLocalizer { get; } = stringLocalizer;

        [HttpGet]
        [IgnoreAntiforgeryToken]
        [EnableCors(CorsPolicies.All)]
        [Route("api/v1/invoices")]
        [RateLimitsFilter(ZoneLimits.PublicInvoices, Scope = RateLimitsScope.RemoteAddress)]
        public async Task<IActionResult> PayButtonHandle(PayButtonViewModel model)
        {
            return await PayButtonHandle(model, CancellationToken.None);
        }

        [HttpPost]
        [Route("api/v1/invoices")]
        [IgnoreAntiforgeryToken]
        [EnableCors(CorsPolicies.All)]
        [RateLimitsFilter(ZoneLimits.PublicInvoices, Scope = RateLimitsScope.RemoteAddress)]
        public async Task<IActionResult> PayButtonHandle([FromForm] PayButtonViewModel model, CancellationToken cancellationToken)
        {
            var store = await storeRepository.FindStore(model.StoreId);
            if (store is null)
                ModelState.AddModelError("Store", StringLocalizer["Invalid store"]);
            else
            {
                var storeBlob = store.GetStoreBlob();
                if (!storeBlob.AnyoneCanInvoice)
                    ModelState.AddModelError("Store", StringLocalizer["Store has not enabled Pay Button"]);
            }

            if (model.Price is decimal and <= 0)
                ModelState.AddModelError("Price", StringLocalizer["Price must be greater than 0"]);

            if (!ModelState.IsValid || store is null)
                return View();

            InvoiceEntity invoice;
            try
            {
                decimal? taxAmount = null;
                var priceForInvoice = model.Price ?? 0m;
                if (!string.IsNullOrEmpty(model.TaxRateId))
                {
                    var storeRate = taxRateResolver.Resolve(store.GetStoreBlob(), model.TaxRateId);
                    if (storeRate != null)
                    {
                        var decimals = currencyNameTable.GetCurrencyData(model.Currency, false)?.Divisibility ?? 2;
                        var result = TaxCalculator.Calculate(priceForInvoice, storeRate.Rate, taxIncluded: false, decimals);
                        priceForInvoice = result.PriceTaxIncluded;
                        taxAmount = result.Tax;
                    }
                }
                invoice = await invoiceController.CreateInvoiceCoreRaw(new CreateInvoiceRequest()
                {
                    Amount = model.Price,
                    Currency = model.Currency,
                    Metadata = new InvoiceMetadata()
                    {
                        ItemDesc = model.CheckoutDesc,
                        OrderId = model.OrderId,
                        TaxIncluded = taxAmount
                    }.ToJObject(),
                    Checkout = new ()
                    {
                        RedirectURL = model.BrowserRedirect ?? store.StoreWebsite,
                        DefaultPaymentMethod = model.DefaultPaymentMethod
                    }
                }, store, HttpContext.Request.GetAbsoluteRoot(),
                entityManipulator: (entity) =>
                {
                    entity.NotificationEmail = model.NotifyEmail;
                    entity.NotificationURLTemplate = model.ServerIpn;
                    entity.FullNotifications = true;
                },
                cancellationToken: cancellationToken);
            }
            catch (BitpayHttpException e)
            {
                ModelState.AddModelError("Store", e.Message);
                if (model.JsonResponse)
                {
                    return BadRequest(ModelState);
                }

                return View();
            }

            var url = GreenfieldInvoiceController.ToModel(invoice, linkGenerator, currencyNameTable, HttpContext.Request).CheckoutLink;
            if (!string.IsNullOrEmpty(model.CheckoutQueryString))
            {
                var additionalParamValues = HttpUtility.ParseQueryString(model.CheckoutQueryString);
                var uriBuilder = new UriBuilder(url);
                var paramValues = HttpUtility.ParseQueryString(uriBuilder.Query);
                paramValues.Add(additionalParamValues);
                uriBuilder.Query = paramValues.ToString()!;
                url = uriBuilder.Uri.AbsoluteUri;
            }
            if (model.JsonResponse)
            {
                return Json(new
                {
                    InvoiceId = invoice.Id,
                    InvoiceUrl = url
                });
            }
            return Redirect(url);
        }
    }
}
