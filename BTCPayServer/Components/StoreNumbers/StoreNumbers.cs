using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Components.StoreRecentTransactions;
using BTCPayServer.Data;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Components.StoreNumbers;

public class StoreNumbers : ViewComponent
{
    private const string CryptoCode = "BTC";
    private const int TransactionDays = 7;
    
    private readonly StoreRepository _storeRepo;
    private readonly ApplicationDbContextFactory _dbContextFactory;
    private readonly BTCPayWalletProvider _walletProvider;
    private readonly BTCPayNetworkProvider _networkProvider;

    public StoreNumbers(
        StoreRepository storeRepo,
        ApplicationDbContextFactory dbContextFactory,
        BTCPayNetworkProvider networkProvider,
        BTCPayWalletProvider walletProvider)
    {
        _storeRepo = storeRepo;
        _walletProvider = walletProvider;
        _networkProvider = networkProvider;
        _dbContextFactory = dbContextFactory;
    }

    public async Task<IViewComponentResult> InvokeAsync(StoreData store)
    {
        
        await using var ctx = _dbContextFactory.CreateContext();
        var payoutsCount = await ctx.Payouts
            .Where(p => p.PullPaymentData.StoreId == store.Id && !p.PullPaymentData.Archived && p.State == PayoutState.AwaitingApproval)
            .CountAsync();
        var refundsCount = await ctx.Invoices
            .Where(i => i.StoreData.Id == store.Id && !i.Archived && i.CurrentRefundId != null)
            .CountAsync();
        
        var walletId = new WalletId(store.Id, CryptoCode);
        var derivation = store.GetDerivationSchemeSettings(_networkProvider, walletId.CryptoCode);
        var transactionsCount = 0;
        if (derivation != null)
        {
            var network = derivation.Network;
            var wallet = _walletProvider.GetWallet(network);
            var allTransactions = await wallet.FetchTransactions(derivation.AccountDerivation);
            var afterDate = DateTimeOffset.UtcNow - TimeSpan.FromDays(TransactionDays);
            transactionsCount = allTransactions.UnconfirmedTransactions.Transactions
                .Concat(allTransactions.ConfirmedTransactions.Transactions)
                .Count(t => t.Timestamp > afterDate);
        }
        
        var vm = new StoreNumbersViewModel
        {
            Store = store,
            WalletId = walletId,
            PayoutsPending = payoutsCount,
            Transactions = transactionsCount,
            TransactionDays = TransactionDays,
            RefundsIssued = refundsCount
        };

        return View(vm);
    }
}
