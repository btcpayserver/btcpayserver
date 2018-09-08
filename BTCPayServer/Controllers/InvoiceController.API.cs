using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Filters;
using BTCPayServer.Models;
using BTCPayServer.Security;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using NBitpayClient;

namespace BTCPayServer.Controllers
{
    [EnableCors("BitpayAPI")]
    [BitpayAPIConstraint]
    [Authorize(Policies.CanCreateInvoice.Key, AuthenticationSchemes = Policies.BitpayAuthentication)]
    public class InvoiceControllerAPI : Controller
    {
        private InvoiceController _InvoiceController;
        private InvoiceRepository _InvoiceRepository;
        private BTCPayNetworkProvider _NetworkProvider;

        public InvoiceControllerAPI(InvoiceController invoiceController,
                                    InvoiceRepository invoceRepository,
                                    BTCPayNetworkProvider networkProvider)
        {
            this._InvoiceController = invoiceController;
            this._InvoiceRepository = invoceRepository;
            this._NetworkProvider = networkProvider;
        }

        [HttpPost]
        [Route("invoices")]
        [MediaTypeConstraint("application/json")]
        public async Task<DataWrapper<InvoiceResponse>> CreateInvoice([FromBody] Invoice invoice)
        {
            return await _InvoiceController.CreateInvoiceCore(invoice, HttpContext.GetStoreData(), HttpContext.Request.GetAbsoluteRoot());
        }

        [HttpGet]
        [Route("invoices/{id}")]
        [AllowAnonymous]
        public async Task<DataWrapper<InvoiceResponse>> GetInvoice(string id, string token)
        {
            var invoice = await _InvoiceRepository.GetInvoice(null, id);
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
            
            var query = new InvoiceQuery()
            {
                Count = limit,
                Skip = offset,
                EndDate = dateEnd,
                StartDate = dateStart,
                OrderId = orderId,
                ItemCode = itemCode,
                Status = status == null ? null : new[] { status },
                StoreId = new[] { this.HttpContext.GetStoreData().Id }
            };

            var entities = (await _InvoiceRepository.GetInvoices(query))
                            .Select((o) => o.EntityToDTO(_NetworkProvider)).ToArray();

            return DataWrapper.Create(entities);
        }
    }
}
