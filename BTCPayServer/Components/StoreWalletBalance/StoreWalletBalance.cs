using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services;
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
    private const string CryptoCode = "BTC";
    private const WalletHistogramType DefaultType = WalletHistogramType.Week;

    private readonly StoreRepository _storeRepo;
    private readonly WalletHistogramService _walletHistogramService;

    public StoreWalletBalance(StoreRepository storeRepo, WalletHistogramService walletHistogramService)
    {
        _storeRepo = storeRepo;
        _walletHistogramService = walletHistogramService;
    }

    public async Task<IViewComponentResult> InvokeAsync(StoreData store)
    {
        var walletId = new WalletId(store.Id, CryptoCode);
        var data = await _walletHistogramService.GetHistogram(store, walletId, DefaultType);
        
        var vm = new StoreWalletBalanceViewModel
        {
            Store = store,
            CryptoCode = CryptoCode,
            WalletId = walletId,
            Series = data?.Series,
            Labels = data?.Labels,
            Balance = data?.Balance ?? 0,
            Type = DefaultType
        };

        return View(vm);
    }
}
