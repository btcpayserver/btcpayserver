using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.ModelBinders;
using BTCPayServer.Models;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Security;
using BTCPayServer.Services;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using LedgerWallet;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Newtonsoft.Json;
using static BTCPayServer.Controllers.StoresController;

namespace BTCPayServer.Controllers
{
    [Route("wallets")]
    [Authorize(AuthenticationSchemes = Policies.CookieAuthentication)]
    [AutoValidateAntiforgeryToken]
    public partial class WalletsController : Controller
    {
        public StoreRepository Repository { get; }
        public BTCPayNetworkProvider NetworkProvider { get; }
        public ExplorerClientProvider ExplorerClientProvider { get; }

        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IOptions<MvcJsonOptions> _mvcJsonOptions;
        private readonly NBXplorerDashboard _dashboard;

        private readonly IFeeProviderFactory _feeRateProvider;
        private readonly BTCPayWalletProvider _walletProvider;
        public RateFetcher RateFetcher { get; }
        [TempData]
        public string StatusMessage { get; set; }

        CurrencyNameTable _currencyTable;
        public WalletsController(StoreRepository repo,
                                 CurrencyNameTable currencyTable,
                                 BTCPayNetworkProvider networkProvider,
                                 UserManager<ApplicationUser> userManager,
                                 IOptions<MvcJsonOptions> mvcJsonOptions,
                                 NBXplorerDashboard dashboard,
                                 RateFetcher rateProvider,
                                 ExplorerClientProvider explorerProvider,
                                 IFeeProviderFactory feeRateProvider,
                                 BTCPayWalletProvider walletProvider)
        {
            _currencyTable = currencyTable;
            Repository = repo;
            RateFetcher = rateProvider;
            NetworkProvider = networkProvider;
            _userManager = userManager;
            _mvcJsonOptions = mvcJsonOptions;
            _dashboard = dashboard;
            ExplorerClientProvider = explorerProvider;
            _feeRateProvider = feeRateProvider;
            _walletProvider = walletProvider;
        }

        public async Task<IActionResult> ListWallets()
        {
            var wallets = new ListWalletsViewModel();
            var stores = await Repository.GetStoresByUserId(GetUserId());

            var onChainWallets = stores
                                .SelectMany(s => s.GetSupportedPaymentMethods(NetworkProvider)
                                              .OfType<DerivationSchemeSettings>()
                                              .Select(d => ((Wallet: _walletProvider.GetWallet(d.Network),
                                                            DerivationStrategy: d.AccountDerivation,
                                                            Network: d.Network)))
                                              .Where(_ => _.Wallet != null)
                                              .Select(_ => (Wallet: _.Wallet,
                                                            Store: s,
                                                            Balance: GetBalanceString(_.Wallet, _.DerivationStrategy),
                                                            DerivationStrategy: _.DerivationStrategy,
                                                            Network: _.Network)))
                                              .ToList();

            foreach (var wallet in onChainWallets)
            {
                ListWalletsViewModel.WalletViewModel walletVm = new ListWalletsViewModel.WalletViewModel();
                wallets.Wallets.Add(walletVm);
                walletVm.Balance = await wallet.Balance + " " + wallet.Wallet.Network.CryptoCode;
                if (!wallet.Store.HasClaim(Policies.CanModifyStoreSettings.Key))
                {
                    walletVm.Balance = "";
                }
                walletVm.CryptoCode = wallet.Network.CryptoCode;
                walletVm.StoreId = wallet.Store.Id;
                walletVm.Id = new WalletId(wallet.Store.Id, wallet.Network.CryptoCode);
                walletVm.StoreName = wallet.Store.StoreName;
                walletVm.IsOwner = wallet.Store.HasClaim(Policies.CanModifyStoreSettings.Key);
            }

            return View(wallets);
        }

        [HttpGet]
        [Route("{walletId}")]
        public async Task<IActionResult> WalletTransactions(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId)
        {
            DerivationSchemeSettings paymentMethod = await GetDerivationSchemeSettings(walletId);
            if (paymentMethod == null)
                return NotFound();

            var wallet = _walletProvider.GetWallet(paymentMethod.Network);
            var transactions = await wallet.FetchTransactions(paymentMethod.AccountDerivation);

            var model = new ListTransactionsViewModel();
            foreach (var tx in transactions.UnconfirmedTransactions.Transactions.Concat(transactions.ConfirmedTransactions.Transactions))
            {
                var vm = new ListTransactionsViewModel.TransactionViewModel();
                model.Transactions.Add(vm);
                vm.Id = tx.TransactionId.ToString();
                vm.Link = string.Format(CultureInfo.InvariantCulture, paymentMethod.Network.BlockExplorerLink, vm.Id);
                vm.Timestamp = tx.Timestamp;
                vm.Positive = tx.BalanceChange >= Money.Zero;
                vm.Balance = tx.BalanceChange.ToString();
                vm.IsConfirmed = tx.Confirmations != 0;
            }
            model.Transactions = model.Transactions.OrderByDescending(t => t.Timestamp).ToList();
            return View(model);
        }


        [HttpGet]
        [Route("{walletId}/send")]
        public async Task<IActionResult> WalletSend(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, string defaultDestination = null, string defaultAmount = null)
        {
            if (walletId?.StoreId == null)
                return NotFound();
            var store = await Repository.FindStore(walletId.StoreId, GetUserId());
            DerivationSchemeSettings paymentMethod = GetDerivationSchemeSettings(walletId, store);
            if (paymentMethod == null)
                return NotFound();
            var network = this.NetworkProvider.GetNetwork(walletId?.CryptoCode);
            if (network == null)
                return NotFound();
            var storeData = store.GetStoreBlob();
            var rateRules = store.GetStoreBlob().GetRateRules(NetworkProvider);
            rateRules.Spread = 0.0m;
            var currencyPair = new Rating.CurrencyPair(paymentMethod.PaymentId.CryptoCode, GetCurrencyCode(storeData.DefaultLang) ?? "USD");
            WalletSendModel model = new WalletSendModel()
            {
                Destination = defaultDestination,
                CryptoCode = walletId.CryptoCode
            };
            if (double.TryParse(defaultAmount, out var amount))
                model.Amount = (decimal)amount;

            var feeProvider = _feeRateProvider.CreateFeeProvider(network);
            var recommendedFees = feeProvider.GetFeeRateAsync();
            var balance = _walletProvider.GetWallet(network).GetBalance(paymentMethod.AccountDerivation);
            model.CurrentBalance = (await balance).ToDecimal(MoneyUnit.BTC);
            model.RecommendedSatoshiPerByte = (int)(await recommendedFees).GetFee(1).Satoshi;
            model.FeeSatoshiPerByte = model.RecommendedSatoshiPerByte;
            model.SupportRBF = network.SupportRBF;
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                try
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(5));
                    var result = await RateFetcher.FetchRate(currencyPair, rateRules, cts.Token).WithCancellation(cts.Token);
                    if (result.BidAsk != null)
                    {
                        model.Rate = result.BidAsk.Center;
                        model.Divisibility = _currencyTable.GetNumberFormatInfo(currencyPair.Right, true).CurrencyDecimalDigits;
                        model.Fiat = currencyPair.Right;
                    }
                    else
                    {
                        model.RateError = $"{result.EvaluatedRule} ({string.Join(", ", result.Errors.OfType<object>().ToArray())})";
                    }
                }
                catch (Exception ex) { model.RateError = ex.Message; }
            }
            return View(model);
        }

        [HttpPost]
        [Route("{walletId}/send")]
        public async Task<IActionResult> WalletSend(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, WalletSendModel vm, string command = null, CancellationToken cancellation = default)
        {
            if (walletId?.StoreId == null)
                return NotFound();
            var store = await Repository.FindStore(walletId.StoreId, GetUserId());
            if (store == null)
                return NotFound();
            var network = this.NetworkProvider.GetNetwork(walletId?.CryptoCode);
            if (network == null)
                return NotFound();
            vm.SupportRBF = network.SupportRBF;
            var destination = ParseDestination(vm.Destination, network.NBitcoinNetwork);
            if (destination == null)
                ModelState.AddModelError(nameof(vm.Destination), "Invalid address");

            if (vm.Amount.HasValue)
            {
                if (vm.CurrentBalance == vm.Amount.Value && !vm.SubstractFees)
                    ModelState.AddModelError(nameof(vm.Amount), "You are sending all your balance to the same destination, you should substract the fees");
                if (vm.CurrentBalance < vm.Amount.Value)
                    ModelState.AddModelError(nameof(vm.Amount), "You are sending more than what you own");
            }
            if (!ModelState.IsValid)
                return View(vm);

            DerivationSchemeSettings derivationScheme = await GetDerivationSchemeSettings(walletId);

            CreatePSBTResponse psbt = null;
            try
            {
                psbt = await CreatePSBT(network, derivationScheme, vm, cancellation);
            }
            catch (NBXplorerException ex)
            {
                ModelState.AddModelError(nameof(vm.Amount), ex.Error.Message);
                return View(vm);
            }
            catch (NotSupportedException)
            {
                ModelState.AddModelError(nameof(vm.Destination), "You need to update your version of NBXplorer");
                return View(vm);
            }
            derivationScheme.RebaseKeyPaths(psbt.PSBT);
            if (command == "ledger")
            {
                return ViewWalletSendLedger(psbt.PSBT, psbt.ChangeAddress);
            }
            else if (command == "analyze-psbt")
            {
                return ViewPSBT(psbt.PSBT, $"Send-{vm.Amount.Value}-{network.CryptoCode}-to-{destination[0].ToString()}.psbt");
            }
            return View(vm);
        }

        private ViewResult ViewWalletSendLedger(PSBT psbt, BitcoinAddress hintChange = null)
        {
            return View("WalletSendLedger", new WalletSendLedgerModel()
            {
                PSBT = psbt.ToBase64(),
                HintChange = hintChange?.ToString(),
                WebsocketPath = this.Url.Action(nameof(LedgerConnection)),
                SuccessPath = this.Url.Action(nameof(WalletPSBTReady))
            });
        }

        private IDestination[] ParseDestination(string destination, Network network)
        {
            try
            {
                destination = destination?.Trim();
                return new IDestination[] { BitcoinAddress.Create(destination, network) };
            }
            catch
            {
                return null;
            }
        }

        private async Task<IActionResult> RedirectToWalletTransaction(WalletId walletId, Transaction transaction)
        {
            var network = NetworkProvider.GetNetwork(walletId.CryptoCode);
            if (transaction != null)
            {
                var wallet = _walletProvider.GetWallet(network);
                var derivationSettings = await GetDerivationSchemeSettings(walletId);
                wallet.InvalidateCache(derivationSettings.AccountDerivation);
                StatusMessage = $"Transaction broadcasted successfully ({transaction.GetHash().ToString()})";
            }
            return RedirectToAction(nameof(WalletTransactions));
        }

        [HttpGet]
        [Route("{walletId}/rescan")]
        public async Task<IActionResult> WalletRescan(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId)
        {
            if (walletId?.StoreId == null)
                return NotFound();
            DerivationSchemeSettings paymentMethod = await GetDerivationSchemeSettings(walletId);
            if (paymentMethod == null)
                return NotFound();

            var vm = new RescanWalletModel();
            vm.IsFullySync = _dashboard.IsFullySynched(walletId.CryptoCode, out var unused);
            vm.IsServerAdmin = User.Claims.Any(c => c.Type == Policies.CanModifyServerSettings.Key && c.Value == "true");
            vm.IsSupportedByCurrency = _dashboard.Get(walletId.CryptoCode)?.Status?.BitcoinStatus?.Capabilities?.CanScanTxoutSet == true;
            var explorer = ExplorerClientProvider.GetExplorerClient(walletId.CryptoCode);
            var scanProgress = await explorer.GetScanUTXOSetInformationAsync(paymentMethod.AccountDerivation);
            if (scanProgress != null)
            {
                vm.PreviousError = scanProgress.Error;
                if (scanProgress.Status == ScanUTXOStatus.Queued || scanProgress.Status == ScanUTXOStatus.Pending)
                {
                    if (scanProgress.Progress == null)
                    {
                        vm.Progress = 0;
                    }
                    else
                    {
                        vm.Progress = scanProgress.Progress.OverallProgress;
                        vm.RemainingTime = TimeSpan.FromSeconds(scanProgress.Progress.RemainingSeconds).PrettyPrint();
                    }
                }
                if (scanProgress.Status == ScanUTXOStatus.Complete)
                {
                    vm.LastSuccess = scanProgress.Progress;
                    vm.TimeOfScan = (scanProgress.Progress.CompletedAt.Value - scanProgress.Progress.StartedAt).PrettyPrint();
                }
            }
            return View(vm);
        }

        [HttpPost]
        [Route("{walletId}/rescan")]
        [Authorize(Policy = Policies.CanModifyServerSettings.Key)]
        public async Task<IActionResult> WalletRescan(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, RescanWalletModel vm)
        {
            if (walletId?.StoreId == null)
                return NotFound();
            DerivationSchemeSettings paymentMethod = await GetDerivationSchemeSettings(walletId);
            if (paymentMethod == null)
                return NotFound();
            var explorer = ExplorerClientProvider.GetExplorerClient(walletId.CryptoCode);
            try
            {
                await explorer.ScanUTXOSetAsync(paymentMethod.AccountDerivation, vm.BatchSize, vm.GapLimit, vm.StartingIndex);
            }
            catch (NBXplorerException ex) when (ex.Error.Code == "scanutxoset-in-progress")
            {

            }
            return RedirectToAction();
        }

        private string GetCurrencyCode(string defaultLang)
        {
            if (defaultLang == null)
                return null;
            try
            {
                var ri = new RegionInfo(defaultLang);
                return ri.ISOCurrencySymbol;
            }
            catch (ArgumentException) { }
            return null;
        }

        private DerivationSchemeSettings GetDerivationSchemeSettings(WalletId walletId, StoreData store)
        {
            if (store == null || !store.HasClaim(Policies.CanModifyStoreSettings.Key))
                return null;

            var paymentMethod = store
                            .GetSupportedPaymentMethods(NetworkProvider)
                            .OfType<DerivationSchemeSettings>()
                            .FirstOrDefault(p => p.PaymentId.PaymentType == Payments.PaymentTypes.BTCLike && p.PaymentId.CryptoCode == walletId.CryptoCode);
            return paymentMethod;
        }

        private async Task<DerivationSchemeSettings> GetDerivationSchemeSettings(WalletId walletId)
        {
            var store = (await Repository.FindStore(walletId.StoreId, GetUserId()));
            return GetDerivationSchemeSettings(walletId, store);
        }

        private static async Task<string> GetBalanceString(BTCPayWallet wallet, DerivationStrategyBase derivationStrategy)
        {
            using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                try
                {
                    return (await wallet.GetBalance(derivationStrategy, cts.Token)).ToString();
                }
                catch
                {
                    return "--";
                }
            }
        }

        private string GetUserId()
        {
            return _userManager.GetUserId(User);
        }

        [HttpGet]
        [Route("{walletId}/send/ledger/ws")]
        public async Task<IActionResult> LedgerConnection(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId,
            string command,
            // getinfo
            // getxpub
            int account = 0,
            // sendtoaddress
            string psbt = null,
            string hintChange = null
            )
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
                return NotFound();

            var network = NetworkProvider.GetNetwork(walletId.CryptoCode);
            if (network == null)
                throw new FormatException("Invalid value for crypto code");
            var storeData = (await Repository.FindStore(walletId.StoreId, GetUserId()));
            var derivationSettings = GetDerivationSchemeSettings(walletId, storeData);

            var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();

            using (var normalOperationTimeout = new CancellationTokenSource())
            using (var signTimeout = new CancellationTokenSource())
            {
                normalOperationTimeout.CancelAfter(TimeSpan.FromMinutes(30));
                var hw = new LedgerHardwareWalletService(webSocket);
                var model = new WalletSendLedgerModel();
                object result = null;
                try
                {
                    if (command == "test")
                    {
                        result = await hw.Test(normalOperationTimeout.Token);
                    }
                    if (command == "sendtoaddress")
                    {
                        if (!_dashboard.IsFullySynched(network.CryptoCode, out var summary))
                            throw new Exception($"{network.CryptoCode}: not started or fully synched");

                        var accountKey = derivationSettings.GetSigningAccountKeySettings();
                        // Some deployment does not have the AccountKeyPath set, let's fix this...
                        if (accountKey.AccountKeyPath == null)
                        {
                            // If the saved wallet key path is not present or incorrect, let's scan the wallet to see if it can sign strategy
                            var foundKeyPath = await hw.FindKeyPathFromDerivation(network,
                                                                               derivationSettings.AccountDerivation,
                                                                               normalOperationTimeout.Token);
                            accountKey.AccountKeyPath = foundKeyPath ?? throw new HardwareWalletException($"This store is not configured to use this ledger");
                            storeData.SetSupportedPaymentMethod(derivationSettings);
                            await Repository.UpdateStore(storeData);
                        }
                        // If it has already the AccountKeyPath, we did not looked up for it, so we need to check if we are on the right ledger
                        else
                        {
                            // Checking if ledger is right with the RootFingerprint is faster as it does not need to make a query to the parent xpub, 
                            // but some deployment does not have it, so let's use AccountKeyPath instead
                            if (accountKey.RootFingerprint == null)
                            {

                                var actualPubKey = await hw.GetExtPubKey(network, accountKey.AccountKeyPath, normalOperationTimeout.Token);
                                if (!derivationSettings.AccountDerivation.GetExtPubKeys().Any(p => p.GetPublicKey() == actualPubKey.GetPublicKey()))
                                    throw new HardwareWalletException($"This store is not configured to use this ledger");
                            }
                            // We have the root fingerprint, we can check the root from it
                            else
                            {
                                var actualPubKey = await hw.GetPubKey(network, new KeyPath(), normalOperationTimeout.Token);
                                if (actualPubKey.GetHDFingerPrint() != accountKey.RootFingerprint.Value)
                                    throw new HardwareWalletException($"This store is not configured to use this ledger");
                            }
                        }

                        // Some deployment does not have the RootFingerprint set, let's fix this...
                        if (accountKey.RootFingerprint == null)
                        {
                            accountKey.RootFingerprint = (await hw.GetPubKey(network, new KeyPath(), normalOperationTimeout.Token)).GetHDFingerPrint();
                            storeData.SetSupportedPaymentMethod(derivationSettings);
                            await Repository.UpdateStore(storeData);
                        }

                        var psbtResponse = new CreatePSBTResponse()
                        {
                            PSBT = PSBT.Parse(psbt, network.NBitcoinNetwork),
                            ChangeAddress = string.IsNullOrEmpty(hintChange) ? null : BitcoinAddress.Create(hintChange, network.NBitcoinNetwork)
                        };


                        derivationSettings.RebaseKeyPaths(psbtResponse.PSBT);

                        signTimeout.CancelAfter(TimeSpan.FromMinutes(5));
                        psbtResponse.PSBT = await hw.SignTransactionAsync(psbtResponse.PSBT, accountKey.GetRootedKeyPath(), accountKey.AccountKey, psbtResponse.ChangeAddress?.ScriptPubKey, signTimeout.Token);
                        result = new SendToAddressResult() { PSBT = psbtResponse.PSBT.ToBase64() };
                    }
                }
                catch (OperationCanceledException)
                { result = new LedgerTestResult() { Success = false, Error = "Timeout" }; }
                catch (Exception ex)
                { result = new LedgerTestResult() { Success = false, Error = ex.Message }; }
                finally { hw.Dispose(); }
                try
                {
                    if (result != null)
                    {
                        UTF8Encoding UTF8NOBOM = new UTF8Encoding(false);
                        var bytes = UTF8NOBOM.GetBytes(JsonConvert.SerializeObject(result, _mvcJsonOptions.Value.SerializerSettings));
                        await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, new CancellationTokenSource(2000).Token);
                    }
                }
                catch { }
                finally
                {
                    await webSocket.CloseSocket();
                }
            }
            return new EmptyResult();
        }

        [Route("{walletId}/settings")]
        public async Task<IActionResult> WalletSettings(
             [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId)
        {
            var derivationSchemeSettings = await GetDerivationSchemeSettings(walletId);
            if (derivationSchemeSettings == null)
                return NotFound();
            var store = (await Repository.FindStore(walletId.StoreId, GetUserId()));
            var vm = new WalletSettingsViewModel()
            {
                Label = derivationSchemeSettings.Label,
                DerivationScheme = derivationSchemeSettings.AccountDerivation.ToString(),
                DerivationSchemeInput = derivationSchemeSettings.AccountOriginal,
                SelectedSigningKey = derivationSchemeSettings.SigningKey.ToString()
            };
            vm.AccountKeys = derivationSchemeSettings.AccountKeySettings
                            .Select(e => new WalletSettingsAccountKeyViewModel()
                            {
                                AccountKey = e.AccountKey.ToString(),
                                MasterFingerprint = e.RootFingerprint is HDFingerprint fp ? fp.ToString() : null,
                                AccountKeyPath = e.AccountKeyPath == null ? "" : $"m/{e.AccountKeyPath}"
                            }).ToList();
            return View(vm);
        }

        [Route("{walletId}/settings")]
        [HttpPost]
        public async Task<IActionResult> WalletSettings(
             [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, WalletSettingsViewModel vm)
        {
            if (!ModelState.IsValid)
                return View(vm);
            var derivationScheme = await GetDerivationSchemeSettings(walletId);
            if (derivationScheme == null)
                return NotFound();
            derivationScheme.Label = vm.Label;
            derivationScheme.SigningKey = new BitcoinExtPubKey(vm.SelectedSigningKey, derivationScheme.Network.NBitcoinNetwork);
            for (int i = 0; i < derivationScheme.AccountKeySettings.Length; i++)
            {
                derivationScheme.AccountKeySettings[i].AccountKeyPath = string.IsNullOrWhiteSpace(vm.AccountKeys[i].AccountKeyPath) ? null
                                                          : new KeyPath(vm.AccountKeys[i].AccountKeyPath);
                derivationScheme.AccountKeySettings[i].RootFingerprint = string.IsNullOrWhiteSpace(vm.AccountKeys[i].MasterFingerprint) ? (HDFingerprint?)null
                                                          : new HDFingerprint(Encoders.Hex.DecodeData(vm.AccountKeys[i].MasterFingerprint));
            }
            var store = (await Repository.FindStore(walletId.StoreId, GetUserId()));
            store.SetSupportedPaymentMethod(derivationScheme);
            await Repository.UpdateStore(store);
            StatusMessage = "Wallet settings updated";
            return RedirectToAction(nameof(WalletSettings));
        }
    }


    public class GetInfoResult
    {
    }

    public class SendToAddressResult
    {
        [JsonProperty("psbt")]
        public string PSBT { get; set; }
    }
}
