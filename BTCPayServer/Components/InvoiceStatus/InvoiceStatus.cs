using System.Collections.Generic;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.InvoiceStatus
{
    public class InvoiceStatus : ViewComponent
    {
        public IViewComponentResult Invoke(InvoiceState state, List<PaymentEntity> payments, string invoiceId, bool isArchived = false, bool hasRefund = false)
        {
            var vm = new InvoiceStatusViewModel
            {
                State = state,
                Payments = payments,
                InvoiceId = invoiceId,
                IsArchived = isArchived,
                HasRefund = hasRefund
            };
            return View(vm);
        }
    }
}
