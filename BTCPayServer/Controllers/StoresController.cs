using BTCPayServer.Authentication;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Options;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBXplorer.DerivationStrategy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.Controllers
{
    [Route("stores")]
    [Authorize(AuthenticationSchemes = "Identity.Application")]
    [Authorize(Policy = StorePolicies.OwnStore)]
    [AutoValidateAntiforgeryToken]
    public partial class StoresController : Controller
    {
        public string CreatedStoreId { get; set; }
        public StoresController(
            NBXplorerDashboard dashboard,
            IServiceProvider serviceProvider,
            BTCPayServerOptions btcpayServerOptions,
            BTCPayServerEnvironment btcpayEnv,
            IOptions<MvcJsonOptions> mvcJsonOptions,
            StoreRepository repo,
            TokenRepository tokenRepo,
            UserManager<ApplicationUser> userManager,
            AccessTokenController tokenController,
            BTCPayWalletProvider walletProvider,
            BTCPayNetworkProvider networkProvider,
            ExplorerClientProvider explorerProvider,
            IFeeProviderFactory feeRateProvider,
            IHostingEnvironment env)
        {
            _Dashboard = dashboard;
            _Repo = repo;
            _TokenRepository = tokenRepo;
            _UserManager = userManager;
            _TokenController = tokenController;
            _WalletProvider = walletProvider;
            _Env = env;
            _NetworkProvider = networkProvider;
            _ExplorerProvider = explorerProvider;
            _MvcJsonOptions = mvcJsonOptions.Value;
            _FeeRateProvider = feeRateProvider;
            _ServiceProvider = serviceProvider;
            _BtcpayServerOptions = btcpayServerOptions;
            _BTCPayEnv = btcpayEnv;
        }
        NBXplorerDashboard _Dashboard;
        BTCPayServerOptions _BtcpayServerOptions;
        BTCPayServerEnvironment _BTCPayEnv;
        IServiceProvider _ServiceProvider;
        BTCPayNetworkProvider _NetworkProvider;
        private ExplorerClientProvider _ExplorerProvider;
        private MvcJsonOptions _MvcJsonOptions;
        private IFeeProviderFactory _FeeRateProvider;
        BTCPayWalletProvider _WalletProvider;
        AccessTokenController _TokenController;
        StoreRepository _Repo;
        TokenRepository _TokenRepository;
        UserManager<ApplicationUser> _UserManager;
        IHostingEnvironment _Env;

        [TempData]
        public string StatusMessage
        {
            get; set;
        }

        [HttpGet]
        [Route("{storeId}/wallet/{cryptoCode}")]
        public async Task<IActionResult> Wallet(string storeId, string cryptoCode)
        {
            var store = await _Repo.FindStore(storeId, GetUserId());
            if (store == null)
                return NotFound();
            WalletModel model = new WalletModel();
            model.ServerUrl = GetStoreUrl(storeId);
            model.CryptoCurrency = cryptoCode;
            return View(model);
        }

        private string GetStoreUrl(string storeId)
        {
            return HttpContext.Request.GetAbsoluteRoot() + "/stores/" + storeId + "/";
        }        

        [HttpGet]
        [Route("{storeId}/users")]
        public async Task<IActionResult> StoreUsers(string storeId)
        {
            StoreUsersViewModel vm = new StoreUsersViewModel();
            await FillUsers(storeId, vm);
            return View(vm);
        }

        private async Task FillUsers(string storeId, StoreUsersViewModel vm)
        {
            var users = await _Repo.GetStoreUsers(storeId);
            vm.StoreId = storeId;
            vm.Users = users.Select(u => new StoreUsersViewModel.StoreUserViewModel()
            {
                Email = u.Email,
                Id = u.Id,
                Role = u.Role
            }).ToList();
        }

        [HttpPost]
        [Route("{storeId}/users")]
        public async Task<IActionResult> StoreUsers(string storeId, StoreUsersViewModel vm)
        {
            await FillUsers(storeId, vm);
            if(!ModelState.IsValid)
            {
                return View(vm);
            }
            var user = await _UserManager.FindByEmailAsync(vm.Email);
            if(user == null)
            {
                ModelState.AddModelError(nameof(vm.Email), "User not found");
                return View(vm);
            }
            if(!StoreRoles.AllRoles.Contains(vm.Role))
            {
                ModelState.AddModelError(nameof(vm.Role), "Invalid role");
                return View(vm);
            }
            if(!await _Repo.AddStoreUser(storeId, user.Id, vm.Role))
            {
                ModelState.AddModelError(nameof(vm.Email), "The user already has access to this store");
                return View(vm);
            }
            StatusMessage = "User added successfully";
            return RedirectToAction(nameof(StoreUsers));
        }

        [HttpGet]
        [Route("{storeId}/users/{userId}/delete")]
        public async Task<IActionResult> DeleteStoreUser(string storeId, string userId)
        {
            StoreUsersViewModel vm = new StoreUsersViewModel();
            var store = await _Repo.FindStore(storeId, userId);
            if (store == null)
                return NotFound();
            var user = await _UserManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();
            return View("Confirm", new ConfirmModel()
            {
                Title = $"Remove store user",
                Description = $"Are you sure to remove access to remove {store.Role} access to {user.Email}?",
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
        [Route("{storeId}")]
        public async Task<IActionResult> UpdateStore(string storeId)
        {
            var store = await _Repo.FindStore(storeId, GetUserId());
            if (store == null)
                return NotFound();

            var storeBlob = store.GetStoreBlob();
            var vm = new StoreViewModel();
            vm.Id = store.Id;
            vm.StoreName = store.StoreName;
            vm.SetCryptoCurrencies(_ExplorerProvider, store.GetDefaultCrypto());
            vm.StoreWebsite = store.StoreWebsite;
            vm.NetworkFee = !storeBlob.NetworkFeeDisabled;
            vm.SpeedPolicy = store.SpeedPolicy;
            AddPaymentMethods(store, vm);
            vm.StatusMessage = StatusMessage;
            vm.MonitoringExpiration = storeBlob.MonitoringExpiration;
            vm.InvoiceExpiration = storeBlob.InvoiceExpiration;
            vm.RateMultiplier = (double)storeBlob.GetRateMultiplier();
            vm.PreferredExchange = storeBlob.PreferredExchange.IsCoinAverage() ? "coinaverage" : storeBlob.PreferredExchange;
            vm.AllowCoinConversion = storeBlob.AllowCoinConversion;
            return View(vm);
        }

        private void AddPaymentMethods(StoreData store, StoreViewModel vm)
        {
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
                    Value = strategy?.DerivationStrategyBase?.ToString() ?? string.Empty
                });
            }

            var lightningByCryptoCode = store
                                        .GetSupportedPaymentMethods(_NetworkProvider)
                                        .OfType<Payments.Lightning.LightningSupportedPaymentMethod>()
                                        .ToDictionary(c => c.CryptoCode);

            foreach (var network in _NetworkProvider.GetAll())
            {
                var lightning = lightningByCryptoCode.TryGet(network.CryptoCode);
                vm.LightningNodes.Add(new StoreViewModel.LightningNode()
                {
                    CryptoCode = network.CryptoCode,
                    Address = lightning?.GetLightningUrl()?.BaseUri.AbsoluteUri ?? string.Empty
                });
            }
        }

        [HttpPost]
        [Route("{storeId}")]
        public async Task<IActionResult> UpdateStore(string storeId, StoreViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }
            if (model.PreferredExchange != null)
                model.PreferredExchange = model.PreferredExchange.Trim().ToLowerInvariant();
            var store = await _Repo.FindStore(storeId, GetUserId());
            if (store == null)
                return NotFound();
            AddPaymentMethods(store, model);

            bool needUpdate = false;
            if (store.SpeedPolicy != model.SpeedPolicy)
            {
                needUpdate = true;
                store.SpeedPolicy = model.SpeedPolicy;
            }
            if (store.StoreName != model.StoreName)
            {
                needUpdate = true;
                store.StoreName = model.StoreName;
            }
            if (store.StoreWebsite != model.StoreWebsite)
            {
                needUpdate = true;
                store.StoreWebsite = model.StoreWebsite;
            }

            if (store.GetDefaultCrypto() != model.DefaultCryptoCurrency)
            {
                needUpdate = true;
                store.SetDefaultCrypto(model.DefaultCryptoCurrency);
            }
            model.SetCryptoCurrencies(_ExplorerProvider, model.DefaultCryptoCurrency);

            var blob = store.GetStoreBlob();
            blob.NetworkFeeDisabled = !model.NetworkFee;
            blob.MonitoringExpiration = model.MonitoringExpiration;
            blob.InvoiceExpiration = model.InvoiceExpiration;

            bool newExchange = blob.PreferredExchange != model.PreferredExchange;
            blob.PreferredExchange = model.PreferredExchange;

            blob.SetRateMultiplier(model.RateMultiplier);
            blob.AllowCoinConversion = model.AllowCoinConversion;

            if (store.SetStoreBlob(blob))
            {
                needUpdate = true;
            }

            if (!blob.PreferredExchange.IsCoinAverage() && newExchange)
            {
                using (HttpClient client = new HttpClient())
                {
                    var rate = await client.GetAsync(model.RateSource);
                    if (rate.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        ModelState.AddModelError(nameof(model.PreferredExchange), $"Unsupported exchange ({model.RateSource})");
                        return View(model);
                    }
                }
            }

            if (needUpdate)
            {
                await _Repo.UpdateStore(store);
                StatusMessage = "Store successfully updated";
            }

            return RedirectToAction(nameof(UpdateStore), new
            {
                storeId = storeId
            });
        }

        private DerivationStrategy ParseDerivationStrategy(string derivationScheme, string format, BTCPayNetwork network)
        {
            if (format == "Electrum")
            {
                //Unsupported Electrum
                //var p2wsh_p2sh = 0x295b43fU;
                //var p2wsh = 0x2aa7ed3U;
                Dictionary<uint, string[]> electrumMapping = new Dictionary<uint, string[]>();
                //Source https://github.com/spesmilo/electrum/blob/9edffd17542de5773e7284a8c8a2673c766bb3c3/lib/bitcoin.py
                var standard = 0x0488b21eU;
                electrumMapping.Add(standard, new[] { "legacy" });
                var p2wpkh_p2sh = 0x049d7cb2U;
                electrumMapping.Add(p2wpkh_p2sh, new string[] { "p2sh" });
                var p2wpkh = 0x4b24746U;
                electrumMapping.Add(p2wpkh, Array.Empty<string>());

                var data = Encoders.Base58Check.DecodeData(derivationScheme);
                if (data.Length < 4)
                    throw new FormatException("data.Length < 4");
                var prefix = Utils.ToUInt32(data, false);
                if (!electrumMapping.TryGetValue(prefix, out string[] labels))
                    throw new FormatException("!electrumMapping.TryGetValue(prefix, out string[] labels)");
                var standardPrefix = Utils.ToBytes(network.NBXplorerNetwork.DefaultSettings.ChainType == NBXplorer.ChainType.Main ? 0x0488b21eU : 0x043587cf, false);

                for (int i = 0; i < 4; i++)
                    data[i] = standardPrefix[i];

                derivationScheme = new BitcoinExtPubKey(Encoders.Base58Check.EncodeData(data), network.NBitcoinNetwork).ToString();
                foreach (var label in labels)
                {
                    derivationScheme = derivationScheme + $"-[{label}]";
                }
            }

            return new DerivationStrategy(new DerivationStrategyFactory(network.NBitcoinNetwork).Parse(derivationScheme), network);
        }

        [HttpGet]
        [Route("{storeId}/Tokens")]
        public async Task<IActionResult> ListTokens(string storeId)
        {
            var model = new TokensViewModel();
            var tokens = await _TokenRepository.GetTokensByStoreIdAsync(storeId);
            model.StatusMessage = StatusMessage;
            model.Tokens = tokens.Select(t => new TokenViewModel()
            {
                Facade = t.Facade,
                Label = t.Label,
                SIN = t.SIN,
                Id = t.Value
            }).ToArray();
            return View(model);
        }

        [HttpPost]
        [Route("/api-tokens")]
        [Route("{storeId}/Tokens/Create")]
        public async Task<IActionResult> CreateToken(string storeId, CreateTokenViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }
            model.Label = model.Label ?? String.Empty;
            storeId = model.StoreId ?? storeId;
            var userId = GetUserId();
            if (userId == null)
                return Unauthorized();
            var store = await _Repo.FindStore(storeId, userId);
            if (store == null)
                return Unauthorized();
            if (store.Role != StoreRoles.Owner)
            {
                StatusMessage = "Error: You need to be owner of this store to request pairing codes";
                return RedirectToAction(nameof(UserStoresController.ListStores), "UserStores");
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

        [HttpGet]
        [Route("/api-tokens")]
        [Route("{storeId}/Tokens/Create")]
        public async Task<IActionResult> CreateToken(string storeId)
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
                return Unauthorized();
            var model = new CreateTokenViewModel();
            model.Facade = "merchant";
            ViewBag.HidePublicKey = storeId == null;
            ViewBag.ShowStores = storeId == null;
            ViewBag.ShowMenu = storeId != null;
            model.StoreId = storeId;
            if (storeId == null)
            {
                model.Stores = new SelectList(await _Repo.GetStoresByUserId(userId), nameof(StoreData.Id), nameof(StoreData.StoreName), storeId);
            }

            return View(model);
        }


        [HttpPost]
        [Route("{storeId}/Tokens/Delete")]
        public async Task<IActionResult> DeleteToken(string storeId, string tokenId)
        {
            var token = await _TokenRepository.GetToken(tokenId);
            if (token == null ||
                token.StoreId != storeId ||
               !await _TokenRepository.DeleteToken(tokenId))
                StatusMessage = "Failure to revoke this token";
            else
                StatusMessage = "Token revoked";
            return RedirectToAction(nameof(ListTokens));
        }


        [HttpGet]
        [Route("/api-access-request")]
        public async Task<IActionResult> RequestPairing(string pairingCode, string selectedStore = null)
        {
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
                var stores = await _Repo.GetStoresByUserId(GetUserId());
                return View(new PairingModel()
                {
                    Id = pairing.Id,
                    Facade = pairing.Facade,
                    Label = pairing.Label,
                    SIN = pairing.SIN ?? "Server-Initiated Pairing",
                    SelectedStore = selectedStore ?? stores.FirstOrDefault()?.Id,
                    Stores = stores.Select(s => new PairingModel.StoreViewModel()
                    {
                        Id = s.Id,
                        Name = string.IsNullOrEmpty(s.StoreName) ? s.Id : s.StoreName
                    }).ToArray()
                });
            }
        }

        [HttpPost]
        [Route("/api-access-request")]
        public async Task<IActionResult> Pair(string pairingCode, string selectedStore)
        {
            if (pairingCode == null)
                return NotFound();
            var store = await _Repo.FindStore(selectedStore, GetUserId());
            var pairing = await _TokenRepository.GetPairingAsync(pairingCode);
            if (store == null || pairing == null)
                return NotFound();

            if(store.Role != StoreRoles.Owner)
            {
                StatusMessage = "Error: You can't approve a pairing without being owner of the store";
                return RedirectToAction(nameof(UserStoresController.ListStores), "UserStores");
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
            return _UserManager.GetUserId(User);
        }
    }
}
