#nullable enable
using System.Linq;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Dashboard.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers;

public partial class UIStoresController
{
    private const string Dashboard2ViewPath = "/Plugins/Dashboard/Views/Dashboard2.cshtml";

    [HttpGet("{storeId}/2")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public IActionResult Dashboard2()
    {
        var store = CurrentStore;
        if (store is null)
            return NotFound();
        HttpContext.SetPreferredStoreId(store.Id);

        var storeBlob = store.GetStoreBlob();
        AddPaymentMethods(store, storeBlob, out var derivationSchemes, out var lightningNodes);

        var walletEnabled = derivationSchemes.Any(scheme => !string.IsNullOrEmpty(scheme.Value) && scheme.Enabled);
        var lightningEnabled = lightningNodes.Any(ln => !string.IsNullOrEmpty(ln.Address) && ln.Enabled);
        var cryptoCode = _networkProvider.DefaultCryptoCode;

        var vm = new Dashboard2ViewModel
        {
            OwnerKey = "store:" + store.Id,
            // Any payment method at all (wallet, lightning, or plugin-provided like Monero, Liquid, etc.)
            IsSetUp = walletEnabled || lightningEnabled
                      || store.GetPaymentMethodConfigs(onlyEnabled: true).Count > 0,
            WalletEnabled = walletEnabled,
            LightningEnabled = lightningEnabled,
            LightningSupported = _networkProvider.GetNetwork<BTCPayNetwork>(cryptoCode)?.SupportLightning is true,
            CryptoCode = cryptoCode
        };

        // The view lives under the plugin folder; the convention-based view location expander
        // looks in /Views/UIStores/, which is not where this plugin's view lives.
        return View(Dashboard2ViewPath, vm);
    }
}
