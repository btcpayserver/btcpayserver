using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using BTCPayServer.Filters;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using BTCPayServer.Data;

namespace BTCPayServer.Controllers
{
    public class PublicController : Controller
    {
        public PublicController(InvoiceController invoiceController,
            StoreRepository storeRepository)
        {
            _InvoiceController = invoiceController;
            _StoreRepository = storeRepository;
        }

        private InvoiceController _InvoiceController;
        private StoreRepository _StoreRepository;

        [HttpPost]
        [Route("api/v1/invoices")]
        [MediaTypeAcceptConstraintAttribute("text/html")]
        [IgnoreAntiforgeryToken]
        [EnableCors(CorsPolicies.All)]
        public async Task<IActionResult> PayButtonHandle([FromForm]PayButtonViewModel model, CancellationToken cancellationToken)
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
            
            if (model == null || model.Price <= 0)
                ModelState.AddModelError("Price", "Price must be greater than 0");

            if (!ModelState.IsValid)
                return View();

            DataWrapper<InvoiceResponse> invoice = null;
            try
            {
                invoice = await _InvoiceController.CreateInvoiceCore(new CreateInvoiceRequest()
                {
                    Price = model.Price,
                    Currency = model.Currency,
                    ItemDesc = model.CheckoutDesc,
                    OrderId = model.OrderId,
                    NotificationEmail = model.NotifyEmail,
                    NotificationURL = model.ServerIpn,
                    RedirectURL = model.BrowserRedirect,
                    FullNotifications = true
                }, store, HttpContext.Request.GetAbsoluteRoot(), cancellationToken: cancellationToken);
            }
            catch (BitpayHttpException e)
            {
                ModelState.AddModelError("Store", e.Message);
                return View();
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
