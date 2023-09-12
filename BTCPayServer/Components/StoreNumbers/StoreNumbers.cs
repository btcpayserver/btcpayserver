using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Components.StoreRecentTransactions;
using BTCPayServer.Data;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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

    public async Task<IViewComponentResult> InvokeAsync(StoreNumbersViewModel vm)
    {
        if (vm.Store == null)
            throw new ArgumentNullException(nameof(vm.Store));
        if (vm.CryptoCode == null)
            throw new ArgumentNullException(nameof(vm.CryptoCode));

        vm.WalletId = new WalletId(vm.Store.Id, vm.CryptoCode);

        if (vm.InitialRendering)
            return View(vm);

        await using var ctx = _dbContextFactory.CreateContext();
        var offset = DateTimeOffset.Now.AddDays(-vm.TimeframeDays).ToUniversalTime();
        
        vm.PaidInvoices = await _invoiceRepository.GetInvoiceCount(
            new InvoiceQuery { StoreId = new [] { vm.Store.Id }, StartDate = offset, Status = new [] { "paid", "confirmed" } });
        vm.PayoutsPending = await ctx.Payouts
            .Where(p => p.PullPaymentData.StoreId == vm.Store.Id && !p.PullPaymentData.Archived && p.State == PayoutState.AwaitingApproval)
            .CountAsync();
        vm.RefundsIssued = await ctx.Invoices
            .Where(i => i.StoreData.Id == vm.Store.Id && !i.Archived && i.CurrentRefundId != null && i.Created >= offset)
            .CountAsync();

        return View(vm);
    }
}
