using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        [Route("/pay/{storeId}")]
        [IgnoreAntiforgeryToken]
        [EnableCors(CorsPolicies.All)]
        public async Task<IActionResult> PayButtonHandle(string storeId, [FromForm]PayButtonViewModel model)
        {
            var store = await _StoreRepository.FindStore(storeId);
            if (store == null)
                ModelState.AddModelError("Store", "Invalid store");

            // TODO: extract validation to model
            if (model.Price <= 0)
                ModelState.AddModelError("Price", "Price must be greater than 0");

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
        [Route("/paybuttontest")]
        public IActionResult PayButtonTest()
        {
            return View();
        }
    }
}
