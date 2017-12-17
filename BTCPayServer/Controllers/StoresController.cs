using BTCPayServer.Authentication;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitpayClient;
using NBXplorer.DerivationStrategy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Controllers
{
    [Route("stores")]
    [Authorize(AuthenticationSchemes = "Identity.Application")]
    [Authorize(Policy = "CanAccessStore")]
    [AutoValidateAntiforgeryToken]
    public class StoresController : Controller
    {
        public StoresController(
            StoreRepository repo,
            TokenRepository tokenRepo,
            CallbackController callbackController,
            UserManager<ApplicationUser> userManager,
            AccessTokenController tokenController,
            BTCPayWallet wallet,
            Network network,
            IHostingEnvironment env)
        {
            _Repo = repo;
            _TokenRepository = tokenRepo;
            _UserManager = userManager;
            _TokenController = tokenController;
            _Wallet = wallet;
            _Env = env;
            _Network = network;
            _CallbackController = callbackController;
        }
        Network _Network;
        CallbackController _CallbackController;
        BTCPayWallet _Wallet;
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
        [Route("create")]
        public IActionResult CreateStore()
        {
            return View();
        }

        [HttpPost]
        [Route("create")]
        public async Task<IActionResult> CreateStore(CreateStoreViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                return View(vm);
            }
            var store = await _Repo.CreateStore(GetUserId(), vm.Name);
            CreatedStoreId = store.Id;
            StatusMessage = "Store successfully created";
            return RedirectToAction(nameof(ListStores));
        }

        public string CreatedStoreId
        {
            get; set;
        }

        [HttpGet]
        public async Task<IActionResult> ListStores()
        {
            StoresViewModel result = new StoresViewModel();
            result.StatusMessage = StatusMessage;
            var stores = await _Repo.GetStoresByUserId(GetUserId());
            var balances = stores.Select(async s => string.IsNullOrEmpty(s.DerivationStrategy) ? Money.Zero : await _Wallet.GetBalance(ParseDerivationStrategy(s.DerivationStrategy, null))).ToArray();

            for (int i = 0; i < stores.Length; i++)
            {
                var store = stores[i];
                result.Stores.Add(new StoresViewModel.StoreViewModel()
                {
                    Id = store.Id,
                    Name = store.StoreName,
                    WebSite = store.StoreWebsite,
                    Balance = await balances[i]
                });
            }
            return View(result);
        }

        [HttpGet]
        [Route("{storeId}/delete")]
        public async Task<IActionResult> DeleteStore(string storeId)
        {
            var store = await _Repo.FindStore(storeId, GetUserId());
            if (store == null)
                return NotFound();
            return View("Confirm", new ConfirmModel()
            {
                Title = "Delete store " + store.StoreName,
                Description = "This store will still be accessible to users sharing it",
                Action = "Delete"
            });
        }

        [HttpPost]
        [Route("{storeId}/delete")]
        public async Task<IActionResult> DeleteStorePost(string storeId)
        {
            var userId = GetUserId();
            var store = await _Repo.FindStore(storeId, GetUserId());
            if (store == null)
                return NotFound();
            await _Repo.RemoveStore(storeId, userId);
            StatusMessage = "Store removed successfully";
            return RedirectToAction(nameof(ListStores));
        }

        [HttpGet]
        [Route("{storeId}")]
        public async Task<IActionResult> UpdateStore(string storeId)
        {
            var store = await _Repo.FindStore(storeId, GetUserId());
            if (store == null)
                return NotFound();

            var storeBlob = store.GetStoreBlob(_Network);
            var vm = new StoreViewModel();
            vm.Id = store.Id;
            vm.StoreName = store.StoreName;
            vm.StoreWebsite = store.StoreWebsite;
            vm.NetworkFee = !storeBlob.NetworkFeeDisabled;
            vm.SpeedPolicy = store.SpeedPolicy;
            vm.DerivationScheme = store.DerivationStrategy;
            vm.StatusMessage = StatusMessage;
            vm.MonitoringExpiration = storeBlob.MonitoringExpiration;
            return View(vm);
        }

        [HttpPost]
        [Route("{storeId}")]
        public async Task<IActionResult> UpdateStore(string storeId, StoreViewModel model, string command)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }
            var store = await _Repo.FindStore(storeId, GetUserId());
            if (store == null)
                return NotFound();

            if (command == "Save")
            {
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

                if (store.DerivationStrategy != model.DerivationScheme)
                {
                    needUpdate = true;
                    try
                    {
                        if (!string.IsNullOrEmpty(model.DerivationScheme))
                        {
                            var strategy = ParseDerivationStrategy(model.DerivationScheme, model.DerivationSchemeFormat);
                            await _Wallet.TrackAsync(strategy);
                            await _CallbackController.RegisterCallbackUriAsync(strategy);
                            model.DerivationScheme = strategy.ToString();
                        }
                        store.DerivationStrategy = model.DerivationScheme;
                    }
                    catch
                    {
                        ModelState.AddModelError(nameof(model.DerivationScheme), "Invalid Derivation Scheme");
                        return View(model);
                    }
                }

                var blob = store.GetStoreBlob(_Network);
                blob.NetworkFeeDisabled = !model.NetworkFee;
                blob.MonitoringExpiration = model.MonitoringExpiration;

                if (store.SetStoreBlob(blob, _Network))
                {
                    needUpdate = true;
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
            else
            {
                if (!string.IsNullOrEmpty(model.DerivationScheme))
                {
                    try
                    {
                        var scheme = ParseDerivationStrategy(model.DerivationScheme, model.DerivationSchemeFormat);
                        var line = scheme.GetLineFor(DerivationFeature.Deposit);

                        for (int i = 0; i < 10; i++)
                        {
                            var address = line.Derive((uint)i);
                            model.AddressSamples.Add((line.Path.Derive((uint)i).ToString(), address.ScriptPubKey.GetDestinationAddress(_Network).ToString()));
                        }
                    }
                    catch
                    {
                        ModelState.AddModelError(nameof(model.DerivationScheme), "Invalid Derivation Scheme");
                    }
                }
                return View(model);
            }
        }

        private DerivationStrategyBase ParseDerivationStrategy(string derivationScheme, string format)
        {
            if (format == "Electrum")
            {
                //Unsupported Electrum
                //var p2wsh_p2sh = 0x295b43fU;
                //var p2wsh = 0x2aa7ed3U;
                Dictionary<uint, string[]> electrumMapping = new Dictionary<uint, string[]>();
                //Source https://github.com/spesmilo/electrum/blob/9edffd17542de5773e7284a8c8a2673c766bb3c3/lib/bitcoin.py
                var standard = _Network == Network.Main ? 0x0488b21eU : 0x043587cf;
                electrumMapping.Add(standard, new[] { "legacy" });
                var p2wpkh_p2sh = 0x049d7cb2U;
                electrumMapping.Add(p2wpkh_p2sh, new string[] { "p2sh" });
                var p2wpkh = 0x4b24746U;
                electrumMapping.Add(p2wpkh, new string[] { });

                var data = Encoders.Base58Check.DecodeData(derivationScheme);
                if (data.Length < 4)
                    throw new FormatException("data.Length < 4");
                var prefix = Utils.ToUInt32(data, false);
                if (!electrumMapping.TryGetValue(prefix, out string[] labels))
                    throw new FormatException("!electrumMapping.TryGetValue(prefix, out string[] labels)");
                var standardPrefix = Utils.ToBytes(standard, false);

                for (int i = 0; i < 4; i++)
                    data[i] = standardPrefix[i];

                derivationScheme = new BitcoinExtPubKey(Encoders.Base58Check.EncodeData(data), _Network).ToString();
                foreach (var label in labels)
                {
                    derivationScheme = derivationScheme + $"-[{label}]";
                }
            }

            return new DerivationStrategyFactory(_Network).Parse(derivationScheme);
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
            if (storeId == null) // Permissions are not checked by Policy if the storeId is not passed by url
            {
                storeId = model.StoreId;
                var userId = GetUserId();
                if (userId == null)
                    return Unauthorized();
                var store = await _Repo.FindStore(storeId, userId);
                if (store == null)
                    return Unauthorized();
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

            return RedirectToAction(nameof(RequestPairing), new
            {
                pairingCode = pairingCode,
                selectedStore = storeId
            });
        }

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
            var pairing = await _TokenRepository.GetPairingAsync(pairingCode);
            if (pairing == null)
            {
                StatusMessage = "Unknown pairing code";
                return RedirectToAction(nameof(ListStores));
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
        [Route("api-access-request")]
        public async Task<IActionResult> Pair(string pairingCode, string selectedStore)
        {
            if (pairingCode == null)
                return NotFound();
            var store = await _Repo.FindStore(selectedStore, GetUserId());
            var pairing = await _TokenRepository.GetPairingAsync(pairingCode);
            if (store == null || pairing == null)
                return NotFound();

            var pairingResult = await _TokenRepository.PairWithStoreAsync(pairingCode, store.Id);
            if (pairingResult == PairingResult.Complete || pairingResult == PairingResult.Partial)
            {
                StatusMessage = "Pairing is successfull";
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
