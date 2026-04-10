#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Plugins.Emails.Services;
using BTCPayServer.Services.Labels;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers;

[Route("stores")]
[Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public partial class UIStoresController : Controller
{
    public UIStoresController(
        BTCPayServerOptions btcpayServerOptions,
        BTCPayServerEnvironment btcpayEnv,
        StoreRepository storeRepo,
        UserManager<ApplicationUser> userManager,
        PermissionService permissionService,
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
        IDataProtectionProvider dataProtector,
        IOptions<LightningNetworkOptions> lightningNetworkOptions,
        IOptions<ExternalServicesOptions> externalServiceOptions,
        IHtmlHelper html,
        DefaultRulesCollection defaultRules,
        EmailSenderFactory emailSenderFactory,
        WalletFileParsers onChainWalletParsers,
        UIUserStoresController userStoresController,
        CallbackGenerator callbackGenerator,
        UriResolver uriResolver,
        CurrencyNameTable currencyNameTable,
        IStringLocalizer stringLocalizer,
        EventAggregator eventAggregator,
        LightningHistogramService lnHistogramService,
        LightningClientFactoryService lightningClientFactory,
        StoreLabelRepository storeLabelRepository)
    {
        _rateFactory = rateFactory;
        _storeRepo = storeRepo;
        _userManager = userManager;
        _permissionService = permissionService;
        _langService = langService;
        _walletProvider = walletProvider;
        _handlers = paymentMethodHandlerDictionary;
        _policiesSettings = policiesSettings;
        _authorizationService = authorizationService;
        _appService = appService;
        _fileService = fileService;
        _networkProvider = networkProvider;
        _explorerProvider = explorerProvider;
        _btcpayServerOptions = btcpayServerOptions;
        _btcPayEnv = btcpayEnv;
        _externalServiceOptions = externalServiceOptions;
        _emailSenderFactory = emailSenderFactory;
        _onChainWalletParsers = onChainWalletParsers;
        _userStoresController = userStoresController;
        _callbackGenerator = callbackGenerator;
        _uriResolver = uriResolver;
        _currencyNameTable = currencyNameTable;
        _eventAggregator = eventAggregator;
        _html = html;
        _defaultRules = defaultRules;
        _dataProtector = dataProtector.CreateProtector("ConfigProtector");
        _lightningNetworkOptions = lightningNetworkOptions.Value;
        _lnHistogramService = lnHistogramService;
        _lightningClientFactory = lightningClientFactory;
        StringLocalizer = stringLocalizer;
        _storeLabelRepository = storeLabelRepository;
    }

    private readonly BTCPayServerOptions _btcpayServerOptions;
    private readonly BTCPayServerEnvironment _btcPayEnv;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly BTCPayWalletProvider _walletProvider;
    private readonly StoreRepository _storeRepo;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly PermissionService _permissionService;
    private readonly RateFetcher _rateFactory;
    private readonly CurrencyNameTable _currencyNameTable;
    private readonly ExplorerClientProvider _explorerProvider;
    private readonly LanguageService _langService;
    private readonly PaymentMethodHandlerDictionary _handlers;
    private readonly DefaultRulesCollection _defaultRules;
    private readonly PoliciesSettings _policiesSettings;
    private readonly IAuthorizationService _authorizationService;
    private readonly AppService _appService;
    private readonly IFileService _fileService;
    private readonly IOptions<ExternalServicesOptions> _externalServiceOptions;
    private readonly EmailSenderFactory _emailSenderFactory;
    private readonly WalletFileParsers _onChainWalletParsers;
    private readonly UIUserStoresController _userStoresController;
    private readonly CallbackGenerator _callbackGenerator;
    private readonly UriResolver _uriResolver;
    private readonly EventAggregator _eventAggregator;
    private readonly IHtmlHelper _html;
    private readonly LightningNetworkOptions _lightningNetworkOptions;
    private readonly IDataProtector _dataProtector;
    private readonly LightningHistogramService _lnHistogramService;
    private readonly LightningClientFactoryService _lightningClientFactory;
    private readonly StoreLabelRepository _storeLabelRepository;

    public IStringLocalizer StringLocalizer { get; }

    [AllowAnonymous]
    [HttpGet("{storeId}/index")]
    public async Task<IActionResult> Index(string storeId)
    {
        var userId = GetUserId();
        if (userId is null)
            return Forbid();
        var store = await _storeRepo.FindStore(storeId, userId);
        if (store is null)
            return NotFound();
        // Keep selected store in context/cookie even for limited roles that will be redirected
        // away from dashboard to wallets.
        HttpContext.SetStoreData(store);
        HttpContext.SetPreferredStoreId(storeId);
        if ((await _authorizationService.AuthorizeAsync(User, Policies.CanModifyStoreSettings)).Succeeded)
        {
            return RedirectToAction("Dashboard", new { storeId });
        }
        if ((await _authorizationService.AuthorizeAsync(User, Policies.CanViewInvoices)).Succeeded)
        {
            return RedirectToAction("ListInvoices", "UIInvoice", new { storeId });
        }
        var permissionSet = store.GetPermissionSet(userId);
        if (permissionSet.HasPermission(Policies.CanViewWallet, store.Id, _permissionService))
        {
            var walletId = _handlers.OfType<BitcoinLikePaymentHandler>()
                .Select(handler => handler.Network.CryptoCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(cryptoCode => !cryptoCode.Equals("BTC", StringComparison.OrdinalIgnoreCase))
                .Select(cryptoCode => new
                {
                    CryptoCode = cryptoCode,
                    HasWallet = store.GetPaymentMethodConfig<DerivationSchemeSettings>(PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode), _handlers) is not null,
                    WalletTypePolicy = cryptoCode.Equals("BTC", StringComparison.OrdinalIgnoreCase)
                        ? Policies.CanUseBitcoinOnchain
                        : Policies.CanUseOtherWallets
                })
                .Where(wallet => wallet.HasWallet && permissionSet.HasPermission(wallet.WalletTypePolicy, store.Id, _permissionService))
                .Select(wallet => new WalletId(storeId, wallet.CryptoCode).ToString())
                .FirstOrDefault();

            if (walletId is not null)
                return RedirectToAction(nameof(UIWalletsController.WalletTransactions), "UIWallets", new { walletId });

            return RedirectToAction("ListWallets", "UIWallets");
        }
        return Forbid();
    }

    public StoreData CurrentStore => HttpContext.GetStoreData();

    public PaymentMethodOptionViewModel.Format[] GetEnabledPaymentMethodChoices(StoreData storeData)
    {
        var enabled = storeData.GetEnabledPaymentIds();

        return enabled
            .Select(o =>
                new PaymentMethodOptionViewModel.Format()
                {
                    Name = o.ToString(),
                    Value = o.ToString(),
                    PaymentId = o
                }).ToArray();
    }

    private string? GetUserId() => User.Identity?.AuthenticationType != AuthenticationSchemes.Cookie ? null : User.GetIdOrNull();
}
