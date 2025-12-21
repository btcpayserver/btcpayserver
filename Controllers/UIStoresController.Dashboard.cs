#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Payments.Lightning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Controllers;

public partial class UIStoresController
{
    [HttpGet("{storeId}")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Dashboard()
    {
        var store = CurrentStore;
        if (store is null)
            return NotFound();
            
        var storeBlob = store.GetStoreBlob();

        AddPaymentMethods(store, storeBlob,
            out var derivationSchemes, out var lightningNodes);

        var walletEnabled = derivationSchemes.Any(scheme => !string.IsNullOrEmpty(scheme.Value) && scheme.Enabled);
        var lightningEnabled = lightningNodes.Any(ln => !string.IsNullOrEmpty(ln.Address) && ln.Enabled);
        var cryptoCode = _networkProvider.DefaultNetwork.CryptoCode;
        var vm = new StoreDashboardViewModel
        {
            WalletEnabled = walletEnabled,
            LightningEnabled = lightningEnabled,
            LightningSupported = _networkProvider.GetNetwork<BTCPayNetwork>(cryptoCode)?.SupportLightning is true,
            StoreId = CurrentStore.Id,
            StoreName = CurrentStore.StoreName,
            CryptoCode = cryptoCode,
            Network = _networkProvider.DefaultNetwork,
            IsSetUp = walletEnabled || lightningEnabled
        };

        // Widget data
        if (vm is { WalletEnabled: false, LightningEnabled: false })
            return View(vm);

        var userId = GetUserId();
        if (userId is null)
            return NotFound();

        var apps = await _appService.GetAllApps(userId, false, store.Id);
        foreach (var app in apps)
        {
            var appData = await _appService.GetAppData(userId, app.Id);
            vm.Apps.Add(appData);
        }

        return View(vm);
    }

    [HttpGet("{storeId}/dashboard/{cryptoCode}/lightning/balance")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public IActionResult LightningBalance(string storeId, string cryptoCode)
    {
        var store = HttpContext.GetStoreData();
        return store != null
             ? ViewComponent("StoreLightningBalance", new { Store = store, CryptoCode = cryptoCode })
             : NotFound();
    }

    [HttpGet("{storeId}/dashboard/{cryptoCode}/numbers")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public IActionResult StoreNumbers(string storeId, string cryptoCode)
    {
        var store = HttpContext.GetStoreData();
        return store != null
            ? ViewComponent("StoreNumbers", new { Store = store, CryptoCode = cryptoCode })
            : NotFound();
    }

    [HttpGet("{storeId}/dashboard/{cryptoCode}/recent-transactions")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public IActionResult RecentTransactions(string storeId, string cryptoCode)
    {
        var store = HttpContext.GetStoreData();
        return store != null
            ? ViewComponent("StoreRecentTransactions", new { Store = store, CryptoCode = cryptoCode })
            : NotFound();
    }

    [HttpGet("{storeId}/dashboard/{cryptoCode}/recent-invoices")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public IActionResult RecentInvoices(string storeId, string cryptoCode)
    {
        var store = HttpContext.GetStoreData();
        return store != null
            ? ViewComponent("StoreRecentInvoices", new { Store = store, CryptoCode = cryptoCode })
            : NotFound();
    }

    internal void AddPaymentMethods(StoreData store, StoreBlob storeBlob,
        out List<StoreDerivationScheme> derivationSchemes, out List<StoreLightningNode> lightningNodes)
    {
        var excludeFilters = storeBlob.GetExcludedPaymentMethods();
        var derivationByCryptoCode =
            store
                .GetPaymentMethodConfigs<DerivationSchemeSettings>(_handlers)
                .ToDictionary(c => ((IHasNetwork)_handlers[c.Key]).Network.CryptoCode, c => c.Value);

        var lightningByCryptoCode = store
            .GetPaymentMethodConfigs(_handlers)
            .Where(c => c.Value is LightningPaymentMethodConfig)
            .ToDictionary(c => ((IHasNetwork)_handlers[c.Key]).Network.CryptoCode, c => (LightningPaymentMethodConfig)c.Value);

        derivationSchemes = [];
        lightningNodes = [];

        foreach (var handler in _handlers)
        {
            if (handler is BitcoinLikePaymentHandler { Network: var network })
            {
                var strategy = derivationByCryptoCode.TryGet(network.CryptoCode);
                var value = strategy?.ToPrettyString() ?? string.Empty;
                derivationSchemes.Add(new StoreDerivationScheme
                {
                    Crypto = network.CryptoCode,
                    PaymentMethodId = handler.PaymentMethodId,
                    WalletSupported = network.WalletSupported,
                    ReadonlyWallet = network.ReadonlyWallet,
                    Value = value,
                    WalletId = new WalletId(store.Id, network.CryptoCode),
                    Enabled = !excludeFilters.Match(handler.PaymentMethodId) && strategy != null,
                    Collapsed = network is Plugins.Altcoins.ElementsBTCPayNetwork { IsNativeAsset : false }  && string.IsNullOrEmpty(value)

                });
            }
            else if (handler is LightningLikePaymentHandler)
            {
                var lnNetwork = ((IHasNetwork)handler).Network;
                var lightning = lightningByCryptoCode.TryGet(lnNetwork.CryptoCode);
                var isEnabled = !excludeFilters.Match(handler.PaymentMethodId) && lightning != null;
                lightningNodes.Add(new StoreLightningNode
                {
                    CryptoCode = lnNetwork.CryptoCode,
                    PaymentMethodId = handler.PaymentMethodId,
                    Address = lightning?.GetDisplayableConnectionString(),
                    Enabled = isEnabled,
                    CacheKey = GetCacheKey(lightning)
            });
            }
        }
    }

    private string? GetCacheKey(LightningPaymentMethodConfig? lightning)
    {
        if (lightning is null)
            return null;
        var connStr = lightning.IsInternalNode ? lightning.InternalNodeRef : lightning.ConnectionString;
        connStr ??= string.Empty;
        return "LN-INFO-" + Encoders.Hex.EncodeData(SHA256.HashData(Encoding.UTF8.GetBytes(connStr))[0..4]);
    }
}
