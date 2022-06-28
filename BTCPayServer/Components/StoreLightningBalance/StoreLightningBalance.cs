using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Components.StoreLightningBalance;

public class StoreLightningBalance : ViewComponent
{
    private string _cryptoCode;
    private readonly StoreRepository _storeRepo;
    private readonly CurrencyNameTable _currencies;
    private readonly BTCPayServerOptions _btcpayServerOptions;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly IOptions<ExternalServicesOptions> _externalServiceOptions;

    public StoreLightningBalance(
        StoreRepository storeRepo,
        CurrencyNameTable currencies,
        BTCPayNetworkProvider networkProvider,
        BTCPayServerOptions btcpayServerOptions,
        LightningClientFactoryService lightningClientFactory,
        IOptions<LightningNetworkOptions> lightningNetworkOptions,
        IOptions<ExternalServicesOptions> externalServiceOptions)
    {
        _storeRepo = storeRepo;
        _currencies = currencies;
        _networkProvider = networkProvider;
        _btcpayServerOptions = btcpayServerOptions;
        _externalServiceOptions = externalServiceOptions;
        _cryptoCode = _networkProvider.DefaultNetwork.CryptoCode;
    }

    public IViewComponentResult Invoke(StoreData store)
    {
        var defaultCurrency = store.GetStoreBlob().DefaultCurrency;

        var vm = new StoreLightningBalanceViewModel
        {
            Store = store,
            CryptoCode = _cryptoCode,
            CurrencyData = _currencies.GetCurrencyData(defaultCurrency, true),
            DefaultCurrency = defaultCurrency,
        };

        return View(vm);
    }
}
