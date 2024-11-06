#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Mvc;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Components.StoreWalletBalance;

public class StoreWalletBalance : ViewComponent
{
    private const HistogramType DefaultType = HistogramType.Week;

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
            StoreId = store.Id,
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
            else
            {
                vm.MissingWalletConfig = true;
            }
        }

        return View(vm);
    }
}
