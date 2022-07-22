using System;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Plugins.PayButton.Models;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using NicolasDorier.RateLimits;

namespace BTCPayServer.Controllers
{
    public class UIPublicController : Controller
    {
        public UIPublicController(UIInvoiceController invoiceController,
            StoreRepository storeRepository)
        {
            _InvoiceController = invoiceController;
            _StoreRepository = storeRepository;
        }

        private readonly UIInvoiceController _InvoiceController;
        private readonly StoreRepository _StoreRepository;

        [HttpGet]
        [IgnoreAntiforgeryToken]
        [EnableCors(CorsPolicies.All)]
        [Route("api/v1/invoices")]
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
            var store = await _StoreRepository.FindStore(model.StoreId);
            if (store == null)
                ModelState.AddModelError("Store", "Invalid store");
            else
            {
                var storeBlob = store.GetStoreBlob();
                if (!storeBlob.AnyoneCanInvoice)
                    ModelState.AddModelError("Store", "Store has not enabled Pay Button");
            }

            if (model == null || (model.Price is decimal v ? v <= 0 : false))
                ModelState.AddModelError("Price", "Price must be greater than 0");

            if (!ModelState.IsValid)
                return View();

            InvoiceEntity invoice = null;
            try
            {
                invoice = await _InvoiceController.CreateInvoiceCoreRaw(new CreateInvoiceRequest()
                {
                    Amount = model.Price,
                    Type = model.Price is null? InvoiceType.TopUp: InvoiceType.Standard,
                    Currency = model.Currency,
                    Checkout = new InvoiceDataBase.CheckoutOptions()
                    {
                        RedirectURL = model.BrowserRedirect,
                        DefaultPaymentMethod = model.DefaultPaymentMethod
                    },
                    Metadata = new InvoiceMetadata()
                    {
                        ItemDesc = model.CheckoutDesc,
                        OrderId = model.OrderId,
                    }.ToJObject()
                    
                }, store, HttpContext.Request.GetAbsoluteRoot(), cancellationToken: cancellationToken);
            }
            catch (Exception e)
            {
                ModelState.AddModelError("Store", e.Message);
                if (model.JsonResponse)
                {
                    return BadRequest(ModelState);
                }

                return View();
            }


            var url = Request.GetAbsoluteUri(Url.Action("Checkout", "UIInvoice", new {invoiceId = invoice.Id}));
            if (model.JsonResponse)
            {
                return Json(new
                {
                    InvoiceId = invoice.Id,
                    InvoiceUrl = url
                });
            }

            if (string.IsNullOrEmpty(model.CheckoutQueryString))
            {
                return Redirect(url);
            }

            var additionalParamValues = HttpUtility.ParseQueryString(model.CheckoutQueryString);
            var uriBuilder = new UriBuilder(url);
            var paramValues = HttpUtility.ParseQueryString(uriBuilder.Query);
            paramValues.Add(additionalParamValues);
            uriBuilder.Query = paramValues.ToString();
            return Redirect(uriBuilder.Uri.AbsoluteUri);
        }
    }
}
