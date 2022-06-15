using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NBXplorer;
using NBXplorer.Client;

namespace BTCPayServer.Components.StoreWalletBalance;

public class StoreWalletBalance : ViewComponent
{
    private string _cryptoCode;
    private const WalletHistogramType DefaultType = WalletHistogramType.Week;

    private readonly StoreRepository _storeRepo;
    private readonly CurrencyNameTable _currencies;
    private readonly WalletHistogramService _walletHistogramService;

    public StoreWalletBalance(
        StoreRepository storeRepo,
        CurrencyNameTable currencies,
        WalletHistogramService walletHistogramService, 
        BTCPayNetworkProvider networkProvider)
    {
        _storeRepo = storeRepo;
        _currencies = currencies;
        _walletHistogramService = walletHistogramService;
        _cryptoCode = networkProvider.DefaultNetwork.CryptoCode;
    }

    public async Task<IViewComponentResult> InvokeAsync(StoreData store)
    {
        var walletId = new WalletId(store.Id, _cryptoCode);
        var data = await _walletHistogramService.GetHistogram(store, walletId, DefaultType);
        var defaultCurrency = store.GetStoreBlob().DefaultCurrency;
        
        var vm = new StoreWalletBalanceViewModel
        {
            Store = store,
            CryptoCode = _cryptoCode,
            CurrencyData = _currencies.GetCurrencyData(defaultCurrency, true),
            DefaultCurrency = defaultCurrency,
            WalletId = walletId,
            Series = data?.Series,
            Labels = data?.Labels,
            Balance = data?.Balance,
            Type = DefaultType
        };

        return View(vm);
    }
}
