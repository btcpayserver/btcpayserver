using BTCPayServer.Authentication;
using Microsoft.Extensions.Logging;
using BTCPayServer.Filters;
using BTCPayServer.Logging;
using BTCPayServer.Models;
using Microsoft.AspNetCore.Mvc;
using NBitpayClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Cors;
using BTCPayServer.Services.Stores;

namespace BTCPayServer.Controllers
{
    [EnableCors("BitpayAPI")]
    [BitpayAPIConstraint]
    public class InvoiceControllerAPI : Controller
    {
        private InvoiceController _InvoiceController;
        private InvoiceRepository _InvoiceRepository;
        private StoreRepository _StoreRepository;
        private BTCPayNetworkProvider _NetworkProvider;

        public InvoiceControllerAPI(InvoiceController invoiceController,
                                    InvoiceRepository invoceRepository,
                                    StoreRepository storeRepository,
                                    BTCPayNetworkProvider networkProvider)
        {
            this._InvoiceController = invoiceController;
            this._InvoiceRepository = invoceRepository;
            this._StoreRepository = storeRepository;
            this._NetworkProvider = networkProvider;
        }

        [HttpPost]
        [Route("invoices")]
        [MediaTypeConstraint("application/json")]
        public async Task<DataWrapper<InvoiceResponse>> CreateInvoice([FromBody] Invoice invoice)
        {
            var store = await _StoreRepository.FindStore(this.User.GetStoreId());
            if (store == null)
                throw new BitpayHttpException(401, "Can't access to store");
            return await _InvoiceController.CreateInvoiceCore(invoice, store, HttpContext.Request.GetAbsoluteRoot());
        }

        [HttpGet]
        [Route("invoices/{id}")]
        public async Task<DataWrapper<InvoiceResponse>> GetInvoice(string id, string token)
        {
            var store = await _StoreRepository.FindStore(this.User.GetStoreId());
            if (store == null)
                throw new BitpayHttpException(401, "Can't access to store");
            var invoice = await _InvoiceRepository.GetInvoice(store.Id, id);
            if (invoice == null)
                throw new BitpayHttpException(404, "Object not found");
            var resp = invoice.EntityToDTO(_NetworkProvider);
            return new DataWrapper<InvoiceResponse>(resp);
        }

        [HttpGet]
        [Route("invoices")]
        public async Task<DataWrapper<InvoiceResponse[]>> GetInvoices(
            string token,
            DateTimeOffset? dateStart = null,
            DateTimeOffset? dateEnd = null,
            string orderId = null,
            string itemCode = null,
            string status = null,
            int? limit = null,
            int? offset = null)
        {
            if (dateEnd != null)
                dateEnd = dateEnd.Value + TimeSpan.FromDays(1); //Should include the end day

            var store = await _StoreRepository.FindStore(this.User.GetStoreId());
            if (store == null)
                throw new BitpayHttpException(401, "Can't access to store");
            var query = new InvoiceQuery()
            {
                Count = limit,
                Skip = offset,
                EndDate = dateEnd,
                StartDate = dateStart,
                OrderId = orderId,
                ItemCode = itemCode,
                Status = status == null ? null : new[] { status },
                StoreId = new[] { store.Id }
            };


            var entities = (await _InvoiceRepository.GetInvoices(query))
                            .Select((o) => o.EntityToDTO(_NetworkProvider)).ToArray();

            return DataWrapper.Create(entities);
        }
    }
}
