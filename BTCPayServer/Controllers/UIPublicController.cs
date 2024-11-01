using System;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers.Greenfield;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Plugins.PayButton.Models;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Localization;
using NicolasDorier.RateLimits;

namespace BTCPayServer.Controllers
{
    public class UIPublicController : Controller
    {
        public UIPublicController(UIInvoiceController invoiceController,
            StoreRepository storeRepository,
            IStringLocalizer stringLocalizer,
            LinkGenerator linkGenerator)
        {
            _InvoiceController = invoiceController;
            _StoreRepository = storeRepository;
            _linkGenerator = linkGenerator;
            StringLocalizer = stringLocalizer;
        }

        private readonly UIInvoiceController _InvoiceController;
        private readonly StoreRepository _StoreRepository;
        private readonly LinkGenerator _linkGenerator;
        public IStringLocalizer StringLocalizer { get; }

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
                ModelState.AddModelError("Store", StringLocalizer["Invalid store"]);
            else
            {
                var storeBlob = store.GetStoreBlob();
                if (!storeBlob.AnyoneCanInvoice)
                    ModelState.AddModelError("Store", StringLocalizer["Store has not enabled Pay Button"]);
            }

            if (model == null || (model.Price is decimal v ? v <= 0 : false))
                ModelState.AddModelError("Price", StringLocalizer["Price must be greater than 0"]);

            if (!ModelState.IsValid)
                return View();

            InvoiceEntity invoice = null;
            try
            {
                invoice = await _InvoiceController.CreateInvoiceCoreRaw(new CreateInvoiceRequest()
                {
                    Amount = model.Price,
                    Currency = model.Currency,
                    Metadata = new InvoiceMetadata()
                    {
                        ItemDesc = model.CheckoutDesc,
                        OrderId = model.OrderId
                    }.ToJObject(),
                    Checkout = new ()
                    {
                        RedirectURL = model.BrowserRedirect ?? store?.StoreWebsite,
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

            var url = GreenfieldInvoiceController.ToModel(invoice, _linkGenerator, HttpContext.Request).CheckoutLink;
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
