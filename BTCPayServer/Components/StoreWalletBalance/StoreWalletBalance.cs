#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBXplorer;
using NBXplorer.Client;

namespace BTCPayServer.Components.StoreWalletBalance;

public class StoreWalletBalance : ViewComponent
{
    private const WalletHistogramType DefaultType = WalletHistogramType.Week;

    private readonly StoreRepository _storeRepo;
    private readonly CurrencyNameTable _currencies;
    private readonly WalletHistogramService _walletHistogramService;
    private readonly BTCPayWalletProvider _walletProvider;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly PaymentMethodHandlerDictionary _handlers;

    public StoreWalletBalance(
        StoreRepository storeRepo,
        CurrencyNameTable currencies,
        WalletHistogramService walletHistogramService,
        BTCPayWalletProvider walletProvider,
        BTCPayNetworkProvider networkProvider,
        PaymentMethodHandlerDictionary handlers)
    {
        _storeRepo = storeRepo;
        _currencies = currencies;
        _walletProvider = walletProvider;
        _networkProvider = networkProvider;
        _walletHistogramService = walletHistogramService;
        _handlers = handlers;
    }

    public async Task<IViewComponentResult> InvokeAsync(StoreData store)
    {
        var cryptoCode = _networkProvider.DefaultNetwork.CryptoCode;
        var walletId = new WalletId(store.Id, cryptoCode);
        var data = await _walletHistogramService.GetHistogram(store, walletId, DefaultType);
        var defaultCurrency = store.GetStoreBlob().DefaultCurrency;

        var vm = new StoreWalletBalanceViewModel
        {
            Store = store,
            CryptoCode = cryptoCode,
            CurrencyData = _currencies.GetCurrencyData(defaultCurrency, true),
            DefaultCurrency = defaultCurrency,
            WalletId = walletId,
            Type = DefaultType
        };

        if (data != null)
        {
            vm.Balance = data.Balance;
            vm.Series = data.Series;
            vm.Labels = data.Labels;
        }
        else
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(3));
            var wallet = _walletProvider.GetWallet(_networkProvider.DefaultNetwork);
            var derivation = store.GetDerivationSchemeSettings(_handlers, walletId.CryptoCode);
            var network = _handlers.GetBitcoinHandler(walletId.CryptoCode).Network;
            if (derivation is not null)
            {
                var balance = await wallet.GetBalance(derivation.AccountDerivation, cts.Token);
                vm.Balance = balance.Available.GetValue(network);
            }
        }

        return View(vm);
    }
}
