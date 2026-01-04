using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Components.StoreNumbers;

public class StoreNumbers : ViewComponent
{
    private readonly StoreRepository _storeRepo;
    private readonly ApplicationDbContextFactory _dbContextFactory;
    private readonly InvoiceRepository _invoiceRepository;

    public StoreNumbers(
        StoreRepository storeRepo,
        ApplicationDbContextFactory dbContextFactory,
        InvoiceRepository invoiceRepository)
    {
        _storeRepo = storeRepo;
        _dbContextFactory = dbContextFactory;
        _invoiceRepository = invoiceRepository;
    }

    public async Task<IViewComponentResult> InvokeAsync(StoreData store, string cryptoCode, bool initialRendering)
    {
        var vm = new StoreNumbersViewModel
        {
            StoreId = store.Id,
            CryptoCode = cryptoCode,
            InitialRendering = initialRendering,
            WalletId = new WalletId(store.Id, cryptoCode)
        };

        if (vm.InitialRendering)
            return View(vm);

        await using var ctx = _dbContextFactory.CreateContext();
        var offset = DateTimeOffset.Now.AddDays(-vm.TimeframeDays).ToUniversalTime();
        
        vm.PaidInvoices = await _invoiceRepository.GetInvoiceCount(
            new InvoiceQuery { StoreId = [store.Id], StartDate = offset, Status = ["paid", "confirmed"] });
        vm.PayoutsPending = await ctx.Payouts
            .Where(p => p.PullPaymentData.StoreId == store.Id && !p.PullPaymentData.Archived && p.State == PayoutState.AwaitingApproval)
            .CountAsync();
        vm.RefundsIssued = await ctx.Invoices
            .Where(i => i.StoreData.Id == store.Id && !i.Archived && i.Created >= offset)
            .SelectMany(i => i.Refunds)
            .CountAsync();

        return View(vm);
    }
}
