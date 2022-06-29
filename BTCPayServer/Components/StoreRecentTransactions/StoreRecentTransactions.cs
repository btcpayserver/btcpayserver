using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Data;
using BTCPayServer.Services;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Services.Stores;
using Dapper;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NBXplorer.Client;
using static BTCPayServer.Components.StoreRecentTransactions.StoreRecentTransactionsViewModel;
using Microsoft.EntityFrameworkCore;
using NBitcoin;

namespace BTCPayServer.Components.StoreRecentTransactions;

public class StoreRecentTransactions : ViewComponent
{
    private readonly StoreRepository _storeRepo;
    private readonly ApplicationDbContextFactory _dbContextFactory;
    private readonly BTCPayWalletProvider _walletProvider;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly NBXplorerConnectionFactory _connectionFactory;

    public StoreRecentTransactions(
        StoreRepository storeRepo,
        BTCPayNetworkProvider networkProvider,
        NBXplorerConnectionFactory connectionFactory,
        BTCPayWalletProvider walletProvider,
        ApplicationDbContextFactory dbContextFactory)
    {
        _storeRepo = storeRepo;
        _networkProvider = networkProvider;
        _connectionFactory = connectionFactory;
        _walletProvider = walletProvider;
        _dbContextFactory = dbContextFactory;
    }

    public async Task<IViewComponentResult> InvokeAsync(StoreRecentTransactionsViewModel vm)
    {
        if (vm.Store == null) throw new ArgumentNullException(nameof(vm.Store));
        if (vm.CryptoCode == null) throw new ArgumentNullException(nameof(vm.CryptoCode));

        vm.WalletId = new WalletId(vm.Store.Id, vm.CryptoCode);
        
        if (vm.InitialRendering) return View(vm);
        
        var derivationSettings = vm.Store.GetDerivationSchemeSettings(_networkProvider, vm.CryptoCode);
        var transactions = new List<StoreRecentTransactionViewModel>();
        if (derivationSettings?.AccountDerivation is not null)
        {
            if (_connectionFactory.Available)
            {
                var wallet_id = derivationSettings.GetNBXWalletId();
                await using var conn = await _connectionFactory.OpenConnection();
                var rows = await conn.QueryAsync(
                    "SELECT t.tx_id, t.seen_at, to_btc(balance_change::NUMERIC) balance_change, (t.blk_id IS NOT NULL) confirmed " +
                    "FROM get_wallets_recent(@wallet_id, @code, @interval, 5, 0) " +
                    "JOIN txs t USING (code, tx_id) " +
                    "ORDER BY seen_at DESC;",
                    new
                    {
                        wallet_id,
                        code = vm.CryptoCode,
                        interval = TimeSpan.FromDays(31)
                    });
                var network = derivationSettings.Network;
                transactions.AddRange(from r in rows
                let seenAt = new DateTimeOffset((DateTime)r.seen_at)
                let balanceChange = new Money((decimal)r.balance_change, MoneyUnit.BTC)
                select new StoreRecentTransactionViewModel
                {
                    Timestamp = seenAt,
                    Id = r.tx_id,
                    Balance = balanceChange.ShowMoney(network),
                    IsConfirmed = r.confirmed,
                    Link = string.Format(CultureInfo.InvariantCulture, network.BlockExplorerLink, r.tx_id),
                    Positive = balanceChange.GetValue(network) >= 0,
                });
            }
            else
            {
                var network = derivationSettings.Network;
                var wallet = _walletProvider.GetWallet(network);
                var allTransactions = await wallet.FetchTransactions(derivationSettings.AccountDerivation);
                transactions = allTransactions.UnconfirmedTransactions.Transactions
                    .Concat(allTransactions.ConfirmedTransactions.Transactions).ToArray()
                    .OrderByDescending(t => t.Timestamp)
                    .Take(5)
                    .Select(tx => new StoreRecentTransactionViewModel
                    {
                        Id = tx.TransactionId.ToString(),
                        Positive = tx.BalanceChange.GetValue(network) >= 0,
                        Balance = tx.BalanceChange.ShowMoney(network),
                        IsConfirmed = tx.Confirmations != 0,
                        Link = string.Format(CultureInfo.InvariantCulture, network.BlockExplorerLink, tx.TransactionId.ToString()),
                        Timestamp = tx.Timestamp
                    })
                    .ToList();
            }
        }

        vm.Transactions = transactions;
        
        return View(vm);
    }
}
