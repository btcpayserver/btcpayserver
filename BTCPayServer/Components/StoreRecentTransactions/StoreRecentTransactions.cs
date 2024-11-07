#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Labels;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Components.StoreRecentTransactions;

public class StoreRecentTransactions : ViewComponent
{
    private readonly BTCPayWalletProvider _walletProvider;
    private readonly WalletRepository _walletRepository;
    private readonly LabelService _labelService;
    private readonly PaymentMethodHandlerDictionary _handlers;
    private readonly TransactionLinkProviders _transactionLinkProviders;

    public StoreRecentTransactions(
        BTCPayWalletProvider walletProvider,
        WalletRepository walletRepository,
        LabelService labelService,
        PaymentMethodHandlerDictionary handlers,
        TransactionLinkProviders transactionLinkProviders)
    {
        _walletProvider = walletProvider;
        _walletRepository = walletRepository;
        _labelService = labelService;
        _handlers = handlers;
        _transactionLinkProviders = transactionLinkProviders;
    }

    public async Task<IViewComponentResult> InvokeAsync(StoreData store, string cryptoCode, bool initialRendering)
    {
        var vm = new StoreRecentTransactionsViewModel
        {
            StoreId = store.Id,
            CryptoCode = cryptoCode,
            InitialRendering = initialRendering,
            WalletId = new WalletId(store.Id, cryptoCode)
        };

        if (vm.InitialRendering)
            return View(vm);

        var derivationSettings = store.GetDerivationSchemeSettings(_handlers, vm.CryptoCode);
        var transactions = new List<StoreRecentTransactionViewModel>();
        if (derivationSettings?.AccountDerivation is not null)
        {
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(vm.CryptoCode);
            var network = ((IHasNetwork)_handlers[pmi]).Network;
            var wallet = _walletProvider.GetWallet(network);
            var allTransactions = await wallet.FetchTransactionHistory(derivationSettings.AccountDerivation, 0, 5, TimeSpan.FromDays(31.0), cancellationToken: HttpContext.RequestAborted);
            var walletTransactionsInfo = await _walletRepository.GetWalletTransactionsInfo(vm.WalletId, allTransactions.Select(t => t.TransactionId.ToString()).ToArray());
            
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
