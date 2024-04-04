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
using Microsoft.Extensions.Options;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers
{
    [Route("stores")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [AutoValidateAntiforgeryToken]
    public partial class UIStoresController : Controller
    {
        public UIStoresController(
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
            SettingsRepository settingsRepository,
            EventAggregator eventAggregator)
        {
            _RateFactory = rateFactory;
            _Repo = repo;
            _TokenRepository = tokenRepo;
            _UserManager = userManager;
            _LangService = langService;
            _TokenController = tokenController;
            _WalletProvider = walletProvider;
            _handlers = paymentMethodHandlerDictionary;
            _policiesSettings = policiesSettings;
            _authorizationService = authorizationService;
            _appService = appService;
            _fileService = fileService;
            DataProtector = dataProtector.CreateProtector("ConfigProtector");
            WebhookNotificationManager = webhookNotificationManager;
            LightningNetworkOptions = lightningNetworkOptions.Value;
            _EventAggregator = eventAggregator;
            _NetworkProvider = networkProvider;
            _ExplorerProvider = explorerProvider;
            _ServiceProvider = serviceProvider;
            _BtcpayServerOptions = btcpayServerOptions;
            _BTCPayEnv = btcpayEnv;
            _externalServiceOptions = externalServiceOptions;
            _lightningClientFactoryService = lightningClientFactoryService;
            _emailSenderFactory = emailSenderFactory;
            _onChainWalletParsers = onChainWalletParsers;
            _settingsRepository = settingsRepository;
            _eventAggregator = eventAggregator;
            Html = html;
        }

        readonly BTCPayServerOptions _BtcpayServerOptions;
        readonly BTCPayServerEnvironment _BTCPayEnv;
        readonly IServiceProvider _ServiceProvider;
        readonly BTCPayNetworkProvider _NetworkProvider;
        readonly BTCPayWalletProvider _WalletProvider;
        readonly BitpayAccessTokenController _TokenController;
        readonly StoreRepository _Repo;
        readonly TokenRepository _TokenRepository;
        readonly UserManager<ApplicationUser> _UserManager;
        readonly RateFetcher _RateFactory;
        readonly SettingsRepository _settingsRepository;
        private readonly ExplorerClientProvider _ExplorerProvider;
        private readonly LanguageService _LangService;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly PoliciesSettings _policiesSettings;
        private readonly IAuthorizationService _authorizationService;
        private readonly AppService _appService;
        private readonly IFileService _fileService;
        private readonly EventAggregator _EventAggregator;
        private readonly IOptions<ExternalServicesOptions> _externalServiceOptions;
        private readonly LightningClientFactoryService _lightningClientFactoryService;
        private readonly EmailSenderFactory _emailSenderFactory;
        private readonly WalletFileParsers _onChainWalletParsers;
        private readonly EventAggregator _eventAggregator;

        public string? GeneratedPairingCode { get; set; }
        public WebhookSender WebhookNotificationManager { get; }
        public IHtmlHelper Html { get; }
        public LightningNetworkOptions LightningNetworkOptions { get; }
        public IDataProtector DataProtector { get; }

        [TempData]
        public bool StoreNotConfigured
        {
            get; set;
        }
        
        [AllowAnonymous]
        [HttpGet("{storeId}/index")]
        public async Task<IActionResult> Index(string storeId)
        {
            var userId = _UserManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
                return Forbid();
            
            var store = await _Repo.FindStore(storeId);
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
            if (User.Identity?.AuthenticationType != AuthenticationSchemes.Cookie)
                return null;
            return _UserManager.GetUserId(User);
        }
    }
}
