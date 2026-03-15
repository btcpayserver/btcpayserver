using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models.InvoicingModels;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.StoreRecentInvoices;

public class StoreRecentInvoices(
    InvoiceRepository invoiceRepo) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(StoreData store, bool initialRendering)
    {
        var vm = new StoreRecentInvoicesViewModel
        {
            StoreId = store.Id,
            InitialRendering = initialRendering
        };

        if (vm.InitialRendering)
            return View(vm);

        var userId = UserClaimsPrincipal.GetIdOrNull();
        var invoiceEntities = await invoiceRepo.GetInvoices(new InvoiceQuery
        {
            UserId = userId,
            StoreId = [store.Id],
            IncludeArchived = false,
            IncludeRefunds = true,
            Take = 5
        });

        vm.Invoices = (from invoice in invoiceEntities
            let state = invoice.GetInvoiceState()
            select new StoreRecentInvoiceViewModel
            {
                Date = invoice.InvoiceTime,
                Status = state,
                HasRefund = invoice.Refunds.Any(),
                InvoiceId = invoice.Id,
                OrderId = invoice.Metadata.OrderId ?? string.Empty,
                Amount = invoice.Price,
                Currency = invoice.Currency,
                Details = new InvoiceDetailsModel
                {
                    Archived = invoice.Archived,
                    Payments = invoice.GetPayments(false)
                }
           }).ToList();

        return View(vm);
    }
}
