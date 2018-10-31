using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Authentication;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments.Changelly;
using BTCPayServer.Rating;
using BTCPayServer.Security;
using BTCPayServer.Services;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Options;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Controllers
{
    [Route("stores")]
    [Authorize(AuthenticationSchemes = Policies.CookieAuthentication)]
    [Authorize(Policy = Policies.CanModifyStoreSettings.Key)]
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
            IOptions<MvcJsonOptions> mvcJsonOptions,
            IHostingEnvironment env, IHttpClientFactory httpClientFactory)
        {
            _RateFactory = rateFactory;
            _Repo = repo;
            _TokenRepository = tokenRepo;
            _UserManager = userManager;
            _LangService = langService;
            _changellyClientProvider = changellyClientProvider;
            MvcJsonOptions = mvcJsonOptions;
            _TokenController = tokenController;
            _WalletProvider = walletProvider;
            _Env = env;
            _httpClientFactory = httpClientFactory;
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
        IHostingEnvironment _Env;
        private IHttpClientFactory _httpClientFactory;

        [TempData]
        public string StatusMessage
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
            var users = await _Repo.GetStoreUsers(StoreData.Id);
            vm.StoreId = StoreData.Id;
            vm.Users = users.Select(u => new StoreUsersViewModel.StoreUserViewModel()
            {
                Email = u.Email,
                Id = u.Id,
                Role = u.Role
            }).ToList();
        }

        public StoreData StoreData
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
            if (!await _Repo.AddStoreUser(StoreData.Id, user.Id, vm.Role))
            {
                ModelState.AddModelError(nameof(vm.Email), "The user already has access to this store");
                return View(vm);
            }
            StatusMessage = "User added successfully";
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
                Description = $"Are you sure to remove access to remove access to {user.Email}?",
                Action = "Delete"
            });
        }

        [HttpPost]
        [Route("{storeId}/users/{userId}/delete")]
        public async Task<IActionResult> DeleteStoreUserPost(string storeId, string userId)
        {
            await _Repo.RemoveStoreUser(storeId, userId);
            StatusMessage = "User removed successfully";
            return RedirectToAction(nameof(StoreUsers), new { storeId = storeId, userId = userId });
        }

        [HttpGet]
        [Route("{storeId}/rates")]
        public IActionResult Rates()
        {
            var storeBlob = StoreData.GetStoreBlob();
            var vm = new RatesViewModel();
            vm.SetExchangeRates(GetSupportedExchanges(), storeBlob.PreferredExchange ?? CoinAverageRateProvider.CoinAverageName);
            vm.Spread = (double)(storeBlob.Spread * 100m);
            vm.Script = storeBlob.GetRateRules(_NetworkProvider).ToString();
            vm.DefaultScript = storeBlob.GetDefaultRateRules(_NetworkProvider).ToString();
            vm.AvailableExchanges = GetSupportedExchanges();
            vm.ShowScripting = storeBlob.RateScripting;
            return View(vm);
        }

        [HttpPost]
        [Route("{storeId}/rates")]
        public async Task<IActionResult> Rates(RatesViewModel model, string command = null)
        {
            model.SetExchangeRates(GetSupportedExchanges(), model.PreferredExchange);
            if (!ModelState.IsValid)
            {
                return View(model);
            }
            if (model.PreferredExchange != null)
                model.PreferredExchange = model.PreferredExchange.Trim().ToLowerInvariant();

            var blob = StoreData.GetStoreBlob();
            model.DefaultScript = blob.GetDefaultRateRules(_NetworkProvider).ToString();
            model.AvailableExchanges = GetSupportedExchanges();

            blob.PreferredExchange = model.PreferredExchange;
            blob.Spread = (decimal)model.Spread / 100.0m;

            if (!model.ShowScripting)
            {
                if (!GetSupportedExchanges().Select(c => c.Name).Contains(blob.PreferredExchange, StringComparer.OrdinalIgnoreCase))
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

                var fetchs = _RateFactory.FetchRates(pairs.ToHashSet(), rules);
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
                if (StoreData.SetStoreBlob(blob))
                {
                    await _Repo.UpdateStore(StoreData);
                    StatusMessage = "Rate settings updated";
                }
                return RedirectToAction(nameof(Rates), new
                {
                    storeId = StoreData.Id
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
                ButtonClass = "btn-primary"
            });
        }

        [HttpPost]
        [Route("{storeId}/rates/confirm")]
        public async Task<IActionResult> ShowRateRulesPost(bool scripting)
        {
            var blob = StoreData.GetStoreBlob();
            blob.RateScripting = scripting;
            blob.RateScript = blob.GetDefaultRateRules(_NetworkProvider).ToString();
            StoreData.SetStoreBlob(blob);
            await _Repo.UpdateStore(StoreData);
            StatusMessage = "Rate rules scripting activated";
            return RedirectToAction(nameof(Rates), new { storeId = StoreData.Id });
        }

        [HttpGet]
        [Route("{storeId}/checkout")]
        public IActionResult CheckoutExperience()
        {
            var storeBlob = StoreData.GetStoreBlob();
            var vm = new CheckoutExperienceViewModel();
            vm.SetCryptoCurrencies(_ExplorerProvider, StoreData.GetDefaultCrypto(_NetworkProvider));
            vm.SetLanguages(_LangService, storeBlob.DefaultLang);
            vm.LightningMaxValue = storeBlob.LightningMaxValue?.ToString() ?? "";
            vm.OnChainMinValue = storeBlob.OnChainMinValue?.ToString() ?? "";
            vm.RequiresRefundEmail = storeBlob.RequiresRefundEmail;
            vm.CustomCSS = storeBlob.CustomCSS?.AbsoluteUri;
            vm.CustomLogo = storeBlob.CustomLogo?.AbsoluteUri;
            vm.HtmlTitle = storeBlob.HtmlTitle;
            return View(vm);
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
            var blob = StoreData.GetStoreBlob();
            if (StoreData.GetDefaultCrypto(_NetworkProvider) != model.DefaultCryptoCurrency)
            {
                needUpdate = true;
                StoreData.SetDefaultCrypto(model.DefaultCryptoCurrency);
            }
            model.SetCryptoCurrencies(_ExplorerProvider, model.DefaultCryptoCurrency);
            model.SetLanguages(_LangService, model.DefaultLang);

            if (!ModelState.IsValid)
            {
                return View(model);
            }
            blob.DefaultLang = model.DefaultLang;
            blob.RequiresRefundEmail = model.RequiresRefundEmail;
            blob.LightningMaxValue = lightningMaxValue;
            blob.OnChainMinValue = onchainMinValue;
            blob.CustomLogo = string.IsNullOrWhiteSpace(model.CustomLogo) ? null : new Uri(model.CustomLogo, UriKind.Absolute);
            blob.CustomCSS = string.IsNullOrWhiteSpace(model.CustomCSS) ? null : new Uri(model.CustomCSS, UriKind.Absolute);
            blob.HtmlTitle = string.IsNullOrWhiteSpace(model.HtmlTitle) ? null : model.HtmlTitle;
            if (StoreData.SetStoreBlob(blob))
            {
                needUpdate = true;
            }
            if (needUpdate)
            {
                await _Repo.UpdateStore(StoreData);
                StatusMessage = "Store successfully updated";
            }

            return RedirectToAction(nameof(CheckoutExperience), new
            {
                storeId = StoreData.Id
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
            vm.NetworkFee = !storeBlob.NetworkFeeDisabled;
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
                .OfType<DerivationStrategy>()
                .ToDictionary(c => c.Network.CryptoCode);
            foreach (var network in _NetworkProvider.GetAll())
            {
                var strategy = derivationByCryptoCode.TryGet(network.CryptoCode);
                vm.DerivationSchemes.Add(new StoreViewModel.DerivationScheme()
                {
                    Crypto = network.CryptoCode,
                    Value = strategy?.DerivationStrategyBase?.ToString() ?? string.Empty,
                    WalletId = new WalletId(store.Id, network.CryptoCode),
                    Enabled = !excludeFilters.Match(new Payments.PaymentMethodId(network.CryptoCode, Payments.PaymentTypes.BTCLike))
                });
            }

            var lightningByCryptoCode = store
                                        .GetSupportedPaymentMethods(_NetworkProvider)
                                        .OfType<Payments.Lightning.LightningSupportedPaymentMethod>()
                                        .ToDictionary(c => c.CryptoCode);

            foreach (var network in _NetworkProvider.GetAll())
            {
                var lightning = lightningByCryptoCode.TryGet(network.CryptoCode);
                var paymentId = new Payments.PaymentMethodId(network.CryptoCode, Payments.PaymentTypes.LightningLike);
                vm.LightningNodes.Add(new StoreViewModel.LightningNode()
                {
                    CryptoCode = network.CryptoCode,
                    Address = lightning?.GetLightningUrl()?.BaseUri.AbsoluteUri ?? string.Empty,
                    Enabled = !excludeFilters.Match(paymentId)
                });
            }


            var changellyEnabled = storeBlob.ChangellySettings != null && storeBlob.ChangellySettings.Enabled;
            vm.ThirdPartyPaymentMethods.Add(new StoreViewModel.ThirdPartyPaymentMethod()
            {
                Enabled = changellyEnabled,
                Action = nameof(UpdateChangellySettings),
                Provider = "Changelly"
            });
        }

        [HttpPost]
        [Route("{storeId}")]
        public async Task<IActionResult> UpdateStore(StoreViewModel model, string command = null)
        {
            bool needUpdate = false;
            if (StoreData.SpeedPolicy != model.SpeedPolicy)
            {
                needUpdate = true;
                StoreData.SpeedPolicy = model.SpeedPolicy;
            }
            if (StoreData.StoreName != model.StoreName)
            {
                needUpdate = true;
                StoreData.StoreName = model.StoreName;
            }
            if (StoreData.StoreWebsite != model.StoreWebsite)
            {
                needUpdate = true;
                StoreData.StoreWebsite = model.StoreWebsite;
            }

            var blob = StoreData.GetStoreBlob();
            blob.AnyoneCanInvoice = model.AnyoneCanCreateInvoice;
            blob.NetworkFeeDisabled = !model.NetworkFee;
            blob.MonitoringExpiration = model.MonitoringExpiration;
            blob.InvoiceExpiration = model.InvoiceExpiration;
            blob.LightningDescriptionTemplate = model.LightningDescriptionTemplate ?? string.Empty;
            blob.PaymentTolerance = model.PaymentTolerance;

            if (StoreData.SetStoreBlob(blob))
            {
                needUpdate = true;
            }

            if (needUpdate)
            {
                await _Repo.UpdateStore(StoreData);
                StatusMessage = "Store successfully updated";
            }

            return RedirectToAction(nameof(UpdateStore), new
            {
                storeId = StoreData.Id
            });

        }

        [HttpGet]
        [Route("{storeId}/delete")]
        public IActionResult DeleteStore(string storeId)
        {
            return View("Confirm", new ConfirmModel()
            {
                Action = "Delete this store",
                Title = "Delete this store",
                Description = "This action is irreversible and will remove all information related to this store. (Invoices, Apps etc...)",
                ButtonClass = "btn-danger"
            });
        }

        [HttpPost]
        [Route("{storeId}/delete")]
        public async Task<IActionResult> DeleteStorePost(string storeId)
        {
            await _Repo.DeleteStore(StoreData.Id);
            StatusMessage = "Success: Store successfully deleted";
            return RedirectToAction(nameof(UserStoresController.ListStores), "UserStores");
        }

        private CoinAverageExchange[] GetSupportedExchanges()
        {
            return _RateFactory.RateProviderFactory.GetSupportedExchanges()
                    .Select(c => c.Value)
                    .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
        }

        private DerivationStrategy ParseDerivationStrategy(string derivationScheme, Script hint, BTCPayNetwork network)
        {
            var parser = new DerivationSchemeParser(network.NBitcoinNetwork);
            parser.HintScriptPubKey = hint;
            return new DerivationStrategy(parser.Parse(derivationScheme), network);
        }

        [HttpGet]
        [Route("{storeId}/Tokens")]
        public async Task<IActionResult> ListTokens()
        {
            var model = new TokensViewModel();
            var tokens = await _TokenRepository.GetTokensByStoreIdAsync(StoreData.Id);
            model.StatusMessage = StatusMessage;
            model.Tokens = tokens.Select(t => new TokenViewModel()
            {
                Facade = t.Facade,
                Label = t.Label,
                SIN = t.SIN,
                Id = t.Value
            }).ToArray();

            model.ApiKey = (await _TokenRepository.GetLegacyAPIKeys(StoreData.Id)).FirstOrDefault();
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
            if (token == null || token.StoreId != StoreData.Id)
                return NotFound();
            return View("Confirm", new ConfirmModel()
            {
                Action = "Revoke the token",
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
                token.StoreId != StoreData.Id ||
               !await _TokenRepository.DeleteToken(tokenId))
                StatusMessage = "Failure to revoke this token";
            else
                StatusMessage = "Token revoked";
            return RedirectToAction(nameof(ListTokens));
        }

        [HttpGet]
        [Route("{storeId}/tokens/{tokenId}")]
        public async Task<IActionResult> ShowToken(string tokenId)
        {
            var token = await _TokenRepository.GetToken(tokenId);
            if (token == null || token.StoreId != StoreData.Id)
                return NotFound();
            return View(token);
        }

        [HttpPost]
        [Route("/api-tokens")]
        [Route("{storeId}/Tokens/Create")]
        [AllowAnonymous]
        public async Task<IActionResult> CreateToken(CreateTokenViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }
            model.Label = model.Label ?? String.Empty;
            var userId = GetUserId();
            if (userId == null)
                return Challenge(Policies.CookieAuthentication);

            var store = StoreData;
            var storeId = StoreData?.Id;
            if (storeId == null)
            {
                storeId = model.StoreId;
                store = await _Repo.FindStore(storeId, userId);
                if (store == null)
                    return Challenge(Policies.CookieAuthentication);
            }

            if (!store.HasClaim(Policies.CanModifyStoreSettings.Key))
            {
                return Challenge(Policies.CookieAuthentication);
            }

            var tokenRequest = new TokenRequest()
            {
                Facade = model.Facade,
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
                    Facade = model.Facade,
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
        public IOptions<MvcJsonOptions> MvcJsonOptions { get; }

        [HttpGet]
        [Route("/api-tokens")]
        [Route("{storeId}/Tokens/Create")]
        [AllowAnonymous]
        public async Task<IActionResult> CreateToken()
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
                return Challenge(Policies.CookieAuthentication);
            var storeId = StoreData?.Id;
            if (StoreData != null)
            {
                if (!StoreData.HasClaim(Policies.CanModifyStoreSettings.Key))
                {
                    return Challenge(Policies.CookieAuthentication);
                }
            }
            var model = new CreateTokenViewModel();
            model.Facade = "merchant";
            ViewBag.HidePublicKey = storeId == null;
            ViewBag.ShowStores = storeId == null;
            ViewBag.ShowMenu = storeId != null;
            model.StoreId = storeId;
            if (storeId == null)
            {
                var stores = await _Repo.GetStoresByUserId(userId);
                model.Stores = new SelectList(stores.Where(s => s.HasClaim(Policies.CanModifyStoreSettings.Key)), nameof(StoreData.Id), nameof(StoreData.StoreName), storeId);
                if (model.Stores.Count() == 0)
                {
                    StatusMessage = "Error: You need to be owner of at least one store before pairing";
                    return RedirectToAction(nameof(UserStoresController.ListStores), "UserStores");
                }
            }
            return View(model);
        }

        [HttpPost]
        [Route("{storeId}/tokens/apikey")]
        public async Task<IActionResult> GenerateAPIKey()
        {
            var store = HttpContext.GetStoreData();
            if (store == null)
                return NotFound();
            await _TokenRepository.GenerateLegacyAPIKey(StoreData.Id);
            StatusMessage = "API Key re-generated";
            return RedirectToAction(nameof(ListTokens));
        }

        [HttpGet]
        [Route("/api-access-request")]
        [AllowAnonymous]
        public async Task<IActionResult> RequestPairing(string pairingCode, string selectedStore = null)
        {
            var userId = GetUserId();
            if (userId == null)
                return Challenge(Policies.CookieAuthentication);
            if (pairingCode == null)
                return NotFound();
            var pairing = await _TokenRepository.GetPairingAsync(pairingCode);
            if (pairing == null)
            {
                StatusMessage = "Unknown pairing code";
                return RedirectToAction(nameof(UserStoresController.ListStores), "UserStores");
            }
            else
            {
                var stores = await _Repo.GetStoresByUserId(userId);
                return View(new PairingModel()
                {
                    Id = pairing.Id,
                    Facade = pairing.Facade,
                    Label = pairing.Label,
                    SIN = pairing.SIN ?? "Server-Initiated Pairing",
                    SelectedStore = selectedStore ?? stores.FirstOrDefault()?.Id,
                    Stores = stores.Where(u => u.HasClaim(Policies.CanModifyStoreSettings.Key)).Select(s => new PairingModel.StoreViewModel()
                    {
                        Id = s.Id,
                        Name = string.IsNullOrEmpty(s.StoreName) ? s.Id : s.StoreName
                    }).ToArray()
                });
            }
        }

        [HttpPost]
        [Route("/api-access-request")]
        [AllowAnonymous]
        public async Task<IActionResult> Pair(string pairingCode, string selectedStore)
        {
            if (pairingCode == null)
                return NotFound();
            var userId = GetUserId();
            if (userId == null)
                return Challenge(Policies.CookieAuthentication);
            var store = await _Repo.FindStore(selectedStore, userId);
            var pairing = await _TokenRepository.GetPairingAsync(pairingCode);
            if (store == null || pairing == null)
                return NotFound();

            if (!store.HasClaim(Policies.CanModifyStoreSettings.Key))
            {
                return Challenge(Policies.CookieAuthentication);
            }

            var pairingResult = await _TokenRepository.PairWithStoreAsync(pairingCode, store.Id);
            if (pairingResult == PairingResult.Complete || pairingResult == PairingResult.Partial)
            {
                StatusMessage = "Pairing is successful";
                if (pairingResult == PairingResult.Partial)
                    StatusMessage = "Server initiated pairing code: " + pairingCode;
                return RedirectToAction(nameof(ListTokens), new
                {
                    storeId = store.Id
                });
            }
            else
            {
                StatusMessage = $"Pairing failed ({pairingResult})";
                return RedirectToAction(nameof(ListTokens), new
                {
                    storeId = store.Id
                });
            }
        }

        private string GetUserId()
        {
            if (User.Identity.AuthenticationType != Policies.CookieAuthentication)
                return null;
            return _UserManager.GetUserId(User);
        }



        // TODO: Need to have talk about how architect default currency implementation
        // For now we have also hardcoded USD for Store creation and then Invoice creation
        const string DEFAULT_CURRENCY = "USD";

        [Route("{storeId}/paybutton")]
        public IActionResult PayButton()
        {
            var store = StoreData;

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
                PayButtonImageUrl = appUrl + "img/paybutton/pay.png",
                StoreId = store.Id
            };
            return View(model);
        }

        [HttpPost]
        [Route("{storeId}/paybutton")]
        public async Task<IActionResult> PayButton(bool enableStore)
        {
            var blob = StoreData.GetStoreBlob();
            blob.AnyoneCanInvoice = enableStore;
            if (StoreData.SetStoreBlob(blob))
            {
                await _Repo.UpdateStore(StoreData);
                StatusMessage = "Store successfully updated";
            }

            return RedirectToAction(nameof(PayButton), new
            {
                storeId = StoreData.Id
            });

        }
    }
}
