using System;
using Dapper;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Components.StoreRecentTransactions;
using BTCPayServer.Data;
using BTCPayServer.Services;
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
    private string CryptoCode;
    private const int TransactionDays = 7;
    
    private readonly StoreRepository _storeRepo;
    private readonly ApplicationDbContextFactory _dbContextFactory;
    private readonly BTCPayWalletProvider _walletProvider;
    private readonly NBXplorerConnectionFactory _nbxConnectionFactory;
    private readonly BTCPayNetworkProvider _networkProvider;

    public StoreNumbers(
        StoreRepository storeRepo,
        ApplicationDbContextFactory dbContextFactory,
        BTCPayNetworkProvider networkProvider,
        BTCPayWalletProvider walletProvider,
        NBXplorerConnectionFactory nbxConnectionFactory)
    {
        _storeRepo = storeRepo;
        _walletProvider = walletProvider;
        _nbxConnectionFactory = nbxConnectionFactory;
        _networkProvider = networkProvider;
        _dbContextFactory = dbContextFactory;
        CryptoCode = networkProvider.DefaultNetwork.CryptoCode;
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
        int? transactionsCount = null;
        if (derivation != null && _nbxConnectionFactory.Available)
        {
            await using var conn = await _nbxConnectionFactory.OpenConnection();
            var wid = NBXplorer.Client.DBUtils.nbxv1_get_wallet_id(derivation.Network.CryptoCode, derivation.AccountDerivation.ToString());
            var afterDate = DateTimeOffset.UtcNow - TimeSpan.FromDays(TransactionDays);
            var count = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM wallets_history WHERE code=@code AND wallet_id=@wid AND seen_at > @afterDate", new { code = derivation.Network.CryptoCode, wid, afterDate });
            transactionsCount = (int)count;
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
