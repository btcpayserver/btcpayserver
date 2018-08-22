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
using Newtonsoft.Json;
using static BTCPayServer.Controllers.StoresController;

namespace BTCPayServer.Controllers
{
    [Route("wallets")]
    [Authorize(AuthenticationSchemes = Policies.CookieAuthentication)]
    [AutoValidateAntiforgeryToken]
    public class WalletsController : Controller
    {
        private StoreRepository _Repo;
        private BTCPayNetworkProvider _NetworkProvider;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IOptions<MvcJsonOptions> _mvcJsonOptions;
        private readonly NBXplorerDashboard _dashboard;
        private readonly ExplorerClientProvider _explorerProvider;
        private readonly IFeeProviderFactory _feeRateProvider;
        private readonly BTCPayWalletProvider _walletProvider;
        RateFetcher _RateProvider;
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
            _Repo = repo;
            _RateProvider = rateProvider;
            _NetworkProvider = networkProvider;
            _userManager = userManager;
            _mvcJsonOptions = mvcJsonOptions;
            _dashboard = dashboard;
            _explorerProvider = explorerProvider;
            _feeRateProvider = feeRateProvider;
            _walletProvider = walletProvider;
        }

        public async Task<IActionResult> ListWallets()
        {
            var wallets = new ListWalletsViewModel();
            var stores = await _Repo.GetStoresByUserId(GetUserId());

            var onChainWallets = stores
                                .SelectMany(s => s.GetSupportedPaymentMethods(_NetworkProvider)
                                              .OfType<DerivationStrategy>()
                                              .Select(d => ((Wallet: _walletProvider.GetWallet(d.Network),
                                                            DerivationStrategy: d.DerivationStrategyBase,
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
            var store = await _Repo.FindStore(walletId.StoreId, GetUserId());
            DerivationStrategy paymentMethod = GetPaymentMethod(walletId, store);
            if (paymentMethod == null)
                return NotFound();

            var wallet = _walletProvider.GetWallet(paymentMethod.Network);
            var transactions = await wallet.FetchTransactions(paymentMethod.DerivationStrategyBase);

            var model = new ListTransactionsViewModel();
            foreach(var tx in transactions.UnconfirmedTransactions.Transactions.Concat(transactions.ConfirmedTransactions.Transactions))
            {
                var vm = new ListTransactionsViewModel.TransactionViewModel();
                model.Transactions.Add(vm);
                vm.Id = tx.TransactionId.ToString();
                vm.Link = string.Format(CultureInfo.InvariantCulture, paymentMethod.Network.BlockExplorerLink, vm.Id);
                vm.Timestamp = tx.Timestamp;
                vm.Positive = tx.BalanceChange >= Money.Zero;
                vm.Balance = tx.BalanceChange.ToString();
            }
            model.Transactions = model.Transactions.OrderByDescending(t => t.Timestamp).ToList();
            return View(model);
        }


        [HttpGet]
        [Route("{walletId}/send")]
        public async Task<IActionResult> WalletSend(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId)
        {
            if (walletId?.StoreId == null)
                return NotFound();
            var store = await _Repo.FindStore(walletId.StoreId, GetUserId());
            DerivationStrategy paymentMethod = GetPaymentMethod(walletId, store);
            if (paymentMethod == null)
                return NotFound();

            var storeData = store.GetStoreBlob();
            var rateRules = store.GetStoreBlob().GetRateRules(_NetworkProvider);
            rateRules.Spread = 0.0m;
            var currencyPair = new Rating.CurrencyPair(paymentMethod.PaymentId.CryptoCode, GetCurrencyCode(storeData.DefaultLang) ?? "USD");
            WalletModel model = new WalletModel();
            model.ServerUrl = GetLedgerWebsocketUrl(this.HttpContext, walletId.CryptoCode, paymentMethod.DerivationStrategyBase);
            model.CryptoCurrency = walletId.CryptoCode;

            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                try
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(5));
                    var result = await _RateProvider.FetchRate(currencyPair, rateRules).WithCancellation(cts.Token);
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
                catch(Exception ex) { model.RateError = ex.Message; }
            }
            return View(model);
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
            catch(ArgumentException) { }
            return null;
        }

        private DerivationStrategy GetPaymentMethod(WalletId walletId, StoreData store)
        {
            if (store == null || !store.HasClaim(Policies.CanModifyStoreSettings.Key))
                return null;

            var paymentMethod = store
                            .GetSupportedPaymentMethods(_NetworkProvider)
                            .OfType<DerivationStrategy>()
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

        public static string GetLedgerWebsocketUrl(HttpContext httpContext, string cryptoCode, DerivationStrategyBase derivationStrategy)
        {
            return $"{httpContext.Request.GetAbsoluteRoot().WithTrailingSlash()}ws/ledger/{cryptoCode}/{derivationStrategy?.ToString() ?? string.Empty}";
        }

        [HttpGet]
        [Route("/ws/ledger/{cryptoCode}/{derivationScheme?}")]
        public async Task<IActionResult> LedgerConnection(
            string command,
            // getinfo
            string cryptoCode = null,
            // getxpub
            [ModelBinder(typeof(ModelBinders.DerivationSchemeModelBinder))]
            DerivationStrategyBase derivationScheme = null,
            int account = 0,
            // sendtoaddress
            string destination = null, string amount = null, string feeRate = null, string substractFees = null
            )
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
                return NotFound();
            var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();

            using (var normalOperationTimeout = new CancellationTokenSource())
            using (var signTimeout = new CancellationTokenSource())
            {
                normalOperationTimeout.CancelAfter(TimeSpan.FromMinutes(30));
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
                            destinationAddress = BitcoinAddress.Create(destination, network.NBitcoinNetwork);
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
                        catch { throw new FormatException("Invalid value for subtract fees"); }
                    }
                    if (command == "test")
                    {
                        result = await hw.Test(normalOperationTimeout.Token);
                    }
                    if (command == "getxpub")
                    {
                        var getxpubResult = await hw.GetExtPubKey(network, account, normalOperationTimeout.Token);
                        result = getxpubResult;
                    }
                    if (command == "getinfo")
                    {
                        var strategy = GetDirectDerivationStrategy(derivationScheme);
                        if (strategy == null || await hw.GetKeyPath(network, strategy, normalOperationTimeout.Token) == null)
                        {
                            throw new Exception($"This store is not configured to use this ledger");
                        }

                        var feeProvider = _feeRateProvider.CreateFeeProvider(network);
                        var recommendedFees = feeProvider.GetFeeRateAsync();
                        var balance = _walletProvider.GetWallet(network).GetBalance(derivationScheme);
                        result = new GetInfoResult() { Balance = (double)(await balance).ToDecimal(MoneyUnit.BTC), RecommendedSatoshiPerByte = (int)(await recommendedFees).GetFee(1).Satoshi };
                    }

                    if (command == "sendtoaddress")
                    {
                        if (!_dashboard.IsFullySynched(network.CryptoCode, out var summary))
                            throw new Exception($"{network.CryptoCode}: not started or fully synched");
                        var strategy = GetDirectDerivationStrategy(derivationScheme);
                        var wallet = _walletProvider.GetWallet(network);
                        var change = wallet.GetChangeAddressAsync(derivationScheme);

                        var unspentCoins = await wallet.GetUnspentCoins(derivationScheme);
                        var changeAddress = await change;
                        var send = new[] { (
                        destination: destinationAddress as IDestination,
                        amount: amountBTC,
                        substractFees: subsctractFeesValue) };

                        foreach (var element in send)
                        {
                            if (element.destination == null)
                                throw new ArgumentNullException(nameof(element.destination));
                            if (element.amount == null)
                                throw new ArgumentNullException(nameof(element.amount));
                            if (element.amount <= Money.Zero)
                                throw new ArgumentOutOfRangeException(nameof(element.amount), "The amount should be above zero");
                        }

                        var foundKeyPath = await hw.GetKeyPath(network, strategy, normalOperationTimeout.Token);
                        if (foundKeyPath == null)
                        {
                            throw new HardwareWalletException($"This store is not configured to use this ledger");
                        }

                        TransactionBuilder builder = new TransactionBuilder();
                        builder.StandardTransactionPolicy.MinRelayTxFee = summary.Status.BitcoinStatus.MinRelayTxFee;
                        builder.SetConsensusFactory(network.NBitcoinNetwork);
                        builder.AddCoins(unspentCoins.Select(c => c.Coin).ToArray());

                        foreach (var element in send)
                        {
                            builder.Send(element.destination, element.amount);
                            if (element.substractFees)
                                builder.SubtractFees();
                        }
                        builder.SetChange(changeAddress.Item1);

                        if (network.MinFee == null)
                        {
                            builder.SendEstimatedFees(feeRateValue);
                        }
                        else
                        {
                            var estimatedFee = builder.EstimateFees(feeRateValue);
                            if (network.MinFee > estimatedFee)
                                builder.SendFees(network.MinFee);
                            else
                                builder.SendEstimatedFees(feeRateValue);
                        }
                        builder.Shuffle();
                        var unsigned = builder.BuildTransaction(false);

                        var keypaths = new Dictionary<Script, KeyPath>();
                        foreach (var c in unspentCoins)
                        {
                            keypaths.TryAdd(c.Coin.ScriptPubKey, c.KeyPath);
                        }

                        var hasChange = unsigned.Outputs.Count == 2;
                        var usedCoins = builder.FindSpentCoins(unsigned);

                        Dictionary<uint256, Transaction> parentTransactions = new Dictionary<uint256, Transaction>();

                        if (!strategy.Segwit)
                        {
                            var parentHashes = usedCoins.Select(c => c.Outpoint.Hash).ToHashSet();
                            var explorer = _explorerProvider.GetExplorerClient(network);
                            var getTransactionAsyncs = parentHashes.Select(h => (Op: explorer.GetTransactionAsync(h), Hash: h)).ToList();
                            foreach (var getTransactionAsync in getTransactionAsyncs)
                            {
                                var tx = (await getTransactionAsync.Op);
                                if (tx == null)
                                    throw new Exception($"Parent transaction {getTransactionAsync.Hash} not found");
                                parentTransactions.Add(tx.Transaction.GetHash(), tx.Transaction);
                            }
                        }


                        signTimeout.CancelAfter(TimeSpan.FromMinutes(5));
                        var transaction = await hw.SignTransactionAsync(usedCoins.Select(c => new SignatureRequest
                        {
                            InputTransaction = parentTransactions.TryGet(c.Outpoint.Hash),
                            InputCoin = c,
                            KeyPath = foundKeyPath.Derive(keypaths[c.TxOut.ScriptPubKey]),
                            PubKey = strategy.Root.Derive(keypaths[c.TxOut.ScriptPubKey]).PubKey
                        }).ToArray(), unsigned, hasChange ? foundKeyPath.Derive(changeAddress.Item2) : null, signTimeout.Token);
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
                        wallet.InvalidateCache(derivationScheme);
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
        public int RecommendedSatoshiPerByte { get; set; }
        public double Balance { get; set; }
    }

    public class SendToAddressResult
    {
        public string TransactionId { get; set; }
    }
}
