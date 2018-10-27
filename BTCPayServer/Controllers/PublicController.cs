using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Filters;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

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
        public async Task<IActionResult> PayButtonHandle([FromForm]PayButtonViewModel model)
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

            var invoice = await _InvoiceController.CreateInvoiceCore(new NBitpayClient.Invoice()
            {
                Price = model.Price,
                Currency = model.Currency,
                ItemDesc = model.CheckoutDesc,
                OrderId = model.OrderId,
                NotificationEmail = model.NotifyEmail,
                NotificationURL = model.ServerIpn,
                RedirectURL = model.BrowserRedirect,
                FullNotifications = true
            }, store, HttpContext.Request.GetAbsoluteRoot());
            return Redirect(invoice.Data.Url);
        }
    }
}
