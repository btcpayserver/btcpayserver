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

        class WebSocketTransport : LedgerWallet.Transports.ILedgerTransport
        {
            private readonly WebSocket webSocket;

            public WebSocketTransport(System.Net.WebSockets.WebSocket webSocket)
            {
                if (webSocket == null)
                    throw new ArgumentNullException(nameof(webSocket));
                this.webSocket = webSocket;
            }

            public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
            public async Task<byte[][]> Exchange(byte[][] apdus)
            {
                List<byte[]> responses = new List<byte[]>();
                using (CancellationTokenSource cts = new CancellationTokenSource(Timeout))
                {
                    foreach (var apdu in apdus)
                    {
                        await this.webSocket.SendAsync(new ArraySegment<byte>(apdu), WebSocketMessageType.Binary, true, cts.Token);
                    }
                    foreach (var apdu in apdus)
                    {
                        byte[] response = new byte[300];
                        var result = await this.webSocket.ReceiveAsync(new ArraySegment<byte>(response), cts.Token);
                        Array.Resize(ref response, result.Count);
                        responses.Add(response);
                    }
                }
                return responses.ToArray();
            }
        }

        class LedgerTestResult
        {
            public bool Success { get; set; }
            public string Error { get; set; }
        }

        class GetInfoResult
        {
            public int RecommendedSatoshiPerByte { get; set; }
            public double Balance { get; set; }
        }

        class SendToAddressResult
        {
            public string TransactionId { get; set; }
        }

        class GetXPubResult
        {
            public string ExtPubKey { get; set; }
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
            var ledgerTransport = new WebSocketTransport(webSocket);
            var ledger = new LedgerWallet.LedgerClient(ledgerTransport);
            try
            {
                if (command == "test")
                {
                    var version = await ledger.GetFirmwareVersionAsync();
                    await Send(webSocket, new LedgerTestResult() { Success = true });
                }
                if (command == "getxpub")
                {
                    var network = _NetworkProvider.GetNetwork(cryptoCode);
                    try
                    {
                        var pubkey = await GetExtPubKey(ledger, network, new KeyPath("49'").Derive(network.CoinType).Derive(0, true));
                        var derivation = new DerivationStrategyFactory(network.NBitcoinNetwork).CreateDirectDerivationStrategy(pubkey, new DerivationStrategyOptions()
                        {
                            P2SH = true,
                            Legacy = false
                        });
                        await Send(webSocket, new GetXPubResult() { ExtPubKey = derivation.ToString() });
                    }
                    catch(FormatException)
                    {
                        await Send(webSocket, new LedgerTestResult() { Success = false, Error = "Unsupported ledger app" });
                    }
                }
                if (command == "getinfo")
                {
                    var network = _NetworkProvider.GetNetwork(cryptoCode);
                    var strategy = store.GetDerivationStrategies(_NetworkProvider).FirstOrDefault(s => s.Network.NBitcoinNetwork == network.NBitcoinNetwork);
                    if (strategy == null)
                    {
                        await Send(webSocket, new LedgerTestResult() { Success = false, Error = $"Derivation strategy for {cryptoCode} is not set" });
                        return new EmptyResult();
                    }
                    DirectDerivationStrategy directStrategy = GetDirectStrategy(strategy);
                    if (directStrategy == null)
                    {
                        await Send(webSocket, new LedgerTestResult() { Success = false, Error = $"The feature does not work for multi-sig wallets" });
                        return new EmptyResult();
                    }

                    var foundKeyPath = await GetKeyPath(ledger, network, directStrategy);

                    if (foundKeyPath == null)
                    {
                        await Send(webSocket, new LedgerTestResult() { Success = false, Error = $"This store is not configured to use this ledger" });
                        return new EmptyResult();
                    }

                    var feeProvider = _FeeRateProvider.CreateFeeProvider(network);
                    var recommendedFees = feeProvider.GetFeeRateAsync();
                    var balance = _WalletProvider.GetWallet(network).GetBalance(strategy.DerivationStrategyBase);

                    await Send(webSocket, new GetInfoResult() { Balance = (double)(await balance).ToDecimal(MoneyUnit.BTC), RecommendedSatoshiPerByte = (int)(await recommendedFees).GetFee(1).Satoshi });
                }

                if (command == "sendtoaddress")
                {
                    var network = _NetworkProvider.GetNetwork(cryptoCode);
                    var strategy = store.GetDerivationStrategies(_NetworkProvider).FirstOrDefault(s => s.Network.NBitcoinNetwork == network.NBitcoinNetwork);
                    if (strategy == null)
                    {
                        await Send(webSocket, new LedgerTestResult() { Success = false, Error = $"Derivation strategy for {cryptoCode} is not set" });
                        return new EmptyResult();
                    }

                    DirectDerivationStrategy directStrategy = GetDirectStrategy(strategy);
                    if (directStrategy == null)
                    {
                        await Send(webSocket, new LedgerTestResult() { Success = false, Error = $"The feature does not work for multi-sig wallets" });
                        return new EmptyResult();
                    }

                    var foundKeyPath = await GetKeyPath(ledger, network, directStrategy);

                    if (foundKeyPath == null)
                    {
                        await Send(webSocket, new LedgerTestResult() { Success = false, Error = $"This store is not configured to use this ledger" });
                        return new EmptyResult();
                    }

                    BitcoinAddress destinationAddress = null;
                    try
                    {
                        destinationAddress = BitcoinAddress.Create(destination.Trim());
                    }
                    catch
                    {
                        await Send(webSocket, new LedgerTestResult() { Success = false, Error = $"Invalid destination address" });
                        return new EmptyResult();
                    }

                    Money amountBTC = null;
                    try
                    {
                        amountBTC = Money.Parse(amount);
                    }
                    catch
                    {
                        await Send(webSocket, new LedgerTestResult() { Success = false, Error = $"Invalid amount" });
                        return new EmptyResult();
                    }
                    if (amount <= Money.Zero)
                    {
                        await Send(webSocket, new LedgerTestResult() { Success = false, Error = "The amount should be above zero" });
                        return new EmptyResult();
                    }

                    FeeRate feeRateValue = null;
                    try
                    {
                        feeRateValue = new FeeRate(Money.Satoshis(int.Parse(feeRate)), 1);
                    }
                    catch
                    {
                        await Send(webSocket, new LedgerTestResult() { Success = false, Error = "Invalid fee rate" });
                        return new EmptyResult();
                    }

                    if (feeRateValue.FeePerK <= Money.Zero)
                    {
                        await Send(webSocket, new LedgerTestResult() { Success = false, Error = "The fee rate should be above zero" });
                        return new EmptyResult();
                    }

                    bool substractFeeBool = bool.Parse(substractFees);

                    var wallet = _WalletProvider.GetWallet(network);
                    var unspentCoins = await wallet.GetUnspentCoins(strategy.DerivationStrategyBase);

                    TransactionBuilder builder = new TransactionBuilder();
                    builder.AddCoins(unspentCoins.Item1);
                    builder.Send(destinationAddress, amountBTC);
                    if (substractFeeBool)
                        builder.SubtractFees();
                    var change = await wallet.GetChangeAddressAsync(strategy.DerivationStrategyBase);
                    builder.SetChange(change.Item1);
                    builder.SendEstimatedFees(feeRateValue);
                    builder.Shuffle();
                    var unsigned = builder.BuildTransaction(false);

                    Dictionary<OutPoint, KeyPath> keyPaths = unspentCoins.Item2;
                    var hasChange = unsigned.Outputs.Count == 2;
                    var usedCoins = builder.FindSpentCoins(unsigned);
                    ledgerTransport.Timeout = TimeSpan.FromMinutes(5);
                    var fullySigned = await ledger.SignTransactionAsync(
                        usedCoins.Select(c => new SignatureRequest
                        {
                            InputCoin = c,
                            KeyPath = foundKeyPath.Derive(keyPaths[c.Outpoint]),
                            PubKey = directStrategy.Root.Derive(keyPaths[c.Outpoint]).PubKey
                        }).ToArray(),
                        unsigned,
                        hasChange ? foundKeyPath.Derive(change.Item2) : null);
                    try
                    {
                        var result = await wallet.BroadcastTransactionsAsync(new List<Transaction>() { fullySigned });
                        if (!result[0].Success)
                        {
                            await Send(webSocket, new LedgerTestResult() { Success = false, Error = $"RPC Error while broadcasting: {result[0].RPCCode} {result[0].RPCCodeMessage} {result[0].RPCMessage}" });
                            return new EmptyResult();
                        }
                    }
                    catch (Exception ex)
                    {
                        await Send(webSocket, new LedgerTestResult() { Success = false, Error = "Error while broadcasting: " + ex.Message });
                        return new EmptyResult();
                    }
                    await Send(webSocket, new SendToAddressResult() { TransactionId = fullySigned.GetHash().ToString() });
                }
            }
            catch (LedgerWallet.LedgerWalletException ex)
            { try { await Send(webSocket, new LedgerTestResult() { Success = false, Error = ex.Message }); } catch { } }
            catch (OperationCanceledException)
            { try { await Send(webSocket, new LedgerTestResult() { Success = false, Error = "timeout" }); } catch { } }
            catch (Exception ex)
            { try { await Send(webSocket, new LedgerTestResult() { Success = false, Error = ex.Message }); } catch { } }
            finally
            {
                await webSocket.CloseSocket();
            }

            return new EmptyResult();
        }

        private static async Task<KeyPath> GetKeyPath(LedgerClient ledger, BTCPayNetwork network, DirectDerivationStrategy directStrategy)
        {
            KeyPath foundKeyPath = null;
            foreach (var account in
                                  new[] { new KeyPath("49'"), new KeyPath("44'") }
                                  .Select(purpose => purpose.Derive(network.CoinType))
                                  .SelectMany(coinType => Enumerable.Range(0, 5).Select(i => coinType.Derive(i, true))))
            {
                try
                {
                    var extpubkey = await GetExtPubKey(ledger, network, account);
                    if (directStrategy.ToString().Contains(extpubkey.ToString()))
                    {
                        foundKeyPath = account;
                        break;
                    }
                }
                catch (FormatException)
                {
                    throw new Exception($"The opened ledger app does not support {network.NBitcoinNetwork.Name}");
                }
            }

            return foundKeyPath;
        }

        private static async Task<BitcoinExtPubKey> GetExtPubKey(LedgerClient ledger, BTCPayNetwork network, KeyPath account)
        {
            var pubKey = await ledger.GetWalletPubKeyAsync(account);
            if (pubKey.Address.Network != network.NBitcoinNetwork)
            {
                if (network.DefaultSettings.ChainType == NBXplorer.ChainType.Main)
                    throw new Exception($"The opened ledger app should be for {network.NBitcoinNetwork.Name}, not for {pubKey.Address.Network}");
            }
            var parent = (await ledger.GetWalletPubKeyAsync(account.Parent)).UncompressedPublicKey.Compress();
            var extpubkey = new ExtPubKey(pubKey.UncompressedPublicKey.Compress(), pubKey.ChainCode, (byte)account.Indexes.Length, parent.Hash.ToBytes().Take(4).ToArray(), account.Indexes.Last()).GetWif(network.NBitcoinNetwork);
            return extpubkey;
        }

        private static DirectDerivationStrategy GetDirectStrategy(DerivationStrategy strategy)
        {
            var directStrategy = strategy.DerivationStrategyBase as DirectDerivationStrategy;
            if (directStrategy == null)
                directStrategy = (strategy.DerivationStrategyBase as P2SHDerivationStrategy).Inner as DirectDerivationStrategy;
            return directStrategy;
        }

        UTF8Encoding UTF8NOBOM = new UTF8Encoding(false);
        private async Task Send(WebSocket webSocket, object result)
        {
            var bytes = UTF8NOBOM.GetBytes(JsonConvert.SerializeObject(result, _MvcJsonOptions.SerializerSettings));
            await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, new CancellationTokenSource(2000).Token);
        }

        [HttpGet]
        public async Task<IActionResult> ListStores()
        {
            StoresViewModel result = new StoresViewModel();
            result.StatusMessage = StatusMessage;
            var stores = await _Repo.GetStoresByUserId(GetUserId());
            var balances = stores
                                .Select(s => s.GetDerivationStrategies(_NetworkProvider)
                                              .Select(d => (Wallet: _WalletProvider.GetWallet(d.Network),
                                                            DerivationStrategy: d.DerivationStrategyBase))
                                              .Where(_ => _.Wallet != null)
                                              .Select(async _ => (await _.Wallet.GetBalance(_.DerivationStrategy)).ToString() + " " + _.Wallet.Network.CryptoCode))
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
                electrumMapping.Add(p2wpkh, new string[] { });

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
