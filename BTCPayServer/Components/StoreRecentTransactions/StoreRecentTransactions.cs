#nullable enable
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
    private readonly BTCPayWalletProvider _walletProvider;
    public BTCPayNetworkProvider NetworkProvider { get; }

    public StoreRecentTransactions(
        BTCPayNetworkProvider networkProvider,
        BTCPayWalletProvider walletProvider)
    {
        NetworkProvider = networkProvider;
        _walletProvider = walletProvider;
    }

    public async Task<IViewComponentResult> InvokeAsync(StoreRecentTransactionsViewModel vm)
    {
        if (vm.Store == null) throw new ArgumentNullException(nameof(vm.Store));
        if (vm.CryptoCode == null) throw new ArgumentNullException(nameof(vm.CryptoCode));

        vm.WalletId = new WalletId(vm.Store.Id, vm.CryptoCode);
        
        if (vm.InitialRendering) return View(vm);
        
        var derivationSettings = vm.Store.GetDerivationSchemeSettings(NetworkProvider, vm.CryptoCode);
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

        vm.Transactions = transactions;
        
        return View(vm);
    }
}
