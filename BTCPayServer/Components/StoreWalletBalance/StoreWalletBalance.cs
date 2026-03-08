#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Mvc;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Components.StoreWalletBalance;

public class StoreWalletBalance(
    CurrencyNameTable currencies,
    WalletHistogramService walletHistogramService,
    BTCPayWalletProvider walletProvider,
    BTCPayNetworkProvider networkProvider,
    PaymentMethodHandlerDictionary handlers)
    : ViewComponent
{
    private const HistogramType DefaultType = HistogramType.Week;
    public async Task<IViewComponentResult> InvokeAsync(StoreData store, string? cryptoCode = null)
    {
        cryptoCode ??= networkProvider.DefaultCryptoCode;
        var walletId = new WalletId(store.Id, cryptoCode);
        var data = await walletHistogramService.GetHistogram(store, walletId, DefaultType);
        var defaultCurrency = store.GetStoreBlob().DefaultCurrency;

        var vm = new StoreWalletBalanceViewModel
        {
            StoreId = store.Id,
            CryptoCode = cryptoCode,
            CurrencyData = currencies.GetCurrencyData(defaultCurrency, true),
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
            var wallet = walletProvider.GetWallet(cryptoCode);
            var derivation = store.GetDerivationSchemeSettings(handlers, walletId.CryptoCode);
            var handler = handlers.TryGetBitcoinHandler(walletId.CryptoCode);
            if (wallet is not null && derivation is not null && handler is not null)
            {
                var balance = await wallet.GetBalance(derivation.AccountDerivation, cts.Token);
                vm.Balance = balance.Available.GetValue(handler.Network);
            }
            else
            {
                vm.MissingWalletConfig = true;
            }
        }

        return View(vm);
    }
}
