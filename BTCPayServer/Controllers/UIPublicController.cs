using System;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Plugins.PayButton.Models;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

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

            DataWrapper<InvoiceResponse> invoice = null;
            try
            {
                invoice = await _InvoiceController.CreateInvoiceCore(new BitpayCreateInvoiceRequest()
                {
                    Price = model.Price,
                    Currency = model.Currency,
                    ItemDesc = model.CheckoutDesc,
                    OrderId = model.OrderId,
                    NotificationEmail = model.NotifyEmail,
                    NotificationURL = model.ServerIpn,
                    RedirectURL = model.BrowserRedirect,
                    FullNotifications = true,
                    DefaultPaymentMethod = model.DefaultPaymentMethod
                }, store, HttpContext.Request.GetAbsoluteRoot(), cancellationToken: cancellationToken);
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

            if (model.JsonResponse)
            {
                return Json(new
                {
                    InvoiceId = invoice.Data.Id,
                    InvoiceUrl = invoice.Data.Url
                });
            }

            if (string.IsNullOrEmpty(model.CheckoutQueryString))
            {
                return Redirect(invoice.Data.Url);
            }

            var additionalParamValues = HttpUtility.ParseQueryString(model.CheckoutQueryString);
            var uriBuilder = new UriBuilder(invoice.Data.Url);
            var paramValues = HttpUtility.ParseQueryString(uriBuilder.Query);
            paramValues.Add(additionalParamValues);
            uriBuilder.Query = paramValues.ToString();
            return Redirect(uriBuilder.Uri.AbsoluteUri);
        }
    }
}
