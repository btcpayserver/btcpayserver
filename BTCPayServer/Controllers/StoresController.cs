using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Changelly;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Rating;
using BTCPayServer.Security;
using BTCPayServer.Security.Bitpay;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Controllers
{
    [Route("stores")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings.Key, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [AutoValidateAntiforgeryToken]
    public partial class StoresController : Controller
    {
        RateFetcher _RateFactory;
        public string CreatedStoreId { get; set; }
        public StoresController(
            IServiceProvider serviceProvider,
            BTCPayServerOptions btcpayServerOptions,
            BTCPayServerEnvironment btcpayEnv,
            StoreRepository repo,
            TokenRepository tokenRepo,
            UserManager<ApplicationUser> userManager,
            AccessTokenController tokenController,
            BTCPayWalletProvider walletProvider,
            BTCPayNetworkProvider networkProvider,
            RateFetcher rateFactory,
            ExplorerClientProvider explorerProvider,
            IFeeProviderFactory feeRateProvider,
            LanguageService langService,
            ChangellyClientProvider changellyClientProvider,
            IWebHostEnvironment env, IHttpClientFactory httpClientFactory,
            PaymentMethodHandlerDictionary paymentMethodHandlerDictionary,
            SettingsRepository settingsRepository,
            IAuthorizationService authorizationService,
            EventAggregator eventAggregator,
            CssThemeManager cssThemeManager)
        {
            _RateFactory = rateFactory;
            _Repo = repo;
            _TokenRepository = tokenRepo;
            _UserManager = userManager;
            _LangService = langService;
            _changellyClientProvider = changellyClientProvider;
            _TokenController = tokenController;
            _WalletProvider = walletProvider;
            _Env = env;
            _httpClientFactory = httpClientFactory;
            _paymentMethodHandlerDictionary = paymentMethodHandlerDictionary;
            _settingsRepository = settingsRepository;
            _authorizationService = authorizationService;
            _CssThemeManager = cssThemeManager;
            _EventAggregator = eventAggregator;
            _NetworkProvider = networkProvider;
            _ExplorerProvider = explorerProvider;
            _FeeRateProvider = feeRateProvider;
            _ServiceProvider = serviceProvider;
            _BtcpayServerOptions = btcpayServerOptions;
            _BTCPayEnv = btcpayEnv;
        }
        BTCPayServerOptions _BtcpayServerOptions;
        BTCPayServerEnvironment _BTCPayEnv;
        IServiceProvider _ServiceProvider;
        BTCPayNetworkProvider _NetworkProvider;
        private ExplorerClientProvider _ExplorerProvider;
        private IFeeProviderFactory _FeeRateProvider;
        BTCPayWalletProvider _WalletProvider;
        AccessTokenController _TokenController;
        StoreRepository _Repo;
        TokenRepository _TokenRepository;
        UserManager<ApplicationUser> _UserManager;
        private LanguageService _LangService;
        private readonly ChangellyClientProvider _changellyClientProvider;
        IWebHostEnvironment _Env;
        private IHttpClientFactory _httpClientFactory;
        private readonly PaymentMethodHandlerDictionary _paymentMethodHandlerDictionary;
        private readonly SettingsRepository _settingsRepository;
        private readonly IAuthorizationService _authorizationService;
        private readonly CssThemeManager _CssThemeManager;
        private readonly EventAggregator _EventAggregator;

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

        public StoreData CurrentStore
        {
            get
            {
                return this.HttpContext.GetStoreData();
            }
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
            TempData[WellKnownTempData.SuccessMessage] = "User added successfully";
            return RedirectToAction(nameof(StoreUsers));
        }

        [HttpGet]
        [Route("{storeId}/users/{userId}/delete")]
        public async Task<IActionResult> DeleteStoreUser(string userId)
        {
            StoreUsersViewModel vm = new StoreUsersViewModel();
            var user = await _UserManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();
            return View("Confirm", new ConfirmModel()
            {
                Title = $"Remove store user",
                Description = $"Are you sure you want to remove store access for {user.Email}?",
                Action = "Delete"
            });
        }

        [HttpPost]
        [Route("{storeId}/users/{userId}/delete")]
        public async Task<IActionResult> DeleteStoreUserPost(string storeId, string userId)
        {
            await _Repo.RemoveStoreUser(storeId, userId);
            TempData[WellKnownTempData.SuccessMessage] = "User removed successfully";
            return RedirectToAction(nameof(StoreUsers), new { storeId = storeId, userId = userId });
        }

        [HttpGet]
        [Route("{storeId}/rates")]
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

        [HttpPost]
        [Route("{storeId}/rates")]
        public async Task<IActionResult> Rates(RatesViewModel model, string command = null, string storeId = null, CancellationToken cancellationToken = default)
        {
            if (command == "scripting-on")
            {
                return RedirectToAction(nameof(ShowRateRules), new {scripting = true,storeId = model.StoreId});
            }else if (command == "scripting-off")
            {
                return RedirectToAction(nameof(ShowRateRules), new {scripting = false, storeId = model.StoreId});
            }

            var exchanges = GetSupportedExchanges();
            model.SetExchangeRates(exchanges, model.PreferredExchange);
            model.StoreId = storeId ?? model.StoreId;
            CurrencyPair[] currencyPairs = null;
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
            RateRules rules = null;
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

        [HttpGet]
        [Route("{storeId}/rates/confirm")]
        public IActionResult ShowRateRules(bool scripting)
        {
            return View("Confirm", new ConfirmModel()
            {
                Action = "Continue",
                Title = "Rate rule scripting",
                Description = scripting ?
                                "This action will modify your current rate sources. Are you sure to turn on rate rules scripting? (Advanced users)"
                                : "This action will delete your rate script. Are you sure to turn off rate rules scripting?",
                ButtonClass = scripting ? "btn-primary" : "btn-danger"
            });
        }

        [HttpPost]
        [Route("{storeId}/rates/confirm")]
        public async Task<IActionResult> ShowRateRulesPost(bool scripting)
        {
            var blob = CurrentStore.GetStoreBlob();
            blob.RateScripting = scripting;
            blob.RateScript = blob.GetDefaultRateRules(_NetworkProvider).ToString();
            CurrentStore.SetStoreBlob(blob);
            await _Repo.UpdateStore(CurrentStore);
            TempData[WellKnownTempData.SuccessMessage] = "Rate rules scripting activated";
            return RedirectToAction(nameof(Rates), new { storeId = CurrentStore.Id });
        }

        [HttpGet]
        [Route("{storeId}/checkout")]
        public IActionResult CheckoutExperience()
        {
            var storeBlob = CurrentStore.GetStoreBlob();
            var vm = new CheckoutExperienceViewModel();
            SetCryptoCurrencies(vm, CurrentStore);
            vm.CustomCSS = storeBlob.CustomCSS;
            vm.CustomLogo = storeBlob.CustomLogo;
            vm.HtmlTitle = storeBlob.HtmlTitle;
            vm.SetLanguages(_LangService, storeBlob.DefaultLang);
            vm.RequiresRefundEmail = storeBlob.RequiresRefundEmail;
            vm.ShowRecommendedFee = storeBlob.ShowRecommendedFee;
            vm.RecommendedFeeBlockTarget = storeBlob.RecommendedFeeBlockTarget;
            vm.OnChainMinValue = storeBlob.OnChainMinValue?.ToString() ?? "";
            vm.LightningMaxValue = storeBlob.LightningMaxValue?.ToString() ?? "";
            vm.LightningAmountInSatoshi = storeBlob.LightningAmountInSatoshi;
            vm.RedirectAutomatically = storeBlob.RedirectAutomatically;
            return View(vm);
        }
        void SetCryptoCurrencies(CheckoutExperienceViewModel vm, Data.StoreData storeData)
        {
            var choices = storeData.GetEnabledPaymentIds(_NetworkProvider)
                                      .Select(o => new CheckoutExperienceViewModel.Format() { Name = o.ToPrettyString(), Value = o.ToString(), PaymentId = o }).ToArray();

            var defaultPaymentId = storeData.GetDefaultPaymentId(_NetworkProvider);
            var chosen = choices.FirstOrDefault(c => c.PaymentId == defaultPaymentId);
            vm.CryptoCurrencies = new SelectList(choices, nameof(chosen.Value), nameof(chosen.Name), chosen?.Value);
            vm.DefaultPaymentMethod = chosen?.Value;
        }

        [HttpPost]
        [Route("{storeId}/checkout")]
        public async Task<IActionResult> CheckoutExperience(CheckoutExperienceViewModel model)
        {
            CurrencyValue lightningMaxValue = null;
            if (!string.IsNullOrWhiteSpace(model.LightningMaxValue))
            {
                if (!CurrencyValue.TryParse(model.LightningMaxValue, out lightningMaxValue))
                {
                    ModelState.AddModelError(nameof(model.LightningMaxValue), "Invalid lightning max value");
                }
            }

            CurrencyValue onchainMinValue = null;
            if (!string.IsNullOrWhiteSpace(model.OnChainMinValue))
            {
                if (!CurrencyValue.TryParse(model.OnChainMinValue, out onchainMinValue))
                {
                    ModelState.AddModelError(nameof(model.OnChainMinValue), "Invalid on chain min value");
                }
            }
            bool needUpdate = false;
            var blob = CurrentStore.GetStoreBlob();
            var defaultPaymentMethodId = model.DefaultPaymentMethod == null ? null : PaymentMethodId.Parse(model.DefaultPaymentMethod);
            if (CurrentStore.GetDefaultPaymentId(_NetworkProvider) != defaultPaymentMethodId)
            {
                needUpdate = true;
                CurrentStore.SetDefaultPaymentId(defaultPaymentMethodId);
            }
            SetCryptoCurrencies(model, CurrentStore);
            model.SetLanguages(_LangService, model.DefaultLang);

            if (!ModelState.IsValid)
            {
                return View(model);
            }
            blob.CustomLogo = model.CustomLogo;
            blob.CustomCSS = model.CustomCSS;
            blob.HtmlTitle = string.IsNullOrWhiteSpace(model.HtmlTitle) ? null : model.HtmlTitle;
            blob.DefaultLang = model.DefaultLang;
            blob.RequiresRefundEmail = model.RequiresRefundEmail;
            blob.ShowRecommendedFee = model.ShowRecommendedFee;
            blob.RecommendedFeeBlockTarget = model.RecommendedFeeBlockTarget;
            blob.OnChainMinValue = onchainMinValue;
            blob.LightningMaxValue = lightningMaxValue;
            blob.LightningAmountInSatoshi = model.LightningAmountInSatoshi;
            blob.RedirectAutomatically = model.RedirectAutomatically;
            if (CurrentStore.SetStoreBlob(blob))
            {
                needUpdate = true;
            }
            if (needUpdate)
            {
                await _Repo.UpdateStore(CurrentStore);
                TempData[WellKnownTempData.SuccessMessage] = "Store successfully updated";
            }

            return RedirectToAction(nameof(CheckoutExperience), new
            {
                storeId = CurrentStore.Id
            });
        }

        [HttpGet]
        [Route("{storeId}")]
        public IActionResult UpdateStore()
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();

            var storeBlob = store.GetStoreBlob();
            var vm = new StoreViewModel();
            vm.Id = store.Id;
            vm.StoreName = store.StoreName;
            vm.StoreWebsite = store.StoreWebsite;
            vm.NetworkFeeMode = storeBlob.NetworkFeeMode;
            vm.AnyoneCanCreateInvoice = storeBlob.AnyoneCanInvoice;
            vm.SpeedPolicy = store.SpeedPolicy;
            vm.CanDelete = _Repo.CanDeleteStores();
            AddPaymentMethods(store, storeBlob, vm);
            vm.MonitoringExpiration = storeBlob.MonitoringExpiration;
            vm.InvoiceExpiration = storeBlob.InvoiceExpiration;
            vm.LightningDescriptionTemplate = storeBlob.LightningDescriptionTemplate;
            vm.PaymentTolerance = storeBlob.PaymentTolerance;
            return View(vm);
        }


        private void AddPaymentMethods(StoreData store, StoreBlob storeBlob, StoreViewModel vm)
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
                .ToDictionary(c => c.CryptoCode.ToUpperInvariant());

            foreach (var paymentMethodId in _paymentMethodHandlerDictionary.Distinct().SelectMany(handler => handler.GetSupportedPaymentMethods()))
            {
                 switch (paymentMethodId.PaymentType)
                {
                    case BitcoinPaymentType _:
                        var strategy = derivationByCryptoCode.TryGet(paymentMethodId.CryptoCode);
                        var network = _NetworkProvider.GetNetwork<BTCPayNetwork>(paymentMethodId.CryptoCode);
                        var value = strategy?.ToPrettyString() ?? string.Empty;
                        
                        vm.DerivationSchemes.Add(new StoreViewModel.DerivationScheme()
                        {
                            Crypto = paymentMethodId.CryptoCode,
                            WalletSupported = network.WalletSupported,
                            Value = value,
                            WalletId = new WalletId(store.Id, paymentMethodId.CryptoCode),
                            Enabled = !excludeFilters.Match(paymentMethodId) && strategy != null,
                            Collapsed = network is ElementsBTCPayNetwork elementsBTCPayNetwork && elementsBTCPayNetwork.NetworkCryptoCode != elementsBTCPayNetwork.CryptoCode && string.IsNullOrEmpty(value)
                        });
                        break;
                    case LightningPaymentType _:
                        var lightning = lightningByCryptoCode.TryGet(paymentMethodId.CryptoCode);
                        vm.LightningNodes.Add(new StoreViewModel.LightningNode()
                        {
                            CryptoCode = paymentMethodId.CryptoCode,
                            Address = lightning?.GetLightningUrl()?.BaseUri.AbsoluteUri ?? string.Empty,
                            Enabled = !excludeFilters.Match(paymentMethodId) && lightning?.GetLightningUrl() != null
                        });
                        break;
                }   
            }

            var changellyEnabled = storeBlob.ChangellySettings != null && storeBlob.ChangellySettings.Enabled;
            vm.ThirdPartyPaymentMethods.Add(new StoreViewModel.AdditionalPaymentMethod()
            {
                Enabled = changellyEnabled,
                Action = nameof(UpdateChangellySettings),
                Provider = "Changelly"
            });

            var coinSwitchEnabled = storeBlob.CoinSwitchSettings != null && storeBlob.CoinSwitchSettings.Enabled;
            vm.ThirdPartyPaymentMethods.Add(new StoreViewModel.AdditionalPaymentMethod()
            {
                Enabled = coinSwitchEnabled,
                Action = nameof(UpdateCoinSwitchSettings),
                Provider = "CoinSwitch"
            });
        }

        [HttpPost]
        [Route("{storeId}")]
        public async Task<IActionResult> UpdateStore(StoreViewModel model, string command = null)
        {
            bool needUpdate = false;
            if (CurrentStore.SpeedPolicy != model.SpeedPolicy)
            {
                needUpdate = true;
                CurrentStore.SpeedPolicy = model.SpeedPolicy;
            }
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
            blob.MonitoringExpiration = model.MonitoringExpiration;
            blob.InvoiceExpiration = model.InvoiceExpiration;
            blob.LightningDescriptionTemplate = model.LightningDescriptionTemplate ?? string.Empty;
            blob.PaymentTolerance = model.PaymentTolerance;

            if (CurrentStore.SetStoreBlob(blob))
            {
                needUpdate = true;
            }

            if (needUpdate)
            {
                await _Repo.UpdateStore(CurrentStore);
                TempData[WellKnownTempData.SuccessMessage] = "Store successfully updated";
            }

            return RedirectToAction(nameof(UpdateStore), new
            {
                storeId = CurrentStore.Id
            });

        }

        [HttpGet]
        [Route("{storeId}/delete")]
        public IActionResult DeleteStore(string storeId)
        {
            return View("Confirm", new ConfirmModel()
            {
                Action = "Delete",
                Title = "Delete this store",
                Description = "This action is irreversible and will remove all information related to this store. (Invoices, Apps etc...)",
                ButtonClass = "btn-danger"
            });
        }

        [HttpPost]
        [Route("{storeId}/delete")]
        public async Task<IActionResult> DeleteStorePost(string storeId)
        {
            await _Repo.DeleteStore(CurrentStore.Id);
            TempData[WellKnownTempData.SuccessMessage] = "Store successfully deleted";
            return RedirectToAction(nameof(UserStoresController.ListStores), "UserStores");
        }

        private IEnumerable<AvailableRateProvider> GetSupportedExchanges()
        {
            var exchanges = _RateFactory.RateProviderFactory.GetSupportedExchanges();
            return exchanges
                .Where(r => !string.IsNullOrWhiteSpace(r.Name))
                .OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase);

        }

        private DerivationSchemeSettings ParseDerivationStrategy(string derivationScheme, Script hint, BTCPayNetwork network)
        {
            var parser = new DerivationSchemeParser(network);
            parser.HintScriptPubKey = hint;
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

        [HttpGet]
        [Route("{storeId}/tokens/{tokenId}/revoke")]
        public async Task<IActionResult> RevokeToken(string tokenId)
        {
            var token = await _TokenRepository.GetToken(tokenId);
            if (token == null || token.StoreId != CurrentStore.Id)
                return NotFound();
            return View("Confirm", new ConfirmModel()
            {
                Action = "Revoke",
                Title = "Revoke the token",
                Description = $"The access token with the label \"{token.Label}\" will be revoked, do you wish to continue?",
                ButtonClass = "btn-danger"
            });
        }
        [HttpPost]
        [Route("{storeId}/tokens/{tokenId}/revoke")]
        public async Task<IActionResult> RevokeTokenConfirm(string tokenId)
        {
            var token = await _TokenRepository.GetToken(tokenId);
            if (token == null ||
                token.StoreId != CurrentStore.Id ||
               !await _TokenRepository.DeleteToken(tokenId))
                TempData[WellKnownTempData.ErrorMessage] = "Failure to revoke this token";
            else
                TempData[WellKnownTempData.SuccessMessage] = "Token revoked";
            return RedirectToAction(nameof(ListTokens));
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
                Id = model.PublicKey == null ? null : NBitpayClient.Extensions.BitIdExtensions.GetBitIDSIN(new PubKey(model.PublicKey))
            };

            string pairingCode = null;
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
                pairingCode = ((DataWrapper<List<PairingCodeResponse>>)await _TokenController.Tokens(tokenRequest)).Data[0].PairingCode;
            }

            GeneratedPairingCode = pairingCode;
            return RedirectToAction(nameof(RequestPairing), new
            {
                pairingCode = pairingCode,
                selectedStore = storeId
            });
        }

        public string GeneratedPairingCode { get; set; }

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
            var storeId = CurrentStore?.Id;
            var model = new CreateTokenViewModel();
            ViewBag.HidePublicKey = true;
            ViewBag.ShowStores = true;
            ViewBag.ShowMenu = false;
            var stores = await _Repo.GetStoresByUserId(userId);
            model.Stores = new SelectList(stores.Where(s => s.Role == StoreRoles.Owner), nameof(CurrentStore.Id), nameof(CurrentStore.StoreName));
            if (!model.Stores.Any())
            {
                TempData[WellKnownTempData.ErrorMessage] = "You need to be owner of at least one store before pairing";
                return RedirectToAction(nameof(UserStoresController.ListStores), "UserStores");
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
        public async Task<IActionResult> GenerateAPIKey(string storeId)
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();
            await _TokenRepository.GenerateLegacyAPIKey(CurrentStore.Id);
            TempData[WellKnownTempData.SuccessMessage] = "API Key re-generated";
            return RedirectToAction(nameof(ListTokens), new
            {
                storeId
            });
        }

        [HttpGet]
        [Route("/api-access-request")]
        [AllowAnonymous]
        public async Task<IActionResult> RequestPairing(string pairingCode, string selectedStore = null)
        {
            var userId = GetUserId();
            if (userId == null)
                return Challenge(AuthenticationSchemes.Cookie);
            if (pairingCode == null)
                return NotFound();
            var pairing = await _TokenRepository.GetPairingAsync(pairingCode);
            if (pairing == null)
            {
                TempData[WellKnownTempData.ErrorMessage] = "Unknown pairing code";
                return RedirectToAction(nameof(UserStoresController.ListStores), "UserStores");
            }
            else
            {
                var stores = await _Repo.GetStoresByUserId(userId);
                return View(new PairingModel()
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

        private string GetUserId()
        {
            if (User.Identity.AuthenticationType != AuthenticationSchemes.Cookie)
                return null;
            return _UserManager.GetUserId(User);
        }



        // TODO: Need to have talk about how architect default currency implementation
        // For now we have also hardcoded USD for Store creation and then Invoice creation
        const string DEFAULT_CURRENCY = "USD";

        [Route("{storeId}/paybutton")]
        public IActionResult PayButton()
        {
            var store = CurrentStore;

            var storeBlob = store.GetStoreBlob();
            if (!storeBlob.AnyoneCanInvoice)
            {
                return View("PayButtonEnable", null);
            }

            var appUrl = HttpContext.Request.GetAbsoluteRoot().WithTrailingSlash();
            var model = new PayButtonViewModel
            {
                Price = 10,
                Currency = DEFAULT_CURRENCY,
                ButtonSize = 2,
                UrlRoot = appUrl,
                PayButtonImageUrl = appUrl + "img/paybutton/pay.svg",
                StoreId = store.Id,
                ButtonType = 0,
                Min = 1,
                Max = 20,
                Step = 1
            };
            return View(model);
        }

        [HttpPost]
        [Route("{storeId}/paybutton")]
        public async Task<IActionResult> PayButton(bool enableStore)
        {
            var blob = CurrentStore.GetStoreBlob();
            blob.AnyoneCanInvoice = enableStore;
            if (CurrentStore.SetStoreBlob(blob))
            {
                await _Repo.UpdateStore(CurrentStore);
                TempData[WellKnownTempData.SuccessMessage] = "Store successfully updated";
            }

            return RedirectToAction(nameof(PayButton), new
            {
                storeId = CurrentStore.Id
            });

        }
    }
}
