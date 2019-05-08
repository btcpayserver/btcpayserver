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
            var store = await Repository.FindStore(walletId.StoreId, GetUserId());
            DerivationSchemeSettings paymentMethod = GetPaymentMethod(walletId, store);
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
            DerivationSchemeSettings paymentMethod = GetPaymentMethod(walletId, store);
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

            var sendModel = new WalletSendLedgerModel()
            {
                Destination = vm.Destination,
                Amount = vm.Amount.Value,
                SubstractFees = vm.SubstractFees,
                FeeSatoshiPerByte = vm.FeeSatoshiPerByte,
                NoChange = vm.NoChange,
                DisableRBF = vm.DisableRBF
            };
            if (command == "ledger")
            {
                return RedirectToAction(nameof(WalletSendLedger), sendModel);
            }
            else
            {
                var storeData = (await Repository.FindStore(walletId.StoreId, GetUserId()));
                var derivationScheme = GetPaymentMethod(walletId, storeData);
                try
                {
                    var psbt = await CreatePSBT(network, derivationScheme, sendModel, cancellation);
                    return File(psbt.PSBT.ToBytes(), "application/octet-stream", $"Send-{vm.Amount.Value}-{network.CryptoCode}-to-{destination[0].ToString()}.psbt");
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
            }
        }

        private async Task<CreatePSBTResponse> CreatePSBT(BTCPayNetwork network, DerivationSchemeSettings derivationSettings, WalletSendLedgerModel sendModel, CancellationToken cancellationToken)
        {
            var nbx = ExplorerClientProvider.GetExplorerClient(network);
            CreatePSBTRequest psbtRequest = new CreatePSBTRequest();
            CreatePSBTDestination psbtDestination = new CreatePSBTDestination();
            psbtRequest.Destinations.Add(psbtDestination);
            if (network.SupportRBF)
            {
                psbtRequest.RBF = !sendModel.DisableRBF;
            }
            psbtDestination.Destination = BitcoinAddress.Create(sendModel.Destination, network.NBitcoinNetwork);
            psbtDestination.Amount = Money.Coins(sendModel.Amount);
            psbtRequest.FeePreference = new FeePreference();
            psbtRequest.FeePreference.ExplicitFeeRate = new FeeRate(Money.Satoshis(sendModel.FeeSatoshiPerByte), 1);
            if (sendModel.NoChange)
            {
                psbtRequest.ExplicitChangeAddress = psbtDestination.Destination;
            }
            psbtDestination.SubstractFees = sendModel.SubstractFees;

            var psbt = (await nbx.CreatePSBTAsync(derivationSettings.AccountDerivation, psbtRequest, cancellationToken));
            if (psbt == null)
                throw new NotSupportedException("You need to update your version of NBXplorer");

            if (network.MinFee != null)
            {
                psbt.PSBT.TryGetFee(out var fee);
                if (fee < network.MinFee)
                {
                    psbtRequest.FeePreference = new FeePreference() { ExplicitFee = network.MinFee };
                    psbt = (await nbx.CreatePSBTAsync(derivationSettings.AccountDerivation, psbtRequest, cancellationToken));
                }
            }

            if (derivationSettings.AccountKeyPath != null && derivationSettings.AccountKeyPath.Indexes.Length != 0)
            {
                // NBX only know the path relative to the account xpub.
                // Here we rebase the hd_keys in the PSBT to have a keypath relative to the root HD so the wallet can sign
                // Note that the fingerprint of the hd keys are now 0, which is wrong
                // However, hardware wallets does not give a damn, and sometimes does not even allow us to get this fingerprint anyway.
                foreach (var o in psbt.PSBT.Inputs.OfType<PSBTCoin>().Concat(psbt.PSBT.Outputs))
                {
                    var rootFP = derivationSettings.RootFingerprint is HDFingerprint fp ? fp : default;
                    foreach (var keypath in o.HDKeyPaths.ToList())
                    {
                        var newKeyPath = derivationSettings.AccountKeyPath.Derive(keypath.Value.Item2);
                        o.HDKeyPaths.Remove(keypath.Key);
                        o.HDKeyPaths.Add(keypath.Key, Tuple.Create(rootFP, newKeyPath));
                    }
                }
            }
            return psbt;
        }

        [HttpGet]
        [Route("{walletId}/send/ledger")]
        public async Task<IActionResult> WalletSendLedger(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, WalletSendLedgerModel vm)
        {
            if (walletId?.StoreId == null)
                return NotFound();
            var store = await Repository.FindStore(walletId.StoreId, GetUserId());
            DerivationSchemeSettings paymentMethod = GetPaymentMethod(walletId, store);
            if (paymentMethod == null)
                return NotFound();
            var network = this.NetworkProvider.GetNetwork(walletId?.CryptoCode);
            if (network == null)
                return NotFound();
            return View(vm);
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

        [HttpGet]
        [Route("{walletId}/rescan")]
        public async Task<IActionResult> WalletRescan(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId)
        {
            if (walletId?.StoreId == null)
                return NotFound();
            var store = await Repository.FindStore(walletId.StoreId, GetUserId());
            DerivationSchemeSettings paymentMethod = GetPaymentMethod(walletId, store);
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
            var store = await Repository.FindStore(walletId.StoreId, GetUserId());
            DerivationSchemeSettings paymentMethod = GetPaymentMethod(walletId, store);
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

        private DerivationSchemeSettings GetPaymentMethod(WalletId walletId, StoreData store)
        {
            if (store == null || !store.HasClaim(Policies.CanModifyStoreSettings.Key))
                return null;

            var paymentMethod = store
                            .GetSupportedPaymentMethods(NetworkProvider)
                            .OfType<DerivationSchemeSettings>()
                            .FirstOrDefault(p => p.PaymentId.PaymentType == Payments.PaymentTypes.BTCLike && p.PaymentId.CryptoCode == walletId.CryptoCode);
            return paymentMethod;
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
        [Route("{walletId}/send/ledger/success")]
        public IActionResult WalletSendLedgerSuccess(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId,
            string txid)
        {
            StatusMessage = $"Transaction broadcasted ({txid})";
            return RedirectToAction(nameof(this.WalletTransactions), new { walletId = walletId.ToString() });
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
            bool noChange = false,
            string destination = null, string amount = null, string feeRate = null, bool substractFees = false, bool disableRBF = false
            )
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
                return NotFound();

            var cryptoCode = walletId.CryptoCode;
            var storeData = (await Repository.FindStore(walletId.StoreId, GetUserId()));
            var derivationSettings = GetPaymentMethod(walletId, storeData);

            var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();

            using (var normalOperationTimeout = new CancellationTokenSource())
            using (var signTimeout = new CancellationTokenSource())
            {
                normalOperationTimeout.CancelAfter(TimeSpan.FromMinutes(30));
                var hw = new HardwareWalletService(webSocket);
                var model = new WalletSendLedgerModel();
                object result = null;
                try
                {
                    BTCPayNetwork network = null;
                    if (cryptoCode != null)
                    {
                        network = NetworkProvider.GetNetwork(cryptoCode);
                        if (network == null)
                            throw new FormatException("Invalid value for crypto code");
                    }

                    if (destination != null)
                    {
                        try
                        {
                            BitcoinAddress.Create(destination.Trim(), network.NBitcoinNetwork);
                            model.Destination = destination.Trim();
                        }
                        catch { }
                    }

                    
                    if (feeRate != null)
                    {
                        try
                        {
                            model.FeeSatoshiPerByte = int.Parse(feeRate, CultureInfo.InvariantCulture);
                        }
                        catch { }
                        if (model.FeeSatoshiPerByte <= 0)
                            throw new FormatException("Invalid value for fee rate");
                    }

                    if (amount != null)
                    {
                        try
                        {
                            model.Amount = Money.Parse(amount).ToDecimal(MoneyUnit.BTC);
                        }
                        catch { }
                        if (model.Amount <= 0m)
                            throw new FormatException("Invalid value for amount");
                    }

                    model.SubstractFees = substractFees;
                    model.NoChange = noChange;
                    model.DisableRBF = disableRBF;
                    if (command == "test")
                    {
                        result = await hw.Test(normalOperationTimeout.Token);
                    }
                    if (command == "sendtoaddress")
                    {
                        if (!_dashboard.IsFullySynched(network.CryptoCode, out var summary))
                            throw new Exception($"{network.CryptoCode}: not started or fully synched");

                        

                        var strategy = GetDirectDerivationStrategy(derivationSettings.AccountDerivation);
                        // Some deployment have the wallet root key path saved in the store blob
                        // If it does, we only have to make 1 call to the hw to check if it can sign the given strategy,
                        if (derivationSettings.AccountKeyPath == null || !await hw.CanSign(network, strategy, derivationSettings.AccountKeyPath, normalOperationTimeout.Token))
                        {
                            // If the saved wallet key path is not present or incorrect, let's scan the wallet to see if it can sign strategy
                            var foundKeyPath = await hw.FindKeyPath(network, strategy, normalOperationTimeout.Token);
                            if (foundKeyPath == null)
                                throw new HardwareWalletException($"This store is not configured to use this ledger");
                            derivationSettings.AccountKeyPath = foundKeyPath;
                            storeData.SetSupportedPaymentMethod(derivationSettings);
                            await Repository.UpdateStore(storeData);
                        }


                        var psbt = await CreatePSBT(network, derivationSettings, model, normalOperationTimeout.Token);
                        signTimeout.CancelAfter(TimeSpan.FromMinutes(5));
                        psbt.PSBT = await hw.SignTransactionAsync(psbt.PSBT, psbt.ChangeAddress?.ScriptPubKey, signTimeout.Token);
                        if(!psbt.PSBT.TryFinalize(out var errors))
                        {
                            throw new Exception($"Error while finalizing the transaction ({new PSBTException(errors).ToString()})");
                        }
                        var transaction = psbt.PSBT.ExtractTransaction();
                        try
                        {
                            var broadcastResult = await ExplorerClientProvider.GetExplorerClient(network).BroadcastAsync(transaction);
                            if (!broadcastResult.Success)
                            {
                                throw new Exception($"RPC Error while broadcasting: {broadcastResult.RPCCode} {broadcastResult.RPCCodeMessage} {broadcastResult.RPCMessage}");
                            }
                        }
                        catch (Exception ex)
                        {
                            throw new Exception("Error while broadcasting: " + ex.Message);
                        }
                        var wallet = _walletProvider.GetWallet(network);
                        wallet.InvalidateCache(derivationSettings.AccountDerivation);
                        result = new SendToAddressResult() { TransactionId = transaction.GetHash().ToString() };
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

        private DirectDerivationStrategy GetDirectDerivationStrategy(DerivationStrategyBase strategy)
        {
            if (strategy == null)
                throw new Exception("The derivation scheme is not provided");
            var directStrategy = strategy as DirectDerivationStrategy;
            if (directStrategy == null)
                directStrategy = (strategy as P2SHDerivationStrategy).Inner as DirectDerivationStrategy;
            return directStrategy;
        }
    }


    public class GetInfoResult
    {
    }

    public class SendToAddressResult
    {
        public string TransactionId { get; set; }
    }
}
