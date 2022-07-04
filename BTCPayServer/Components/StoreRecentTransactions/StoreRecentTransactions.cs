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
    private string CryptoCode;
    private readonly StoreRepository _storeRepo;
    private readonly ApplicationDbContextFactory _dbContextFactory;
    private readonly BTCPayWalletProvider _walletProvider;

    public BTCPayNetworkProvider NetworkProvider { get; }

    public StoreRecentTransactions(
        StoreRepository storeRepo,
        BTCPayNetworkProvider networkProvider,
        NBXplorerConnectionFactory connectionFactory,
        BTCPayWalletProvider walletProvider,
        ApplicationDbContextFactory dbContextFactory)
    {
        _storeRepo = storeRepo;
        NetworkProvider = networkProvider;
        _walletProvider = walletProvider;
        _dbContextFactory = dbContextFactory;
        CryptoCode = networkProvider.DefaultNetwork.CryptoCode;
    }


    public async Task<IViewComponentResult> InvokeAsync(StoreData store)
    {
        var walletId = new WalletId(store.Id, CryptoCode);
        var derivationSettings = store.GetDerivationSchemeSettings(NetworkProvider, walletId.CryptoCode);
        var transactions = new List<StoreRecentTransactionViewModel>();
        if (derivationSettings?.AccountDerivation is not null)
        {
            var network = derivationSettings.Network;
            var wallet = _walletProvider.GetWallet(network);
            var allTransactions = await wallet.FetchTransactionHistory(derivationSettings.AccountDerivation, 0, 5, TimeSpan.FromDays(31.0));
            transactions = allTransactions
                .Select(tx => new StoreRecentTransactionViewModel
                {
                    Id = tx.TransactionId.ToString(),
                    Positive = tx.BalanceChange.GetValue(network) >= 0,
                    Balance = tx.BalanceChange.ShowMoney(network),
                    IsConfirmed = tx.Confirmations != 0,
                    Link = string.Format(CultureInfo.InvariantCulture, network.BlockExplorerLink, tx.TransactionId.ToString()),
                    Timestamp = tx.SeenAt
                })
                .ToList();
        }


        var vm = new StoreRecentTransactionsViewModel
        {
            Store = store,
            WalletId = walletId,
            Transactions = transactions
        };
        return View(vm);
    }
}
