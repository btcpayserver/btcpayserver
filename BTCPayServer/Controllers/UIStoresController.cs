#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.HostedServices.Webhooks;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Rating;
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
using NBitcoin.DataEncoders;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers
{
   
    [Route("stores")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
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
            EventAggregator eventAggregator,
            AppService appService,
            IFileService fileService,
            WebhookSender webhookNotificationManager,
            IDataProtectionProvider dataProtector,
            IOptions<LightningNetworkOptions> lightningNetworkOptions,
            IOptions<ExternalServicesOptions> externalServiceOptions,
            IHtmlHelper html,
            LightningClientFactoryService lightningClientFactoryService,
            EmailSenderFactory emailSenderFactory)
        {
            _RateFactory = rateFactory;
            _Repo = repo;
            _TokenRepository = tokenRepo;
            _UserManager = userManager;
            _LangService = langService;
            _TokenController = tokenController;
            _WalletProvider = walletProvider;
            _paymentMethodHandlerDictionary = paymentMethodHandlerDictionary;
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
        private readonly ExplorerClientProvider _ExplorerProvider;
        private readonly LanguageService _LangService;
        private readonly PaymentMethodHandlerDictionary _paymentMethodHandlerDictionary;
        private readonly PoliciesSettings _policiesSettings;
        private readonly IAuthorizationService _authorizationService;
        private readonly AppService _appService;
        private readonly IFileService _fileService;
        private readonly EventAggregator _EventAggregator;
        private readonly IOptions<ExternalServicesOptions> _externalServiceOptions;
        private readonly LightningClientFactoryService _lightningClientFactoryService;
        private readonly EmailSenderFactory _emailSenderFactory;

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
            
            var store = await _Repo.FindStore(storeId, userId);
            if (store is null)
            {
                return Forbid();
            }
            if (store.GetPermissionSet(userId).Contains(Policies.CanModifyStoreSettings, storeId))
            {
                return RedirectToAction("Dashboard", new { storeId });
            }
            if (store.GetPermissionSet(userId).Contains(Policies.CanViewInvoices, storeId))
            {
                return RedirectToAction("ListInvoices", "UIInvoice", new { storeId });
            }
            HttpContext.SetStoreData(store);
            return View();
        }

        [HttpGet("{storeId}/users")]
        public async Task<IActionResult> StoreUsers()
        {
            var vm = new StoreUsersViewModel { Role = StoreRoleId.Guest.Role };
            await FillUsers(vm);
            return View(vm);
        }

        private async Task FillUsers(StoreUsersViewModel vm)
        {
            var users = await _Repo.GetStoreUsers(CurrentStore.Id);
            vm.StoreId = CurrentStore.Id;
            vm.Users = users.Select(u => new StoreUsersViewModel.StoreUserViewModel()
            {
                Email = u.Email,
                Id = u.Id,
                Role = u.StoreRole.Role
            }).ToList();
        }

        public StoreData CurrentStore => HttpContext.GetStoreData();

        [HttpPost("{storeId}/users")]
        public async Task<IActionResult> StoreUsers(string storeId, StoreUsersViewModel vm)
        {
            await FillUsers(vm);
            if (!ModelState.IsValid)
            {
                return View(vm);
            }
            var user = await _UserManager.FindByEmailAsync(vm.Email);
            if (user == null)
            {
                ModelState.AddModelError(nameof(vm.Email), "User not found");
                return View(vm);
            }

            var roles = await _Repo.GetStoreRoles(CurrentStore.Id);
            if (roles.All(role => role.Id != vm.Role))
            {
                ModelState.AddModelError(nameof(vm.Role), "Invalid role");
                return View(vm);
            }
            var roleId = await _Repo.ResolveStoreRoleId(storeId, vm.Role);

            if (!await _Repo.AddStoreUser(CurrentStore.Id, user.Id, roleId))
            {
                ModelState.AddModelError(nameof(vm.Email), "The user already has access to this store");
                return View(vm);
            }
            TempData[WellKnownTempData.SuccessMessage] = "User added successfully.";
            return RedirectToAction(nameof(StoreUsers));
        }

        [HttpGet("{storeId}/users/{userId}/delete")]
        public async Task<IActionResult> DeleteStoreUser(string userId)
        {
            var user = await _UserManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();
            return View("Confirm", new ConfirmModel("Remove store user", $"This action will prevent <strong>{Html.Encode(user.Email)}</strong> from accessing this store and its settings. Are you sure?", "Remove"));
        }

        [HttpPost("{storeId}/users/{userId}/delete")]
        public async Task<IActionResult> DeleteStoreUserPost(string storeId, string userId)
        {
            if (await _Repo.RemoveStoreUser(storeId, userId))
                TempData[WellKnownTempData.SuccessMessage] = "User removed successfully.";
            else
            {
                TempData[WellKnownTempData.ErrorMessage] = "Removing this user would result in the store having no owner.";
            }
            return RedirectToAction(nameof(StoreUsers), new { storeId, userId });
        }

        [HttpGet("{storeId}/rates")]
        public IActionResult Rates()
        {
            var exchanges = GetSupportedExchanges();
            var storeBlob = CurrentStore.GetStoreBlob();
            var vm = new RatesViewModel();
            vm.SetExchangeRates(exchanges, storeBlob.PreferredExchange ?? storeBlob.GetRecommendedExchange());
            vm.Spread = (double)(storeBlob.Spread * 100m);
            vm.StoreId = CurrentStore.Id;
            vm.Script = storeBlob.GetRateRules(_NetworkProvider).ToString();
            vm.DefaultScript = storeBlob.GetDefaultRateRules(_NetworkProvider).ToString();
            vm.AvailableExchanges = exchanges;
            vm.DefaultCurrencyPairs = storeBlob.GetDefaultCurrencyPairString();
            vm.ShowScripting = storeBlob.RateScripting;
            return View(vm);
        }

        [HttpPost("{storeId}/rates")]
        public async Task<IActionResult> Rates(RatesViewModel model, string? command = null, string? storeId = null, CancellationToken cancellationToken = default)
        {
            if (command == "scripting-on")
            {
                return RedirectToAction(nameof(ShowRateRules), new { scripting = true, storeId = model.StoreId });
            }
            else if (command == "scripting-off")
            {
                return RedirectToAction(nameof(ShowRateRules), new { scripting = false, storeId = model.StoreId });
            }

            var exchanges = GetSupportedExchanges();
            model.SetExchangeRates(exchanges, model.PreferredExchange ?? this.HttpContext.GetStoreData().GetStoreBlob().GetRecommendedExchange());
            model.StoreId = storeId ?? model.StoreId;
            CurrencyPair[]? currencyPairs = null;
            try
            {
                currencyPairs = model.DefaultCurrencyPairs?
                     .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                     .Select(p => CurrencyPair.Parse(p))
                     .ToArray();
            }
            catch
            {
                ModelState.AddModelError(nameof(model.DefaultCurrencyPairs), "Invalid currency pairs (should be for example: BTC_USD,BTC_CAD,BTC_JPY)");
            }
            if (!ModelState.IsValid)
            {
                return View(model);
            }
            if (model.PreferredExchange != null)
                model.PreferredExchange = model.PreferredExchange.Trim().ToLowerInvariant();

            var blob = CurrentStore.GetStoreBlob();
            model.DefaultScript = blob.GetDefaultRateRules(_NetworkProvider).ToString();
            model.AvailableExchanges = exchanges;

            blob.PreferredExchange = model.PreferredExchange;
            blob.Spread = (decimal)model.Spread / 100.0m;
            blob.DefaultCurrencyPairs = currencyPairs;
            if (!model.ShowScripting)
            {
                if (!exchanges.Any(provider => provider.Id.Equals(model.PreferredExchange, StringComparison.InvariantCultureIgnoreCase)))
                {
                    ModelState.AddModelError(nameof(model.PreferredExchange), $"Unsupported exchange ({model.RateSource})");
                    return View(model);
                }
            }
            RateRules? rules = null;
            if (model.ShowScripting)
            {
                if (!RateRules.TryParse(model.Script, out rules, out var errors))
                {
                    errors = errors ?? new List<RateRulesErrors>();
                    var errorString = String.Join(", ", errors.ToArray());
                    ModelState.AddModelError(nameof(model.Script), $"Parsing error ({errorString})");
                    return View(model);
                }
                else
                {
                    blob.RateScript = rules.ToString();
                    ModelState.Remove(nameof(model.Script));
                    model.Script = blob.RateScript;
                }
            }
            rules = blob.GetRateRules(_NetworkProvider);

            if (command == "Test")
            {
                if (string.IsNullOrWhiteSpace(model.ScriptTest))
                {
                    ModelState.AddModelError(nameof(model.ScriptTest), "Fill out currency pair to test for (like BTC_USD,BTC_CAD)");
                    return View(model);
                }
                var splitted = model.ScriptTest.Split(',', StringSplitOptions.RemoveEmptyEntries);

                var pairs = new List<CurrencyPair>();
                foreach (var pair in splitted)
                {
                    if (!CurrencyPair.TryParse(pair, out var currencyPair))
                    {
                        ModelState.AddModelError(nameof(model.ScriptTest), $"Invalid currency pair '{pair}' (it should be formatted like BTC_USD,BTC_CAD)");
                        return View(model);
                    }
                    pairs.Add(currencyPair);
                }

                var fetchs = _RateFactory.FetchRates(pairs.ToHashSet(), rules, cancellationToken);
                var testResults = new List<RatesViewModel.TestResultViewModel>();
                foreach (var fetch in fetchs)
                {
                    var testResult = await (fetch.Value);
                    testResults.Add(new RatesViewModel.TestResultViewModel()
                    {
                        CurrencyPair = fetch.Key.ToString(),
                        Error = testResult.Errors.Count != 0,
                        Rule = testResult.Errors.Count == 0 ? testResult.Rule + " = " + testResult.BidAsk.Bid.ToString(CultureInfo.InvariantCulture)
                                                            : testResult.EvaluatedRule
                    });
                }
                model.TestRateRules = testResults;
                return View(model);
            }
            else // command == Save
            {
                if (CurrentStore.SetStoreBlob(blob))
                {
                    await _Repo.UpdateStore(CurrentStore);
                    TempData[WellKnownTempData.SuccessMessage] = "Rate settings updated";
                }
                return RedirectToAction(nameof(Rates), new
                {
                    storeId = CurrentStore.Id
                });
            }
        }

        [HttpGet("{storeId}/rates/confirm")]
        public IActionResult ShowRateRules(bool scripting)
        {
            return View("Confirm", new ConfirmModel
            {
                Action = "Continue",
                Title = "Rate rule scripting",
                Description = scripting ?
                                "This action will modify your current rate sources. Are you sure to turn on rate rules scripting? (Advanced users)"
                                : "This action will delete your rate script. Are you sure to turn off rate rules scripting?",
                ButtonClass = scripting ? "btn-primary" : "btn-danger"
            });
        }

        [HttpPost("{storeId}/rates/confirm")]
        public async Task<IActionResult> ShowRateRulesPost(bool scripting)
        {
            var blob = CurrentStore.GetStoreBlob();
            blob.RateScripting = scripting;
            blob.RateScript = blob.GetDefaultRateRules(_NetworkProvider).ToString();
            CurrentStore.SetStoreBlob(blob);
            await _Repo.UpdateStore(CurrentStore);
            TempData[WellKnownTempData.SuccessMessage] = "Rate rules scripting " + (scripting ? "activated" : "deactivated");
            return RedirectToAction(nameof(Rates), new { storeId = CurrentStore.Id });
        }

        [HttpGet("{storeId}/checkout")]
        public IActionResult CheckoutAppearance()
        {
            var storeBlob = CurrentStore.GetStoreBlob();
            var vm = new CheckoutAppearanceViewModel();
            SetCryptoCurrencies(vm, CurrentStore);
            vm.PaymentMethodCriteria = CurrentStore.GetSupportedPaymentMethods(_NetworkProvider)
                                    .Where(s => !storeBlob.GetExcludedPaymentMethods().Match(s.PaymentId))
                                    .Where(s => _NetworkProvider.GetNetwork(s.PaymentId.CryptoCode) != null)
                                    .Where(s => s.PaymentId.PaymentType != PaymentTypes.LNURLPay)
                                    .Select(method =>
            {
                var existing = storeBlob.PaymentMethodCriteria.SingleOrDefault(criteria =>
                        criteria.PaymentMethod == method.PaymentId);
                return existing is null
                    ? new PaymentMethodCriteriaViewModel { PaymentMethod = method.PaymentId.ToString(), Value = "" }
                    : new PaymentMethodCriteriaViewModel
                    {
                        PaymentMethod = existing.PaymentMethod.ToString(),
                        Type = existing.Above
                            ? PaymentMethodCriteriaViewModel.CriteriaType.GreaterThan
                            : PaymentMethodCriteriaViewModel.CriteriaType.LessThan,
                        Value = existing.Value?.ToString() ?? ""
                    };
            }).ToList();

            vm.UseClassicCheckout = storeBlob.CheckoutType == Client.Models.CheckoutType.V1;
            vm.CelebratePayment = storeBlob.CelebratePayment;
            vm.PlaySoundOnPayment = storeBlob.PlaySoundOnPayment;
            vm.OnChainWithLnInvoiceFallback = storeBlob.OnChainWithLnInvoiceFallback;
            vm.ShowPayInWalletButton = storeBlob.ShowPayInWalletButton;
            vm.ShowStoreHeader = storeBlob.ShowStoreHeader;
            vm.LightningAmountInSatoshi = storeBlob.LightningAmountInSatoshi;
            vm.RequiresRefundEmail = storeBlob.RequiresRefundEmail;
            vm.LazyPaymentMethods = storeBlob.LazyPaymentMethods;
            vm.RedirectAutomatically = storeBlob.RedirectAutomatically;
            vm.CustomCSS = storeBlob.CustomCSS;
            vm.CustomLogo = storeBlob.CustomLogo;
            vm.SoundFileId = storeBlob.SoundFileId;
            vm.HtmlTitle = storeBlob.HtmlTitle;
            vm.DisplayExpirationTimer = (int)storeBlob.DisplayExpirationTimer.TotalMinutes;
            vm.ReceiptOptions = CheckoutAppearanceViewModel.ReceiptOptionsViewModel.Create(storeBlob.ReceiptOptions);
            vm.AutoDetectLanguage = storeBlob.AutoDetectLanguage;
            vm.SetLanguages(_LangService, storeBlob.DefaultLang);

            return View(vm);
        }

        void SetCryptoCurrencies(CheckoutAppearanceViewModel vm, Data.StoreData storeData)
        {
            var choices = GetEnabledPaymentMethodChoices(storeData);
            var chosen = GetDefaultPaymentMethodChoice(storeData);

            vm.PaymentMethods = new SelectList(choices, nameof(chosen.Value), nameof(chosen.Name), chosen?.Value);
            vm.DefaultPaymentMethod = chosen?.Value;
        }

        public PaymentMethodOptionViewModel.Format[] GetEnabledPaymentMethodChoices(StoreData storeData)
        {
            var enabled = storeData.GetEnabledPaymentIds(_NetworkProvider);

            return enabled
                .Select(o =>
                    new PaymentMethodOptionViewModel.Format()
                    {
                        Name = o.ToPrettyString(),
                        Value = o.ToString(),
                        PaymentId = o
                    }).ToArray();
        }

        PaymentMethodOptionViewModel.Format? GetDefaultPaymentMethodChoice(StoreData storeData)
        {
            var enabled = storeData.GetEnabledPaymentIds(_NetworkProvider);
            var defaultPaymentId = storeData.GetDefaultPaymentId();
            var defaultChoice = defaultPaymentId is not null ? defaultPaymentId.FindNearest(enabled) : null;
            if (defaultChoice is null)
            {
                defaultChoice = enabled.FirstOrDefault(e => e.CryptoCode == _NetworkProvider.DefaultNetwork.CryptoCode && e.PaymentType == PaymentTypes.BTCLike) ??
                                enabled.FirstOrDefault(e => e.CryptoCode == _NetworkProvider.DefaultNetwork.CryptoCode && e.PaymentType == PaymentTypes.LightningLike) ??
                                enabled.FirstOrDefault();
            }
            var choices = GetEnabledPaymentMethodChoices(storeData);

            return defaultChoice is null ? null : choices.FirstOrDefault(c => defaultChoice.ToString().Equals(c.Value, StringComparison.OrdinalIgnoreCase));
        }

        [HttpPost("{storeId}/checkout")]
        public async Task<IActionResult> CheckoutAppearance(CheckoutAppearanceViewModel model, [FromForm] bool RemoveSoundFile = false)
        {
            bool needUpdate = false;
            var blob = CurrentStore.GetStoreBlob();
            var defaultPaymentMethodId = model.DefaultPaymentMethod == null ? null : PaymentMethodId.Parse(model.DefaultPaymentMethod);
            if (CurrentStore.GetDefaultPaymentId() != defaultPaymentMethodId)
            {
                needUpdate = true;
                CurrentStore.SetDefaultPaymentId(defaultPaymentMethodId);
            }
            SetCryptoCurrencies(model, CurrentStore);
            model.SetLanguages(_LangService, model.DefaultLang);
            model.PaymentMethodCriteria ??= new List<PaymentMethodCriteriaViewModel>();
            for (var index = 0; index < model.PaymentMethodCriteria.Count; index++)
            {
                var methodCriterion = model.PaymentMethodCriteria[index];
                if (!string.IsNullOrWhiteSpace(methodCriterion.Value))
                {
                    if (!CurrencyValue.TryParse(methodCriterion.Value, out var value))
                    {
                        model.AddModelError(viewModel => viewModel.PaymentMethodCriteria[index].Value,
                            $"{methodCriterion.PaymentMethod}: Invalid format. Make sure to enter a valid amount and currency code. Examples: '5 USD', '0.001 BTC'", this);
                    }
                }
            }
            
            var userId = GetUserId();
            if (userId is null)
                return NotFound();

            if (model.SoundFile != null)
            {
                if (model.SoundFile.Length > 1_000_000)
                {
                    ModelState.AddModelError(nameof(model.SoundFile), "The uploaded sound file should be less than 1MB");
                }
                else if (!model.SoundFile.ContentType.StartsWith("audio/", StringComparison.InvariantCulture))
                {
                    ModelState.AddModelError(nameof(model.SoundFile), "The uploaded sound file needs to be an audio file");
                }
                else
                {
                    var formFile = await model.SoundFile.Bufferize();
                    if (!FileTypeDetector.IsAudio(formFile.Buffer, formFile.FileName))
                    {
                        ModelState.AddModelError(nameof(model.SoundFile), "The uploaded sound file needs to be an audio file");
                    }
                    else
                    {
                        model.SoundFile = formFile;
                        // delete existing file
                        if (!string.IsNullOrEmpty(blob.SoundFileId))
                        {
                            await _fileService.RemoveFile(blob.SoundFileId, userId);
                        }

                        // add new file
                        try
                        {
                            var storedFile = await _fileService.AddFile(model.SoundFile, userId);
                            blob.SoundFileId = storedFile.Id;
                            needUpdate = true;
                        }
                        catch (Exception e)
                        {
                            ModelState.AddModelError(nameof(model.SoundFile), $"Could not save sound: {e.Message}");
                        }
                    }
                }
            }
            else if (RemoveSoundFile && !string.IsNullOrEmpty(blob.SoundFileId))
            {
                await _fileService.RemoveFile(blob.SoundFileId, userId);
                blob.SoundFileId = null;
                needUpdate = true;
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // Payment criteria for Off-Chain should also affect LNUrl
            foreach (var newCriteria in model.PaymentMethodCriteria.ToList())
            {
                var paymentMethodId = PaymentMethodId.Parse(newCriteria.PaymentMethod);
                if (paymentMethodId.PaymentType == PaymentTypes.LightningLike)
                    model.PaymentMethodCriteria.Add(new PaymentMethodCriteriaViewModel()
                    {
                        PaymentMethod = new PaymentMethodId(paymentMethodId.CryptoCode, PaymentTypes.LNURLPay).ToString(),
                        Type = newCriteria.Type,
                        Value = newCriteria.Value
                    });
                // Should not be able to set LNUrlPay criteria directly in UI
                if (paymentMethodId.PaymentType == PaymentTypes.LNURLPay)
                    model.PaymentMethodCriteria.Remove(newCriteria);
            }
            blob.PaymentMethodCriteria ??= new List<PaymentMethodCriteria>();
            foreach (var newCriteria in model.PaymentMethodCriteria)
            {
                var paymentMethodId = PaymentMethodId.Parse(newCriteria.PaymentMethod);
                var existingCriteria = blob.PaymentMethodCriteria.FirstOrDefault(c => c.PaymentMethod == paymentMethodId);
                if (existingCriteria != null)
                    blob.PaymentMethodCriteria.Remove(existingCriteria);
                CurrencyValue.TryParse(newCriteria.Value, out var cv);
                blob.PaymentMethodCriteria.Add(new PaymentMethodCriteria()
                {
                    Above = newCriteria.Type == PaymentMethodCriteriaViewModel.CriteriaType.GreaterThan,
                    Value = cv,
                    PaymentMethod = paymentMethodId
                });
            }

            blob.ShowPayInWalletButton = model.ShowPayInWalletButton;
            blob.ShowStoreHeader = model.ShowStoreHeader;
            blob.CheckoutType = model.UseClassicCheckout ? Client.Models.CheckoutType.V1 : Client.Models.CheckoutType.V2;
            blob.CelebratePayment = model.CelebratePayment;
            blob.PlaySoundOnPayment = model.PlaySoundOnPayment;
            blob.OnChainWithLnInvoiceFallback = model.OnChainWithLnInvoiceFallback;
            blob.LightningAmountInSatoshi = model.LightningAmountInSatoshi;
            blob.RequiresRefundEmail = model.RequiresRefundEmail;
            blob.LazyPaymentMethods = model.LazyPaymentMethods;
            blob.RedirectAutomatically = model.RedirectAutomatically;
            blob.ReceiptOptions = model.ReceiptOptions.ToDTO();
            blob.CustomLogo = model.CustomLogo;
            blob.CustomCSS = model.CustomCSS;
            blob.HtmlTitle = string.IsNullOrWhiteSpace(model.HtmlTitle) ? null : model.HtmlTitle;
            blob.DisplayExpirationTimer = TimeSpan.FromMinutes(model.DisplayExpirationTimer);
            blob.AutoDetectLanguage = model.AutoDetectLanguage;
            blob.DefaultLang = model.DefaultLang;
            blob.NormalizeToRelativeLinks(Request);
            if (CurrentStore.SetStoreBlob(blob))
            {
                needUpdate = true;
            }
            if (needUpdate)
            {
                await _Repo.UpdateStore(CurrentStore);
                TempData[WellKnownTempData.SuccessMessage] = "Store successfully updated";
            }

            return RedirectToAction(nameof(CheckoutAppearance), new
            {
                storeId = CurrentStore.Id
            });
        }

        internal void AddPaymentMethods(StoreData store, StoreBlob storeBlob,
            out List<StoreDerivationScheme> derivationSchemes, out List<StoreLightningNode> lightningNodes)
        {
            var excludeFilters = storeBlob.GetExcludedPaymentMethods();
            var derivationByCryptoCode =
                store
                .GetSupportedPaymentMethods(_NetworkProvider)
                .OfType<DerivationSchemeSettings>()
                .ToDictionary(c => c.Network.CryptoCode.ToUpperInvariant());

            var lightningByCryptoCode = store
                .GetSupportedPaymentMethods(_NetworkProvider)
                .OfType<LightningSupportedPaymentMethod>()
                .Where(method => method.PaymentId.PaymentType == LightningPaymentType.Instance)
                .ToDictionary(c => c.CryptoCode.ToUpperInvariant());

            derivationSchemes = new List<StoreDerivationScheme>();
            lightningNodes = new List<StoreLightningNode>();

            foreach (var paymentMethodId in _paymentMethodHandlerDictionary.Distinct().SelectMany(handler => handler.GetSupportedPaymentMethods()))
            {
                switch (paymentMethodId.PaymentType)
                {
                    case BitcoinPaymentType _:
                        var strategy = derivationByCryptoCode.TryGet(paymentMethodId.CryptoCode);
                        var network = _NetworkProvider.GetNetwork<BTCPayNetwork>(paymentMethodId.CryptoCode);
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

                    case LNURLPayPaymentType lnurlPayPaymentType:
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

        [HttpGet("{storeId}/settings")]
        public IActionResult GeneralSettings()
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();

            var storeBlob = store.GetStoreBlob();
            var vm = new GeneralSettingsViewModel
            {
                Id = store.Id,
                StoreName = store.StoreName,
                StoreWebsite = store.StoreWebsite,
                StoreSupportUrl = storeBlob.StoreSupportUrl,
                LogoFileId = storeBlob.LogoFileId,
                CssFileId = storeBlob.CssFileId,
                BrandColor = storeBlob.BrandColor,
                NetworkFeeMode = storeBlob.NetworkFeeMode,
                AnyoneCanCreateInvoice = storeBlob.AnyoneCanInvoice,
                PaymentTolerance = storeBlob.PaymentTolerance,
                InvoiceExpiration = (int)storeBlob.InvoiceExpiration.TotalMinutes,
                DefaultCurrency = storeBlob.DefaultCurrency,
                BOLT11Expiration = (long)storeBlob.RefundBOLT11Expiration.TotalDays,
                Archived = store.Archived,
                CanDelete = _Repo.CanDeleteStores()
            };

            return View(vm);
        }

        [HttpPost("{storeId}/settings")]
        public async Task<IActionResult> GeneralSettings(
            GeneralSettingsViewModel model,
            [FromForm] bool RemoveLogoFile = false,
            [FromForm] bool RemoveCssFile = false)
        {
            bool needUpdate = false;
            if (CurrentStore.StoreName != model.StoreName)
            {
                needUpdate = true;
                CurrentStore.StoreName = model.StoreName;
            }

            if (CurrentStore.StoreWebsite != model.StoreWebsite)
            {
                needUpdate = true;
                CurrentStore.StoreWebsite = model.StoreWebsite;
            }

            var blob = CurrentStore.GetStoreBlob();
            blob.StoreSupportUrl = model.StoreSupportUrl;
            blob.AnyoneCanInvoice = model.AnyoneCanCreateInvoice;
            blob.NetworkFeeMode = model.NetworkFeeMode;
            blob.PaymentTolerance = model.PaymentTolerance;
            blob.DefaultCurrency = model.DefaultCurrency;
            blob.InvoiceExpiration = TimeSpan.FromMinutes(model.InvoiceExpiration);
            blob.RefundBOLT11Expiration = TimeSpan.FromDays(model.BOLT11Expiration);
            if (!string.IsNullOrEmpty(model.BrandColor) && !ColorPalette.IsValid(model.BrandColor))
            {
                ModelState.AddModelError(nameof(model.BrandColor), "Invalid color");
                return View(model);
            }
            blob.BrandColor = model.BrandColor;

            var userId = GetUserId();
            if (userId is null)
                return NotFound();

            if (model.LogoFile != null)
            {
                if (model.LogoFile.Length > 1_000_000)
                {
                    ModelState.AddModelError(nameof(model.LogoFile), "The uploaded logo file should be less than 1MB");
                }
                else if (!model.LogoFile.ContentType.StartsWith("image/", StringComparison.InvariantCulture))
                {
                    ModelState.AddModelError(nameof(model.LogoFile), "The uploaded logo file needs to be an image");
                }
                else
                {
                    var formFile = await model.LogoFile.Bufferize();
                    if (!FileTypeDetector.IsPicture(formFile.Buffer, formFile.FileName))
                    {
                        ModelState.AddModelError(nameof(model.LogoFile), "The uploaded logo file needs to be an image");
                    }
                    else
                    {
                        model.LogoFile = formFile;
                        // delete existing file
                        if (!string.IsNullOrEmpty(blob.LogoFileId))
                        {
                            await _fileService.RemoveFile(blob.LogoFileId, userId);
                        }
                        // add new image
                        try
                        {
                            var storedFile = await _fileService.AddFile(model.LogoFile, userId);
                            blob.LogoFileId = storedFile.Id;
                        }
                        catch (Exception e)
                        {
                            ModelState.AddModelError(nameof(model.LogoFile), $"Could not save logo: {e.Message}");
                        }
                    }
                }
            }
            else if (RemoveLogoFile && !string.IsNullOrEmpty(blob.LogoFileId))
            {
                await _fileService.RemoveFile(blob.LogoFileId, userId);
                blob.LogoFileId = null;
                needUpdate = true;
            }

            if (model.CssFile != null)
            {
                if (model.CssFile.Length > 1_000_000)
                {
                    ModelState.AddModelError(nameof(model.CssFile), "The uploaded file should be less than 1MB");
                }
                else if (!model.CssFile.ContentType.Equals("text/css", StringComparison.InvariantCulture))
                {
                    ModelState.AddModelError(nameof(model.CssFile), "The uploaded file needs to be a CSS file");
                }
                else if (!model.CssFile.FileName.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
                {
                    ModelState.AddModelError(nameof(model.CssFile), "The uploaded file needs to be a CSS file");
                }
                else
                {
                    // delete existing file
                    if (!string.IsNullOrEmpty(blob.CssFileId))
                    {
                        await _fileService.RemoveFile(blob.CssFileId, userId);
                    }
                    // add new file
                    try
                    {
                        var storedFile = await _fileService.AddFile(model.CssFile, userId);
                        blob.CssFileId = storedFile.Id;
                    }
                    catch (Exception e)
                    {
                        ModelState.AddModelError(nameof(model.CssFile), $"Could not save CSS file: {e.Message}");
                    }
                }
            }
            else if (RemoveCssFile && !string.IsNullOrEmpty(blob.CssFileId))
            {
                await _fileService.RemoveFile(blob.CssFileId, userId);
                blob.CssFileId = null;
                needUpdate = true;
            }

            if (CurrentStore.SetStoreBlob(blob))
            {
                needUpdate = true;
            }

            if (needUpdate)
            {
                await _Repo.UpdateStore(CurrentStore);

                TempData[WellKnownTempData.SuccessMessage] = "Store successfully updated";
            }

            return RedirectToAction(nameof(GeneralSettings), new
            {
                storeId = CurrentStore.Id
            });
        }

        [HttpPost("{storeId}/archive")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanModifyStoreSettings)]
        public async Task<IActionResult> ToggleArchive(string storeId)
        {
            CurrentStore.Archived = !CurrentStore.Archived;
            await _Repo.UpdateStore(CurrentStore);

            TempData[WellKnownTempData.SuccessMessage] = CurrentStore.Archived
                ? "The store has been archived and will no longer appear in the stores list by default."
                : "The store has been unarchived and will appear in the stores list by default again.";

            return RedirectToAction(nameof(GeneralSettings), new
            {
                storeId = CurrentStore.Id
            });
        }

        [HttpGet("{storeId}/delete")]
        public IActionResult DeleteStore(string storeId)
        {
            return View("Confirm", new ConfirmModel("Delete store", "The store will be permanently deleted. This action will also delete all invoices, apps and data associated with the store. Are you sure?", "Delete"));
        }

        [HttpPost("{storeId}/delete")]
        public async Task<IActionResult> DeleteStorePost(string storeId)
        {
            await _Repo.DeleteStore(CurrentStore.Id);
            TempData[WellKnownTempData.SuccessMessage] = "Store successfully deleted.";
            return RedirectToAction(nameof(UIHomeController.Index), "UIHome");
        }

        private IEnumerable<RateSourceInfo> GetSupportedExchanges()
        {
            return _RateFactory.RateProviderFactory.AvailableRateProviders
                .OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase);

        }

        private DerivationSchemeSettings ParseDerivationStrategy(string derivationScheme, BTCPayNetwork network)
        {
            var parser = new DerivationSchemeParser(network);
            var isOD = Regex.Match(derivationScheme, @"\(.*?\)");
            if (isOD.Success)
            {
                var derivationSchemeSettings = new DerivationSchemeSettings { Network = network };
                var result = parser.ParseOutputDescriptor(derivationScheme);
                derivationSchemeSettings.AccountOriginal = derivationScheme.Trim();
                derivationSchemeSettings.AccountDerivation = result.Item1;
                derivationSchemeSettings.AccountKeySettings = result.Item2?.Select((path, i) => new AccountKeySettings()
                {
                    RootFingerprint = path?.MasterFingerprint,
                    AccountKeyPath = path?.KeyPath,
                    AccountKey = result.Item1.GetExtPubKeys().ElementAt(i).GetWif(parser.Network)
                }).ToArray() ?? new AccountKeySettings[result.Item1.GetExtPubKeys().Count()];
                return derivationSchemeSettings;
            }

            var strategy = parser.Parse(derivationScheme);
            return new DerivationSchemeSettings(strategy, network);
        }

        [HttpGet("{storeId}/tokens")]
        public async Task<IActionResult> ListTokens()
        {
            var model = new TokensViewModel();
            var tokens = await _TokenRepository.GetTokensByStoreIdAsync(CurrentStore.Id);
            model.StoreNotConfigured = StoreNotConfigured;
            model.Tokens = tokens.Select(t => new TokenViewModel()
            {
                Label = t.Label,
                SIN = t.SIN,
                Id = t.Value
            }).ToArray();

            model.ApiKey = (await _TokenRepository.GetLegacyAPIKeys(CurrentStore.Id)).FirstOrDefault();
            if (model.ApiKey == null)
                model.EncodedApiKey = "*API Key*";
            else
                model.EncodedApiKey = Encoders.Base64.EncodeData(Encoders.ASCII.DecodeData(model.ApiKey));
            return View(model);
        }

        [HttpGet("{storeId}/tokens/{tokenId}/revoke")]
        public async Task<IActionResult> RevokeToken(string tokenId)
        {
            var token = await _TokenRepository.GetToken(tokenId);
            if (token == null || token.StoreId != CurrentStore.Id)
                return NotFound();
            return View("Confirm", new ConfirmModel("Revoke the token", $"The access token with the label <strong>{Html.Encode(token.Label)}</strong> will be revoked. Do you wish to continue?", "Revoke"));
        }

        [HttpPost("{storeId}/tokens/{tokenId}/revoke")]
        public async Task<IActionResult> RevokeTokenConfirm(string tokenId)
        {
            var token = await _TokenRepository.GetToken(tokenId);
            if (token == null ||
                token.StoreId != CurrentStore.Id ||
               !await _TokenRepository.DeleteToken(tokenId))
                TempData[WellKnownTempData.ErrorMessage] = "Failure to revoke this token.";
            else
                TempData[WellKnownTempData.SuccessMessage] = "Token revoked";
            return RedirectToAction(nameof(ListTokens), new { storeId = token?.StoreId });
        }

        [HttpGet("{storeId}/tokens/{tokenId}")]
        public async Task<IActionResult> ShowToken(string tokenId)
        {
            var token = await _TokenRepository.GetToken(tokenId);
            if (token == null || token.StoreId != CurrentStore.Id)
                return NotFound();
            return View(token);
        }

        [HttpGet("{storeId}/tokens/create")]
        public IActionResult CreateToken(string storeId)
        {
            var model = new CreateTokenViewModel();
            ViewBag.HidePublicKey = storeId == null;
            ViewBag.ShowStores = storeId == null;
            ViewBag.ShowMenu = storeId != null;
            model.StoreId = storeId;
            return View(model);
        }

        [HttpPost("{storeId}/tokens/create")]
        public async Task<IActionResult> CreateToken(string storeId, CreateTokenViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(nameof(CreateToken), model);
            }
            model.Label = model.Label ?? String.Empty;
            var userId = GetUserId();
            if (userId == null)
                return Challenge(AuthenticationSchemes.Cookie);
            var store = model.StoreId switch
            {
                null => CurrentStore,
                string id => await _Repo.FindStore(storeId, userId)
            };
            if (store == null)
                return Challenge(AuthenticationSchemes.Cookie);
            var tokenRequest = new TokenRequest()
            {
                Label = model.Label,
                Id = model.PublicKey == null ? null : NBitpayClient.Extensions.BitIdExtensions.GetBitIDSIN(new PubKey(model.PublicKey).Compress())
            };

            string? pairingCode = null;
            if (model.PublicKey == null)
            {
                tokenRequest.PairingCode = await _TokenRepository.CreatePairingCodeAsync();
                await _TokenRepository.UpdatePairingCode(new PairingCodeEntity()
                {
                    Id = tokenRequest.PairingCode,
                    Label = model.Label,
                });
                await _TokenRepository.PairWithStoreAsync(tokenRequest.PairingCode, store.Id);
                pairingCode = tokenRequest.PairingCode;
            }
            else
            {
                pairingCode = (await _TokenController.Tokens(tokenRequest)).Data[0].PairingCode;
            }

            GeneratedPairingCode = pairingCode;
            return RedirectToAction(nameof(RequestPairing), new
            {
                pairingCode,
                selectedStore = storeId
            });
        }

        [HttpGet("/api-tokens")]
        [AllowAnonymous]
        public async Task<IActionResult> CreateToken()
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
                return Challenge(AuthenticationSchemes.Cookie);
            var model = new CreateTokenViewModel();
            ViewBag.HidePublicKey = true;
            ViewBag.ShowStores = true;
            ViewBag.ShowMenu = false;
            var stores = (await _Repo.GetStoresByUserId(userId)).Where(data => data.HasPermission(userId, Policies.CanModifyStoreSettings)).ToArray();

            model.Stores = new SelectList(stores, nameof(CurrentStore.Id), nameof(CurrentStore.StoreName));
            if (!model.Stores.Any())
            {
                TempData[WellKnownTempData.ErrorMessage] = "You need to be owner of at least one store before pairing";
                return RedirectToAction(nameof(UIHomeController.Index), "UIHome");
            }
            return View(model);
        }

        [HttpPost("/api-tokens")]
        [AllowAnonymous]
        public Task<IActionResult> CreateToken2(CreateTokenViewModel model)
        {
            return CreateToken(model.StoreId, model);
        }

        [HttpPost("{storeId}/tokens/apikey")]
        public async Task<IActionResult> GenerateAPIKey(string storeId, string command = "")
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();
            if (command == "revoke")
            {
                await _TokenRepository.RevokeLegacyAPIKeys(CurrentStore.Id);
                TempData[WellKnownTempData.SuccessMessage] = "API Key revoked";
            }
            else
            {
                await _TokenRepository.GenerateLegacyAPIKey(CurrentStore.Id);
                TempData[WellKnownTempData.SuccessMessage] = "API Key re-generated";
            }

            return RedirectToAction(nameof(ListTokens), new
            {
                storeId
            });
        }

        [HttpGet("/api-access-request")]
        [AllowAnonymous]
        public async Task<IActionResult> RequestPairing(string pairingCode, string? selectedStore = null)
        {
            var userId = GetUserId();
            if (userId == null)
                return Challenge(AuthenticationSchemes.Cookie);

            if (pairingCode == null)
                return NotFound();

            if (selectedStore != null)
            {
                var store = await _Repo.FindStore(selectedStore, userId);
                if (store == null)
                    return NotFound();
                HttpContext.SetStoreData(store);
            }

            var pairing = await _TokenRepository.GetPairingAsync(pairingCode);
            if (pairing == null)
            {
                TempData[WellKnownTempData.ErrorMessage] = "Unknown pairing code";
                return RedirectToAction(nameof(UIHomeController.Index), "UIHome");
            }

            var stores = (await _Repo.GetStoresByUserId(userId)).Where(data => data.HasPermission(userId, Policies.CanModifyStoreSettings)).ToArray();
            return View(new PairingModel
            {
                Id = pairing.Id,
                Label = pairing.Label,
                SIN = pairing.SIN ?? "Server-Initiated Pairing",
                StoreId = selectedStore ?? stores.FirstOrDefault()?.Id,
                Stores = stores.Select(s => new PairingModel.StoreViewModel
                {
                    Id = s.Id,
                    Name = string.IsNullOrEmpty(s.StoreName) ? s.Id : s.StoreName
                }).ToArray()
            });
        }

        [HttpPost("/api-access-request")]
        public async Task<IActionResult> Pair(string pairingCode, string storeId)
        {
            if (pairingCode == null)
                return NotFound();
            var store = CurrentStore;
            var pairing = await _TokenRepository.GetPairingAsync(pairingCode);
            if (store == null || pairing == null)
                return NotFound();

            var pairingResult = await _TokenRepository.PairWithStoreAsync(pairingCode, store.Id);
            if (pairingResult == PairingResult.Complete || pairingResult == PairingResult.Partial)
            {
                var excludeFilter = store.GetStoreBlob().GetExcludedPaymentMethods();
                StoreNotConfigured = !store.GetSupportedPaymentMethods(_NetworkProvider)
                                          .Where(p => !excludeFilter.Match(p.PaymentId))
                                          .Any();
                TempData[WellKnownTempData.SuccessMessage] = "Pairing is successful";
                if (pairingResult == PairingResult.Partial)
                    TempData[WellKnownTempData.SuccessMessage] = "Server initiated pairing code: " + pairingCode;
                return RedirectToAction(nameof(ListTokens), new
                {
                    storeId = store.Id,
                    pairingCode = pairingCode
                });
            }
            else
            {
                TempData[WellKnownTempData.ErrorMessage] = $"Pairing failed ({pairingResult})";
                return RedirectToAction(nameof(ListTokens), new
                {
                    storeId = store.Id
                });
            }
        }

        private string? GetUserId()
        {
            if (User.Identity?.AuthenticationType != AuthenticationSchemes.Cookie)
                return null;
            return _UserManager.GetUserId(User);
        }
    }
}
