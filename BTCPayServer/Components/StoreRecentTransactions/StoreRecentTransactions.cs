using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Components.StoreRecentTransactions;

public class StoreRecentTransactions : ViewComponent
{
    private const string CryptoCode = "BTC";
    
    private readonly StoreRepository _storeRepo;
    private readonly ApplicationDbContextFactory _dbContextFactory;
    private readonly BTCPayWalletProvider _walletProvider;
    private readonly BTCPayNetworkProvider _networkProvider;

    public StoreRecentTransactions(
        StoreRepository storeRepo,
        BTCPayWalletProvider walletProvider,
        BTCPayNetworkProvider networkProvider,
        ApplicationDbContextFactory dbContextFactory)
    {
        _storeRepo = storeRepo;
        _walletProvider = walletProvider;
        _networkProvider = networkProvider;
        _dbContextFactory = dbContextFactory;
    }

    public async Task<IViewComponentResult> InvokeAsync(StoreData store)
    {
        var walletId = new WalletId(store.Id, CryptoCode);
        var derivation = store.GetDerivationSchemeSettings(_networkProvider, walletId.CryptoCode);
        var transactions = new List<StoreRecentTransactionViewModel>();
        if (derivation != null)
        {
            var network = derivation.Network;
            var wallet = _walletProvider.GetWallet(network);
            var allTransactions = await wallet.FetchTransactions(derivation.AccountDerivation);
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
        
        var vm = new StoreRecentTransactionsViewModel
        {
            Store = store,
            WalletId = walletId,
            Transactions = transactions
        };

        return View(vm);
    }
}
