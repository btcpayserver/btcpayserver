#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Rating;
using BTCPayServer.Security.Bitpay;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
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
            WebhookSender webhookNotificationManager,
            IDataProtectionProvider dataProtector,
            IOptions<ExternalServicesOptions> externalServiceOptions)
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
            DataProtector = dataProtector.CreateProtector("ConfigProtector");
            WebhookNotificationManager = webhookNotificationManager;
            _EventAggregator = eventAggregator;
            _NetworkProvider = networkProvider;
            _ExplorerProvider = explorerProvider;
            _ServiceProvider = serviceProvider;
            _BtcpayServerOptions = btcpayServerOptions;
            _BTCPayEnv = btcpayEnv;
            _externalServiceOptions = externalServiceOptions;
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
        private readonly EventAggregator _EventAggregator;
        private readonly IOptions<ExternalServicesOptions> _externalServiceOptions;

        [TempData]
        public bool StoreNotConfigured
        {
            get; set;
        }

        [HttpGet]
        [Route("{storeId}/users")]
        public async Task<IActionResult> StoreUsers()
        {
            StoreUsersViewModel vm = new StoreUsersViewModel();
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
                Role = u.Role
            }).ToList();
        }

        public StoreData CurrentStore => HttpContext.GetStoreData();

        [HttpGet("{storeId}")]
        public async Task<IActionResult> Dashboard()
        {
            var store = CurrentStore;
            var storeBlob = store.GetStoreBlob();

            AddPaymentMethods(store, storeBlob,
                out var derivationSchemes, out var lightningNodes);

            var walletEnabled = derivationSchemes.Any(scheme => !string.IsNullOrEmpty(scheme.Value) && scheme.Enabled);
            var lightningEnabled = lightningNodes.Any(ln => !string.IsNullOrEmpty(ln.Address) && ln.Enabled);
            var vm = new StoreDashboardViewModel
            {
                WalletEnabled = walletEnabled,
                LightningEnabled = lightningEnabled,
                StoreId = CurrentStore.Id,
                StoreName = CurrentStore.StoreName,
                IsSetUp = walletEnabled || lightningEnabled
            };
            
            // Widget data
            if (vm.WalletEnabled || vm.LightningEnabled)
            {
                var userId = GetUserId();
                var apps = await _appService.GetAllApps(userId, false, store.Id);
                vm.Apps = apps
                    .Where(a => a.AppType == AppType.Crowdfund.ToString())
                    .Select(a =>
                    {
                        var appData = _appService.GetAppDataIfOwner(userId, a.Id, AppType.Crowdfund).Result;
                        appData.StoreData = store;
                        return appData;
                    })
                    .ToList();
            }
            
            return View("Dashboard", vm);
        }

        [HttpPost]
        [Route("{storeId}/users")]
        public async Task<IActionResult> StoreUsers(StoreUsersViewModel vm)
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
            if (!StoreRoles.AllRoles.Contains(vm.Role))
            {
                ModelState.AddModelError(nameof(vm.Role), "Invalid role");
                return View(vm);
            }
            if (!await _Repo.AddStoreUser(CurrentStore.Id, user.Id, vm.Role))
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
            return View("Confirm", new ConfirmModel("Remove store user", $"This action will prevent <strong>{user.Email}</strong> from accessing this store and its settings. Are you sure?", "Remove"));
        }

        [HttpPost("{storeId}/users/{userId}/delete")]
        public async Task<IActionResult> DeleteStoreUserPost(string storeId, string userId)
        {
            if(await _Repo.RemoveStoreUser(storeId, userId))
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
            vm.SetExchangeRates(exchanges, storeBlob.PreferredExchange ?? CoinGeckoRateProvider.CoinGeckoName);
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
            model.SetExchangeRates(exchanges, model.PreferredExchange);
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
                var existing =
                    storeBlob.PaymentMethodCriteria.SingleOrDefault(criteria =>
                        criteria.PaymentMethod == method.PaymentId);
                if (existing is null)
                {
                    return new PaymentMethodCriteriaViewModel()
                    {
                        PaymentMethod = method.PaymentId.ToString(),
                        Value = ""
                    };
                }
                else
                {
                    return new PaymentMethodCriteriaViewModel()
                    {
                        PaymentMethod = existing.PaymentMethod.ToString(),
                        Type = existing.Above
                            ? PaymentMethodCriteriaViewModel.CriteriaType.GreaterThan
                            : PaymentMethodCriteriaViewModel.CriteriaType.LessThan,
                        Value = existing.Value?.ToString() ?? ""
                    };
                }
            }).ToList();

            vm.RequiresRefundEmail = storeBlob.RequiresRefundEmail;
            vm.LazyPaymentMethods = storeBlob.LazyPaymentMethods;
            vm.RedirectAutomatically = storeBlob.RedirectAutomatically;
            vm.CustomCSS = storeBlob.CustomCSS;
            vm.CustomLogo = storeBlob.CustomLogo;
            vm.HtmlTitle = storeBlob.HtmlTitle;
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

        [HttpPost]
        [Route("{storeId}/checkout")]
        public async Task<IActionResult> CheckoutAppearance(CheckoutAppearanceViewModel model)
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
            blob.RequiresRefundEmail = model.RequiresRefundEmail;
            blob.LazyPaymentMethods = model.LazyPaymentMethods;
            blob.RedirectAutomatically = model.RedirectAutomatically;
            blob.CustomLogo = model.CustomLogo;
            blob.CustomCSS = model.CustomCSS;
            blob.HtmlTitle = string.IsNullOrWhiteSpace(model.HtmlTitle) ? null : model.HtmlTitle;
            blob.AutoDetectLanguage = model.AutoDetectLanguage;
            blob.DefaultLang = model.DefaultLang;

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
                            Collapsed = network is ElementsBTCPayNetwork elementsBTCPayNetwork && elementsBTCPayNetwork.NetworkCryptoCode != elementsBTCPayNetwork.CryptoCode && string.IsNullOrEmpty(value)
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
                NetworkFeeMode = storeBlob.NetworkFeeMode,
                AnyoneCanCreateInvoice = storeBlob.AnyoneCanInvoice,
                PaymentTolerance = storeBlob.PaymentTolerance,
                InvoiceExpiration = (int)storeBlob.InvoiceExpiration.TotalMinutes,
                DefaultCurrency = storeBlob.DefaultCurrency,
                BOLT11Expiration = (long)storeBlob.RefundBOLT11Expiration.TotalDays,
                CanDelete = _Repo.CanDeleteStores()
            };

            return View(vm);
        }

        [HttpPost("{storeId}/settings")]
        public async Task<IActionResult> GeneralSettings(GeneralSettingsViewModel model, string? command = null)
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
            blob.AnyoneCanInvoice = model.AnyoneCanCreateInvoice;
            blob.NetworkFeeMode = model.NetworkFeeMode;
            blob.PaymentTolerance = model.PaymentTolerance;
            blob.DefaultCurrency = model.DefaultCurrency;
            blob.InvoiceExpiration = TimeSpan.FromMinutes(model.InvoiceExpiration);
            blob.RefundBOLT11Expiration = TimeSpan.FromDays(model.BOLT11Expiration);
            
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

        private IEnumerable<AvailableRateProvider> GetSupportedExchanges()
        {
            var exchanges = _RateFactory.RateProviderFactory.GetSupportedExchanges();
            return exchanges
                .Where(r => !string.IsNullOrWhiteSpace(r.Name))
                .OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase);

        }

        private DerivationSchemeSettings ParseDerivationStrategy(string derivationScheme, BTCPayNetwork network)
        {
            var parser = new DerivationSchemeParser(network);
            try
            {
                var derivationSchemeSettings = new DerivationSchemeSettings();
                derivationSchemeSettings.Network = network;
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
            catch (Exception)
            {
                // ignored
            }

            return new DerivationSchemeSettings(parser.Parse(derivationScheme), network);
        }

        [HttpGet]
        [Route("{storeId}/Tokens")]
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
            return View("Confirm", new ConfirmModel("Revoke the token", $"The access token with the label <strong>{token.Label}</strong> will be revoked. Do you wish to continue?", "Revoke"));
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

        [HttpGet]
        [Route("{storeId}/tokens/{tokenId}")]
        public async Task<IActionResult> ShowToken(string tokenId)
        {
            var token = await _TokenRepository.GetToken(tokenId);
            if (token == null || token.StoreId != CurrentStore.Id)
                return NotFound();
            return View(token);
        }

        [HttpPost]
        [Route("{storeId}/Tokens/Create")]
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
            storeId = model.StoreId;
            var store = CurrentStore ?? await _Repo.FindStore(storeId, userId);
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
                await _TokenRepository.PairWithStoreAsync(tokenRequest.PairingCode, storeId);
                pairingCode = tokenRequest.PairingCode;
            }
            else
            {
                pairingCode = (await _TokenController.Tokens(tokenRequest)).Data[0].PairingCode;
            }

            GeneratedPairingCode = pairingCode;
            return RedirectToAction(nameof(RequestPairing), new
            {
                pairingCode = pairingCode,
                selectedStore = storeId
            });
        }

        public string? GeneratedPairingCode { get; set; }
        public WebhookSender WebhookNotificationManager { get; }
        public IDataProtector DataProtector { get; }

        [HttpGet]
        [Route("{storeId}/Tokens/Create")]
        public IActionResult CreateToken(string storeId)
        {
            var model = new CreateTokenViewModel();
            ViewBag.HidePublicKey = storeId == null;
            ViewBag.ShowStores = storeId == null;
            ViewBag.ShowMenu = storeId != null;
            model.StoreId = storeId;
            return View(model);
        }

        [HttpGet]
        [Route("/api-tokens")]
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
            var stores = await _Repo.GetStoresByUserId(userId);
            model.Stores = new SelectList(stores.Where(s => s.Role == StoreRoles.Owner), nameof(CurrentStore.Id), nameof(CurrentStore.StoreName));
            if (!model.Stores.Any())
            {
                TempData[WellKnownTempData.ErrorMessage] = "You need to be owner of at least one store before pairing";
                return RedirectToAction(nameof(UIHomeController.Index), "UIHome");
            }
            return View(model);
        }

        [HttpPost]
        [Route("/api-tokens")]
        [AllowAnonymous]
        public Task<IActionResult> CreateToken2(CreateTokenViewModel model)
        {
            return CreateToken(model.StoreId, model);
        }

        [HttpPost]
        [Route("{storeId}/tokens/apikey")]
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

        [HttpGet]
        [Route("/api-access-request")]
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
                ViewBag.ShowStores = false;
            }
            var pairing = await _TokenRepository.GetPairingAsync(pairingCode);
            if (pairing == null)
            {
                TempData[WellKnownTempData.ErrorMessage] = "Unknown pairing code";
                return RedirectToAction(nameof(UIHomeController.Index), "UIHome");
            }
            else
            {
                var stores = await _Repo.GetStoresByUserId(userId);
                return View(new PairingModel
                {
                    Id = pairing.Id,
                    Label = pairing.Label,
                    SIN = pairing.SIN ?? "Server-Initiated Pairing",
                    StoreId = selectedStore ?? stores.FirstOrDefault()?.Id,
                    Stores = stores.Where(u => u.Role == StoreRoles.Owner).Select(s => new PairingModel.StoreViewModel()
                    {
                        Id = s.Id,
                        Name = string.IsNullOrEmpty(s.StoreName) ? s.Id : s.StoreName
                    }).ToArray()
                });
            }
        }

        [HttpPost]
        [Route("/api-access-request")]
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
