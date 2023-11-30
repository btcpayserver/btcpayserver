#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Data;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using BTCPayServer.Services.Labels;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBXplorer.Client;
using static BTCPayServer.Components.StoreRecentTransactions.StoreRecentTransactionsViewModel;

namespace BTCPayServer.Components.StoreRecentTransactions;

public class StoreRecentTransactions : ViewComponent
{
    private readonly BTCPayWalletProvider _walletProvider;
    private readonly WalletRepository _walletRepository;
    private readonly LabelService _labelService;
    private readonly TransactionLinkProviders _transactionLinkProviders;

    public BTCPayNetworkProvider NetworkProvider { get; }

    public StoreRecentTransactions(
        BTCPayNetworkProvider networkProvider,
        BTCPayWalletProvider walletProvider,
        WalletRepository walletRepository,
        LabelService labelService,
        TransactionLinkProviders transactionLinkProviders)
    {
        NetworkProvider = networkProvider;
        _walletProvider = walletProvider;
        _walletRepository = walletRepository;
        _labelService = labelService;
        _transactionLinkProviders = transactionLinkProviders;
    }

    public async Task<IViewComponentResult> InvokeAsync(StoreRecentTransactionsViewModel vm)
    {
        if (vm.Store == null)
            throw new ArgumentNullException(nameof(vm.Store));
        if (vm.CryptoCode == null)
            throw new ArgumentNullException(nameof(vm.CryptoCode));

        vm.WalletId = new WalletId(vm.Store.Id, vm.CryptoCode);

        if (vm.InitialRendering)
            return View(vm);

        var derivationSettings = vm.Store.GetDerivationSchemeSettings(NetworkProvider, vm.CryptoCode);
        var transactions = new List<StoreRecentTransactionViewModel>();
        if (derivationSettings?.AccountDerivation is not null)
        {
            var network = derivationSettings.Network;
            var wallet = _walletProvider.GetWallet(network);
            var allTransactions = await wallet.FetchTransactionHistory(derivationSettings.AccountDerivation, 0, 5, TimeSpan.FromDays(31.0), cancellationToken: this.HttpContext.RequestAborted);
            var walletTransactionsInfo = await _walletRepository.GetWalletTransactionsInfo(vm.WalletId, allTransactions.Select(t => t.TransactionId.ToString()).ToArray());
            var pmi = new PaymentMethodId(vm.CryptoCode, PaymentTypes.BTCLike);
            transactions = allTransactions
                .Select(tx =>
                {
                    walletTransactionsInfo.TryGetValue(tx.TransactionId.ToString(), out var transactionInfo);
                    var labels = _labelService.CreateTransactionTagModels(transactionInfo, Request);
                    return new StoreRecentTransactionViewModel
                    {
                        Id = tx.TransactionId.ToString(),
                        Positive = tx.BalanceChange.GetValue(network) >= 0,
                        Balance = tx.BalanceChange.ShowMoney(network),
                        Currency = vm.CryptoCode,
                        IsConfirmed = tx.Confirmations != 0,
                        Link = _transactionLinkProviders.GetTransactionLink(pmi, tx.TransactionId.ToString()),
                        Timestamp = tx.SeenAt,
                        Labels = labels
                    };
                })
                .ToList();
        }

        vm.Transactions = transactions;

        return View(vm);
    }
}
