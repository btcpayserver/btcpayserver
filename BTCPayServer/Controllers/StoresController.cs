using BTCPayServer.Authentication;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Fees;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using LedgerWallet;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Options;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitpayClient;
using NBXplorer.DerivationStrategy;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
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
        }
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
        [Route("{storeId}/wallet")]
        public async Task<IActionResult> Wallet(string storeId)
        {
            var store = await _Repo.FindStore(storeId, GetUserId());
            if (store == null)
                return NotFound();
            WalletModel model = new WalletModel();
            model.ServerUrl = GetStoreUrl(storeId);
            model.SetCryptoCurrencies(_ExplorerProvider, store.GetDefaultCrypto());
            return View(model);
        }

        private string GetStoreUrl(string storeId)
        {
            return HttpContext.Request.GetAbsoluteRoot() + "/stores/" + storeId + "/";
        }

        public class GetInfoResult
        {
            public int RecommendedSatoshiPerByte { get; set; }
            public double Balance { get; set; }
        }

        public class SendToAddressResult
        {
            public string TransactionId { get; set; }
        }

        [HttpGet]
        [Route("{storeId}/ws/ledger")]
        public async Task<IActionResult> LedgerConnection(
            string storeId,
            string command,
            // getinfo
            string cryptoCode = null,
            // sendtoaddress
            string destination = null, string amount = null, string feeRate = null, string substractFees = null
            )
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
                return NotFound();
            var store = await _Repo.FindStore(storeId, GetUserId());
            if (store == null)
                return NotFound();

            var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();

            var hw = new HardwareWalletService(webSocket);
            object result = null;
            try
            {
                BTCPayNetwork network = null;
                if (cryptoCode != null)
                {
                    network = _NetworkProvider.GetNetwork(cryptoCode);
                    if (network == null)
                        throw new FormatException("Invalid value for crypto code");
                }

                BitcoinAddress destinationAddress = null;
                if (destination != null)
                {
                    try
                    {
                        destinationAddress = BitcoinAddress.Create(destination);
                    }
                    catch { }
                    if (destinationAddress == null)
                        throw new FormatException("Invalid value for destination");
                }

                FeeRate feeRateValue = null;
                if (feeRate != null)
                {
                    try
                    {
                        feeRateValue = new FeeRate(Money.Satoshis(int.Parse(feeRate, CultureInfo.InvariantCulture)), 1);
                    }
                    catch { }
                    if (feeRateValue == null || feeRateValue.FeePerK <= Money.Zero)
                        throw new FormatException("Invalid value for fee rate");
                }

                Money amountBTC = null;
                if (amount != null)
                {
                    try
                    {
                        amountBTC = Money.Parse(amount);
                    }
                    catch { }
                    if (amountBTC == null || amountBTC <= Money.Zero)
                        throw new FormatException("Invalid value for amount");
                }

                bool subsctractFeesValue = false;
                if (substractFees != null)
                {
                    try
                    {
                        subsctractFeesValue = bool.Parse(substractFees);
                    }
                    catch { throw new FormatException("Invalid value for substract fees"); }
                }
                if (command == "test")
                {
                    result = await hw.Test();
                }
                if (command == "getxpub")
                {
                    result = await hw.GetExtPubKey(network);
                }
                if (command == "getinfo")
                {
                    var strategy = GetDirectDerivationStrategy(store, network);
                    var strategyBase = GetDerivationStrategy(store, network);
                    if (strategy == null || !await hw.SupportDerivation(network, strategy))
                    {
                        throw new Exception($"This store is not configured to use this ledger");
                    }

                    var feeProvider = _FeeRateProvider.CreateFeeProvider(network);
                    var recommendedFees = feeProvider.GetFeeRateAsync();
                    var balance = _WalletProvider.GetWallet(network).GetBalance(strategyBase);
                    result = new GetInfoResult() { Balance = (double)(await balance).ToDecimal(MoneyUnit.BTC), RecommendedSatoshiPerByte = (int)(await recommendedFees).GetFee(1).Satoshi };
                }

                if (command == "sendtoaddress")
                {
                    var strategy = GetDirectDerivationStrategy(store, network);
                    var strategyBase = GetDerivationStrategy(store, network);
                    var wallet = _WalletProvider.GetWallet(network);
                    var change = wallet.GetChangeAddressAsync(strategyBase);
                    var unspentCoins = await wallet.GetUnspentCoins(strategyBase);
                    var changeAddress = await change;
                    var transaction = await hw.SendToAddress(strategy, unspentCoins, network,
                                            new[] { (destinationAddress as IDestination, amountBTC, subsctractFeesValue) },
                                            feeRateValue,
                                            changeAddress.Item1,
                                            changeAddress.Item2);
                    try
                    {
                        var broadcastResult = await wallet.BroadcastTransactionsAsync(new List<Transaction>() { transaction });
                        if (!broadcastResult[0].Success)
                        {
                            throw new Exception($"RPC Error while broadcasting: {broadcastResult[0].RPCCode} {broadcastResult[0].RPCCodeMessage} {broadcastResult[0].RPCMessage}");
                        }
                    }
                    catch (Exception ex)
                    {
                        throw new Exception("Error while broadcasting: " + ex.Message);
                    }
                    wallet.InvalidateCache(strategyBase);
                    result = new SendToAddressResult() { TransactionId = transaction.GetHash().ToString() };
                }
            }
            catch (OperationCanceledException)
            { result = new LedgerTestResult() { Success = false, Error = "Timeout" }; }
            catch (Exception ex)
            { result = new LedgerTestResult() { Success = false, Error = ex.Message }; }

            try
            {
                if (result != null)
                {
                    UTF8Encoding UTF8NOBOM = new UTF8Encoding(false);
                    var bytes = UTF8NOBOM.GetBytes(JsonConvert.SerializeObject(result, _MvcJsonOptions.SerializerSettings));
                    await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, new CancellationTokenSource(2000).Token);
                }
            }
            catch { }
            finally
            {
                await webSocket.CloseSocket();
            }

            return new EmptyResult();
        }

        private DirectDerivationStrategy GetDirectDerivationStrategy(StoreData store, BTCPayNetwork network)
        {
            var strategy = GetDerivationStrategy(store, network);
            var directStrategy = strategy as DirectDerivationStrategy;
            if (directStrategy == null)
                directStrategy = (strategy as P2SHDerivationStrategy).Inner as DirectDerivationStrategy;
            if (!directStrategy.Segwit)
                return null;
            return directStrategy;
        }

        private DerivationStrategyBase GetDerivationStrategy(StoreData store, BTCPayNetwork network)
        {
            var strategy = store.GetDerivationStrategies(_NetworkProvider).FirstOrDefault(s => s.Network.NBitcoinNetwork == network.NBitcoinNetwork);
            if (strategy == null)
            {
                throw new Exception($"Derivation strategy for {network.CryptoCode} is not set");
            }

            return strategy.DerivationStrategyBase;
        }

        [HttpGet]
        public async Task<IActionResult> ListStores()
        {
            StoresViewModel result = new StoresViewModel();
            result.StatusMessage = StatusMessage;
            var stores = await _Repo.GetStoresByUserId(GetUserId());
            var balances = stores
                                .Select(s => s.GetDerivationStrategies(_NetworkProvider)
                                              .Select(d => ((Wallet: _WalletProvider.GetWallet(d.Network),
                                                            DerivationStrategy: d.DerivationStrategyBase)))
                                              .Where(_ => _.Wallet != null)
                                              .Select(async _ => (await GetBalanceString(_)) + " " + _.Wallet.Network.CryptoCode))
                                .ToArray();

            await Task.WhenAll(balances.SelectMany(_ => _));
            for (int i = 0; i < stores.Length; i++)
            {
                var store = stores[i];
                result.Stores.Add(new StoresViewModel.StoreViewModel()
                {
                    Id = store.Id,
                    Name = store.StoreName,
                    WebSite = store.StoreWebsite,
                    Balances = balances[i].Select(t => t.Result).ToArray()
                });
            }
            return View(result);
        }

        private static async Task<string> GetBalanceString((BTCPayWallet Wallet, DerivationStrategyBase DerivationStrategy) _)
        {
            using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                try
                {
                    return (await _.Wallet.GetBalance(_.DerivationStrategy, cts.Token)).ToString();
                }
                catch
                {
                    return "--";
                }
            }
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

            var storeBlob = store.GetStoreBlob();
            var vm = new StoreViewModel();
            vm.Id = store.Id;
            vm.StoreName = store.StoreName;
            vm.SetCryptoCurrencies(_ExplorerProvider, store.GetDefaultCrypto());
            vm.StoreWebsite = store.StoreWebsite;
            vm.NetworkFee = !storeBlob.NetworkFeeDisabled;
            vm.SpeedPolicy = store.SpeedPolicy;
            AddDerivationSchemes(store, vm);
            vm.StatusMessage = StatusMessage;
            vm.MonitoringExpiration = storeBlob.MonitoringExpiration;
            vm.InvoiceExpiration = storeBlob.InvoiceExpiration;
            vm.RateMultiplier = (double)storeBlob.GetRateMultiplier();
            vm.PreferredExchange = storeBlob.PreferredExchange.IsCoinAverage() ? "coinaverage" : storeBlob.PreferredExchange;
            return View(vm);
        }

        private void AddDerivationSchemes(StoreData store, StoreViewModel vm)
        {
            var strategies = store
                            .GetDerivationStrategies(_NetworkProvider)
                            .ToDictionary(s => s.Network.CryptoCode);
            foreach (var explorerProvider in _ExplorerProvider.GetAll())
            {
                if (strategies.TryGetValue(explorerProvider.Item1.CryptoCode, out DerivationStrategy strat))
                {
                    vm.DerivationSchemes.Add(new StoreViewModel.DerivationScheme()
                    {
                        Crypto = explorerProvider.Item1.CryptoCode,
                        Value = strat.DerivationStrategyBase.ToString()
                    });
                }
            }
        }

        [HttpGet]
        [Route("{storeId}/derivations")]
        public async Task<IActionResult> AddDerivationScheme(string storeId, string selectedScheme = null)
        {
            selectedScheme = selectedScheme ?? "BTC";
            var store = await _Repo.FindStore(storeId, GetUserId());
            if (store == null)
                return NotFound();
            DerivationSchemeViewModel vm = new DerivationSchemeViewModel();
            vm.ServerUrl = GetStoreUrl(storeId);
            vm.SetCryptoCurrencies(_ExplorerProvider, selectedScheme);
            return View(vm);
        }

        [HttpPost]
        [Route("{storeId}/derivations")]
        public async Task<IActionResult> AddDerivationScheme(string storeId, DerivationSchemeViewModel vm, string selectedScheme = null)
        {
            selectedScheme = selectedScheme ?? "BTC";
            vm.ServerUrl = GetStoreUrl(storeId);
            var store = await _Repo.FindStore(storeId, GetUserId());
            if (store == null)
                return NotFound();

            var network = vm.CryptoCurrency == null ? null : _ExplorerProvider.GetNetwork(vm.CryptoCurrency);
            vm.SetCryptoCurrencies(_ExplorerProvider, selectedScheme);
            if (network == null)
            {
                ModelState.AddModelError(nameof(vm.CryptoCurrency), "Invalid network");
                return View(vm);
            }
            var wallet = _WalletProvider.GetWallet(network);
            if (wallet == null)
            {
                ModelState.AddModelError(nameof(vm.CryptoCurrency), "Invalid network");
                return View(vm);
            }


            DerivationStrategyBase strategy = null;
            try
            {
                if (!string.IsNullOrEmpty(vm.DerivationScheme))
                {
                    strategy = ParseDerivationStrategy(vm.DerivationScheme, vm.DerivationSchemeFormat, network);
                    vm.DerivationScheme = strategy.ToString();
                }
                store.SetDerivationStrategy(network, vm.DerivationScheme);
            }
            catch
            {
                ModelState.AddModelError(nameof(vm.DerivationScheme), "Invalid Derivation Scheme");
                vm.Confirmation = false;
                return View(vm);
            }


            if (strategy == null || vm.Confirmation)
            {
                try
                {
                    if (strategy != null)
                        await wallet.TrackAsync(strategy);
                    store.SetDerivationStrategy(network, vm.DerivationScheme);
                }
                catch
                {
                    ModelState.AddModelError(nameof(vm.DerivationScheme), "Invalid Derivation Scheme");
                    return View(vm);
                }

                await _Repo.UpdateStore(store);
                StatusMessage = $"Derivation scheme for {network.CryptoCode} has been modified.";
                return RedirectToAction(nameof(UpdateStore), new { storeId = storeId });
            }
            else
            {
                if (!string.IsNullOrEmpty(vm.DerivationScheme))
                {
                    var line = strategy.GetLineFor(DerivationFeature.Deposit);

                    for (int i = 0; i < 10; i++)
                    {
                        var address = line.Derive((uint)i);
                        vm.AddressSamples.Add((DerivationStrategyBase.GetKeyPath(DerivationFeature.Deposit).Derive((uint)i).ToString(), address.ScriptPubKey.GetDestinationAddress(network.NBitcoinNetwork).ToString()));
                    }
                }
                vm.Confirmation = true;
                return View(vm);
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
            AddDerivationSchemes(store, model);

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

        private DerivationStrategyBase ParseDerivationStrategy(string derivationScheme, string format, BTCPayNetwork network)
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

            return new DerivationStrategyFactory(network.NBitcoinNetwork).Parse(derivationScheme);
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
