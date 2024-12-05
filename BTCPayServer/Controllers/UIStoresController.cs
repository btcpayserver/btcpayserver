#nullable enable
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.HostedServices.Webhooks;
using BTCPayServer.Models.StoreViewModels;
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
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers;

[Route("stores")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[AutoValidateAntiforgeryToken]
public partial class UIStoresController : Controller
{
    public UIStoresController(
        BTCPayServerOptions btcpayServerOptions,
        BTCPayServerEnvironment btcpayEnv,
        StoreRepository storeRepo,
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
        LightningClientFactoryService lightningClientFactory)
    {
        _rateFactory = rateFactory;
        _storeRepo = storeRepo;
        _tokenRepository = tokenRepo;
        _userManager = userManager;
        _langService = langService;
        _tokenController = tokenController;
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
        _webhookNotificationManager = webhookNotificationManager;
        _lightningNetworkOptions = lightningNetworkOptions.Value;
        _lnHistogramService = lnHistogramService;
        _lightningClientFactory = lightningClientFactory;
        StringLocalizer = stringLocalizer;
    }

    private readonly BTCPayServerOptions _btcpayServerOptions;
    private readonly BTCPayServerEnvironment _btcPayEnv;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly BTCPayWalletProvider _walletProvider;
    private readonly BitpayAccessTokenController _tokenController;
    private readonly StoreRepository _storeRepo;
    private readonly TokenRepository _tokenRepository;
    private readonly UserManager<ApplicationUser> _userManager;
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
    private readonly WebhookSender _webhookNotificationManager;
    private readonly LightningNetworkOptions _lightningNetworkOptions;
    private readonly IDataProtector _dataProtector;
    private readonly LightningHistogramService _lnHistogramService;
    private readonly LightningClientFactoryService _lightningClientFactory;

    public string? GeneratedPairingCode { get; set; }
    public IStringLocalizer StringLocalizer { get; }

    [TempData]
    private bool StoreNotConfigured { get; set; }
        
    [AllowAnonymous]
    [HttpGet("{storeId}/index")]
    public async Task<IActionResult> Index(string storeId)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId))
            return Forbid();
            
        var store = await _storeRepo.FindStore(storeId);
        if (store is null)
            return NotFound();

        if ((await _authorizationService.AuthorizeAsync(User, Policies.CanModifyStoreSettings)).Succeeded)
        {
            return RedirectToAction("Dashboard", new { storeId });
        }
        if ((await _authorizationService.AuthorizeAsync(User, Policies.CanViewInvoices)).Succeeded)
        {
            return RedirectToAction("ListInvoices", "UIInvoice", new { storeId });
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

    private string? GetUserId()
    {
        return User.Identity?.AuthenticationType != AuthenticationSchemes.Cookie ? null : _userManager.GetUserId(User);
    }
}
