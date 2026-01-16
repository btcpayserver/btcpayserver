using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Security;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Components.StoreLightningBalance;

public class StoreLightningBalance : ViewComponent
{
    private const HistogramType DefaultType = HistogramType.Week;

    private readonly StoreRepository _storeRepo;
    private readonly CurrencyNameTable _currencies;
    private readonly BTCPayServerOptions _btcpayServerOptions;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly LightningClientFactoryService _lightningClientFactory;
    private readonly IOptions<LightningNetworkOptions> _lightningNetworkOptions;
    private readonly IOptions<ExternalServicesOptions> _externalServiceOptions;
    private readonly IAuthorizationService _authorizationService;
    private readonly PaymentMethodHandlerDictionary _handlers;
    private readonly LightningHistogramService _lnHistogramService;

    public StoreLightningBalance(
        StoreRepository storeRepo,
        CurrencyNameTable currencies,
        BTCPayNetworkProvider networkProvider,
        BTCPayServerOptions btcpayServerOptions,
        LightningClientFactoryService lightningClientFactory,
        IOptions<LightningNetworkOptions> lightningNetworkOptions,
        IOptions<ExternalServicesOptions> externalServiceOptions,
        IAuthorizationService authorizationService,
        PaymentMethodHandlerDictionary handlers,
        LightningHistogramService lnHistogramService)
    {
        _storeRepo = storeRepo;
        _currencies = currencies;
        _networkProvider = networkProvider;
        _btcpayServerOptions = btcpayServerOptions;
        _externalServiceOptions = externalServiceOptions;
        _authorizationService = authorizationService;
        _handlers = handlers;
        _lightningClientFactory = lightningClientFactory;
        _lightningNetworkOptions = lightningNetworkOptions;
        _lnHistogramService = lnHistogramService;
    }

    public async Task<IViewComponentResult> InvokeAsync(StoreData store, string cryptoCode, bool initialRendering)
    {
        var defaultCurrency = store.GetStoreBlob().DefaultCurrency;
        var vm = new StoreLightningBalanceViewModel
        {
            StoreId = store.Id,
            CryptoCode = cryptoCode,
            InitialRendering = initialRendering,
            DefaultCurrency = defaultCurrency,
            CurrencyData = _currencies.GetCurrencyData(defaultCurrency, true),
            DataUrl = Url.Action("LightningBalanceDashboard", "UIStores", new { storeId = store.Id, cryptoCode })
        };

        if (vm.InitialRendering)
            return View(vm);
        
        try
        {
            var lightningClient = await GetLightningClient(store, vm.CryptoCode);
            
            // balance
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var balance = await lightningClient.GetBalance(cts.Token);
            vm.Balance = balance;
            vm.TotalOnchain = balance.OnchainBalance != null
                ? (balance.OnchainBalance.Confirmed ?? 0L) + (balance.OnchainBalance.Reserved ?? 0L) +
                  (balance.OnchainBalance.Unconfirmed ?? 0L)
                : null;
            vm.TotalOffchain = balance.OffchainBalance != null
                ? (balance.OffchainBalance.Opening ?? 0) + (balance.OffchainBalance.Local ?? 0) +
                  (balance.OffchainBalance.Closing ?? 0)
                : null;
            
            // histogram
            var data = await _lnHistogramService.GetHistogram(lightningClient, DefaultType, cts.Token);
            if (data != null)
            {
                vm.Type = data.Type;
                vm.Series = data.Series;
                vm.Labels = data.Labels;
            }
        }
        catch (Exception ex) when (ex is NotImplementedException or NotSupportedException)
        {
            // not all implementations support balance fetching
            vm.ProblemDescription = "Your node does not support balance fetching.";
        }
        catch
        {
            // general error
            vm.ProblemDescription = "Could not fetch Lightning balance.";
        }
        return View(vm);
    }

    private async Task<ILightningClient> GetLightningClient(StoreData store, string cryptoCode)
    {
        var network = _networkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
        var id = PaymentTypes.LN.GetPaymentMethodId(cryptoCode);
        var existing = store.GetPaymentMethodConfig<LightningPaymentMethodConfig>(id, _handlers);
        if (existing == null)
            return null;

        if (existing.GetExternalLightningUrl() is { } connectionString)
        {
            return _lightningClientFactory.Create(connectionString, network);
        }
        if (existing.IsInternalNode && _lightningNetworkOptions.Value.InternalLightningByCryptoCode.TryGetValue(cryptoCode, out var internalLightningNode))
        {
            var result = await _authorizationService.AuthorizeAsync(HttpContext.User, null,
                new PolicyRequirement(Policies.CanUseInternalLightningNode));
            return result.Succeeded ? internalLightningNode : null;
        }

        return null;
    }
}
