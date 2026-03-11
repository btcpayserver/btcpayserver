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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Components.StoreLightningBalance;

public class StoreLightningBalance(
    CurrencyNameTable currencies,
    BTCPayNetworkProvider networkProvider,
    LightningClientFactoryService lightningClientFactory,
    IOptions<LightningNetworkOptions> lightningNetworkOptions,
    IAuthorizationService authorizationService,
    PaymentMethodHandlerDictionary handlers,
    LightningHistogramService lnHistogramService)
    : ViewComponent
{
    private const HistogramType DefaultType = HistogramType.Week;

    public async Task<IViewComponentResult> InvokeAsync(StoreData store, string cryptoCode, bool initialRendering)
    {
        var defaultCurrency = store.GetStoreBlob().DefaultCurrency;
        var vm = new StoreLightningBalanceViewModel
        {
            StoreId = store.Id,
            CryptoCode = cryptoCode,
            InitialRendering = initialRendering,
            DefaultCurrency = defaultCurrency,
            CurrencyData = currencies.GetCurrencyData(defaultCurrency, true),
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
            var data = await lnHistogramService.GetHistogram(lightningClient, DefaultType, cts.Token);
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
        var network = networkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
        var id = PaymentTypes.LN.GetPaymentMethodId(cryptoCode);
        var existing = store.GetPaymentMethodConfig<LightningPaymentMethodConfig>(id, handlers);
        if (existing == null)
            return null;

        if (existing.GetExternalLightningUrl() is { } connectionString)
        {
            return lightningClientFactory.Create(connectionString, network);
        }
        if (existing.IsInternalNode && lightningNetworkOptions.Value.InternalLightningByCryptoCode.TryGetValue(cryptoCode, out var internalLightningNode))
        {
            var result = await authorizationService.AuthorizeAsync(HttpContext.User, null,
                new PolicyRequirement(Policies.CanUseInternalLightningNode));
            return result.Succeeded ? internalLightningNode : null;
        }

        return null;
    }
}
