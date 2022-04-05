using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.StoreRecentInvoices;

public class StoreRecentInvoices : ViewComponent
{
    private readonly StoreRepository _storeRepo;
    private readonly InvoiceRepository _invoiceRepo;
    private readonly CurrencyNameTable _currencyNameTable;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContextFactory _dbContextFactory;

    public StoreRecentInvoices(
        StoreRepository storeRepo,
        InvoiceRepository invoiceRepo,
        CurrencyNameTable currencyNameTable,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContextFactory dbContextFactory)
    {
        _storeRepo = storeRepo;
        _invoiceRepo = invoiceRepo;
        _userManager = userManager;
        _currencyNameTable = currencyNameTable;
        _dbContextFactory = dbContextFactory;
    }

    public async Task<IViewComponentResult> InvokeAsync(StoreData store)
    {
        var userId = _userManager.GetUserId(UserClaimsPrincipal);
        var invoiceEntities = await _invoiceRepo.GetInvoices(new InvoiceQuery
            {
                UserId = userId,
                StoreId = new [] { store.Id },
                Take = 5
            });
        var invoices = new List<StoreRecentInvoiceViewModel>();
        foreach (var invoice in invoiceEntities)
        {
            var state = invoice.GetInvoiceState();
            invoices.Add(new StoreRecentInvoiceViewModel
            {
                Date = invoice.InvoiceTime,
                Status = state,
                InvoiceId = invoice.Id,
                OrderId = invoice.Metadata.OrderId ?? string.Empty,
                AmountCurrency = _currencyNameTable.DisplayFormatCurrency(invoice.Price, invoice.Currency),
            });
        }
        var vm = new StoreRecentInvoicesViewModel
        {
            Store = store,
            Invoices = invoices
        };

        return View(vm);
    }
}
