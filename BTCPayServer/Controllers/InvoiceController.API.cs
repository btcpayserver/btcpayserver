using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Filters;
using BTCPayServer.Models;
using BTCPayServer.Security;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    [BitpayAPIConstraint]
    [Authorize(Policies.CanCreateInvoice.Key, AuthenticationSchemes = AuthenticationSchemes.Bitpay)]
    public class InvoiceControllerAPI : Controller
    {
        private InvoiceController _InvoiceController;
        private InvoiceRepository _InvoiceRepository;

        public InvoiceControllerAPI(InvoiceController invoiceController,
                                    InvoiceRepository invoiceRepository)
        {
            _InvoiceController = invoiceController;
            _InvoiceRepository = invoiceRepository;
        }

        [HttpPost]
        [Route("invoices")]
        [MediaTypeConstraint("application/json")]
        public async Task<DataWrapper<InvoiceResponse>> CreateInvoice([FromBody] CreateInvoiceRequest invoice, CancellationToken cancellationToken)
        {
            if (invoice == null)
                throw new BitpayHttpException(400, "Invalid invoice");
            return await _InvoiceController.CreateInvoiceCore(invoice, HttpContext.GetStoreData(), HttpContext.Request.GetAbsoluteRoot(), cancellationToken: cancellationToken);
        }

        [HttpGet]
        [Route("invoices/{id}")]
        public async Task<DataWrapper<InvoiceResponse>> GetInvoice(string id)
        {
            var invoice = (await _InvoiceRepository.GetInvoices(new InvoiceQuery()
            {
                InvoiceId = new[] {id},
                StoreId = new[] { HttpContext.GetStoreData().Id }
            })).FirstOrDefault();
            if (invoice == null)
                throw new BitpayHttpException(404, "Object not found");
            return new DataWrapper<InvoiceResponse>(invoice.EntityToDTO());
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
                OrderId =  orderId == null ? null : new[] { orderId },
                ItemCode =  itemCode == null ? null : new[] { itemCode },
                Status = status == null ? null : new[] { status },
                StoreId = new[] { this.HttpContext.GetStoreData().Id }
            };

            var entities = (await _InvoiceRepository.GetInvoices(query))
                            .Select((o) => o.EntityToDTO()).ToArray();

            return DataWrapper.Create(entities);
        }
    }
}
