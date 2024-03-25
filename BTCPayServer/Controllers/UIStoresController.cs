#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.HostedServices.Webhooks;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Security.Bitpay;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Mails;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Options;
using NBitcoin;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers;

[Route("stores")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[AutoValidateAntiforgeryToken]
public partial class UIStoresController(
    IServiceProvider serviceProvider,
    BTCPayServerOptions btcpayServerOptions,
    BTCPayServerEnvironment btcpayEnv,
    StoreRepository repo,
    TokenRepository tokenRepo,
    UserManager<ApplicationUser> userManager,
    BitpayAccessTokenController tokenController,
    BTCPayWalletProvider walletProvider,
    BTCPayNetworkProvider networkProvider,
    RateFetcher rateFactory,
    ExplorerClientProvider explorerProvider,
    LanguageService langService,
    PaymentMethodHandlerDictionary paymentMethodHandlerDictionary,
    PoliciesSettings policiesSettings,
    IAuthorizationService authorizationService,
    AppService appService,
    IFileService fileService,
    WebhookSender webhookNotificationManager,
    IDataProtectionProvider dataProtector,
    IOptions<LightningNetworkOptions> lightningNetworkOptions,
    IOptions<ExternalServicesOptions> externalServiceOptions,
    IHtmlHelper html,
    LightningClientFactoryService lightningClientFactoryService,
    EmailSenderFactory emailSenderFactory,
    WalletFileParsers onChainWalletParsers,
    ISettingsRepository settingsRepository,
    EventAggregator eventAggregator)
    : Controller
{
    public string? GeneratedPairingCode { get; set; }
    private WebhookSender WebhookNotificationManager { get; } = webhookNotificationManager;
    private IHtmlHelper Html { get; } = html;
    private LightningNetworkOptions LightningNetworkOptions { get; } = lightningNetworkOptions.Value;
    private IDataProtector DataProtector { get; } = dataProtector.CreateProtector("ConfigProtector");
        
    public StoreData CurrentStore => HttpContext.GetStoreData();

    [TempData]
    private bool StoreNotConfigured { get; set; }
        
    [AllowAnonymous]
    [HttpGet("{storeId}/index")]
    public async Task<IActionResult> Index(string storeId)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId))
            return Forbid();
            
        var store = await repo.FindStore(storeId);
        if (store is null)
            return NotFound();

        if ((await authorizationService.AuthorizeAsync(User, Policies.CanModifyStoreSettings)).Succeeded)
        {
            return RedirectToAction("Dashboard", new { storeId });
        }
        if ((await authorizationService.AuthorizeAsync(User, Policies.CanViewInvoices)).Succeeded)
        {
            return RedirectToAction("ListInvoices", "UIInvoice", new { storeId });
        }
        return Forbid();
    }

    public PaymentMethodOptionViewModel.Format[] GetEnabledPaymentMethodChoices(StoreData storeData)
    {
        var enabled = storeData.GetEnabledPaymentIds(networkProvider);

        return enabled
            .Select(o =>
                new PaymentMethodOptionViewModel.Format()
                {
                    Name = o.ToPrettyString(),
                    Value = o.ToString(),
                    PaymentId = o
                }).ToArray();
    }

    internal void AddPaymentMethods(StoreData store, StoreBlob storeBlob,
        out List<StoreDerivationScheme> derivationSchemes, out List<StoreLightningNode> lightningNodes)
    {
        var excludeFilters = storeBlob.GetExcludedPaymentMethods();
        var derivationByCryptoCode =
            store
                .GetSupportedPaymentMethods(networkProvider)
                .OfType<DerivationSchemeSettings>()
                .ToDictionary(c => c.Network.CryptoCode.ToUpperInvariant());

        var lightningByCryptoCode = store
            .GetSupportedPaymentMethods(networkProvider)
            .OfType<LightningSupportedPaymentMethod>()
            .Where(method => method.PaymentId.PaymentType == LightningPaymentType.Instance)
            .ToDictionary(c => c.CryptoCode.ToUpperInvariant());

        derivationSchemes = new List<StoreDerivationScheme>();
        lightningNodes = new List<StoreLightningNode>();

        foreach (var paymentMethodId in paymentMethodHandlerDictionary.Distinct().SelectMany(handler => handler.GetSupportedPaymentMethods()))
        {
            switch (paymentMethodId.PaymentType)
            {
                case BitcoinPaymentType _:
                    var strategy = derivationByCryptoCode.TryGet(paymentMethodId.CryptoCode);
                    var network = networkProvider.GetNetwork<BTCPayNetwork>(paymentMethodId.CryptoCode);
                    var value = strategy?.ToPrettyString() ?? string.Empty;

                    derivationSchemes.Add(new StoreDerivationScheme
                    {
                        Crypto = paymentMethodId.CryptoCode,
                        WalletSupported = network.WalletSupported,
                        Value = value,
                        WalletId = new WalletId(store.Id, paymentMethodId.CryptoCode),
                        Enabled = !excludeFilters.Match(paymentMethodId) && strategy != null,
#if ALTCOINS
                            Collapsed = network is Plugins.Altcoins.ElementsBTCPayNetwork elementsBTCPayNetwork && elementsBTCPayNetwork.NetworkCryptoCode != elementsBTCPayNetwork.CryptoCode && string.IsNullOrEmpty(value)
#endif
                    });
                    break;

                case LNURLPayPaymentType:
                    break;

                case LightningPaymentType _:
                    var lightning = lightningByCryptoCode.TryGet(paymentMethodId.CryptoCode);
                    var isEnabled = !excludeFilters.Match(paymentMethodId) && lightning != null;
                    lightningNodes.Add(new StoreLightningNode
                    {
                        CryptoCode = paymentMethodId.CryptoCode,
                        Address = lightning?.GetDisplayableConnectionString(),
                        Enabled = isEnabled
                    });
                    break;
            }
        }
    }

    private string? GetUserId()
    {
        return User.Identity?.AuthenticationType != AuthenticationSchemes.Cookie ? null : userManager.GetUserId(User);
    }
}
