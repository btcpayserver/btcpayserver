using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Labels;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.Payment;
using NBXplorer;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers.GreenField
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public class StoreOnChainWalletsController : Controller
    {
        private StoreData Store => HttpContext.GetStoreData();
        private readonly IAuthorizationService _authorizationService;
        private readonly BTCPayWalletProvider _btcPayWalletProvider;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly WalletRepository _walletRepository;
        private readonly ExplorerClientProvider _explorerClientProvider;
        private readonly CssThemeManager _cssThemeManager;
        private readonly NBXplorerDashboard _nbXplorerDashboard;
        private readonly WalletsController _walletsController;
        private readonly PayjoinClient _payjoinClient;
        private readonly DelayedTransactionBroadcaster _delayedTransactionBroadcaster;
        private readonly EventAggregator _eventAggregator;

        public StoreOnChainWalletsController(
            IAuthorizationService authorizationService,
            BTCPayWalletProvider btcPayWalletProvider,
            BTCPayNetworkProvider btcPayNetworkProvider,
            WalletRepository walletRepository,
            ExplorerClientProvider explorerClientProvider,
            CssThemeManager cssThemeManager,
            NBXplorerDashboard nbXplorerDashboard,
            WalletsController walletsController,
            PayjoinClient payjoinClient,
            DelayedTransactionBroadcaster delayedTransactionBroadcaster,
            EventAggregator eventAggregator)
        {
            _authorizationService = authorizationService;
            _btcPayWalletProvider = btcPayWalletProvider;
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _walletRepository = walletRepository;
            _explorerClientProvider = explorerClientProvider;
            _cssThemeManager = cssThemeManager;
            _nbXplorerDashboard = nbXplorerDashboard;
            _walletsController = walletsController;
            _payjoinClient = payjoinClient;
            _delayedTransactionBroadcaster = delayedTransactionBroadcaster;
            _eventAggregator = eventAggregator;
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}/wallet")]
        public async Task<IActionResult> ShowOnChainWalletOverview(string storeId, string cryptoCode)
        {
            if (IsInvalidWalletRequest(cryptoCode, out BTCPayNetwork network,
                out DerivationSchemeSettings derivationScheme, out IActionResult actionResult)) return actionResult;

            var wallet = _btcPayWalletProvider.GetWallet(network);
            return Ok(new OnChainWalletOverviewData()
            {
                Balance = await wallet.GetBalance(derivationScheme.AccountDerivation)
            });
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}/wallet/transactions")]
        public async Task<IActionResult> ShowOnChainWalletTransactions(string storeId, string cryptoCode,
            TransactionStatus[] statusFilter = null)
        {
            if (IsInvalidWalletRequest(cryptoCode, out BTCPayNetwork network,
                out DerivationSchemeSettings derivationScheme, out IActionResult actionResult)) return actionResult;

            var wallet = _btcPayWalletProvider.GetWallet(network);
            var walletId = new WalletId(storeId, cryptoCode);
            var walletBlobAsync = await _walletRepository.GetWalletInfo(walletId);
            var walletTransactionsInfoAsync = await _walletRepository.GetWalletTransactionsInfo(walletId);

            var txs = await wallet.FetchTransactions(derivationScheme.AccountDerivation);
            var filteredFlatList = new List<TransactionInformation>();
            if (statusFilter is null || statusFilter.Contains(TransactionStatus.Confirmed))
            {
                filteredFlatList.AddRange(txs.ConfirmedTransactions.Transactions);
            }

            if (statusFilter is null || statusFilter.Contains(TransactionStatus.Unconfirmed))
            {
                filteredFlatList.AddRange(txs.UnconfirmedTransactions.Transactions);
            }

            if (statusFilter is null || statusFilter.Contains(TransactionStatus.Replaced))
            {
                filteredFlatList.AddRange(txs.ReplacedTransactions.Transactions);
            }


            return Ok(filteredFlatList.Select(information =>
            {
                walletTransactionsInfoAsync.TryGetValue(information.TransactionId.ToString(), out var transactionInfo);
                return ToModel(transactionInfo, information, wallet);
            }).ToList());
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}/wallet/transactions/{transactionId}")]
        public async Task<IActionResult> GetOnChainWalletTransaction(string storeId, string cryptoCode,
            string transactionId)
        {
            if (IsInvalidWalletRequest(cryptoCode, out BTCPayNetwork network,
                out DerivationSchemeSettings derivationScheme, out IActionResult actionResult)) return actionResult;

            var wallet = _btcPayWalletProvider.GetWallet(network);
            var tx = await wallet.FetchTransaction(derivationScheme.AccountDerivation, uint256.Parse(transactionId));
            if (tx is null)
            {
                return NotFound();
            }

            var walletId = new WalletId(storeId, cryptoCode);
            var walletTransactionsInfoAsync =
                (await _walletRepository.GetWalletTransactionsInfo(walletId, new[] {transactionId})).Values
                .FirstOrDefault();

            return Ok(ToModel(walletTransactionsInfoAsync, tx, wallet));
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}/wallet/utxos")]
        public async Task<IActionResult> GetOnChainWalletUTXOs(string storeId, string cryptoCode)
        {
            if (IsInvalidWalletRequest(cryptoCode, out BTCPayNetwork network,
                out DerivationSchemeSettings derivationScheme, out IActionResult actionResult)) return actionResult;

            var wallet = _btcPayWalletProvider.GetWallet(network);

            var walletId = new WalletId(storeId, cryptoCode);
            var walletTransactionsInfoAsync = await _walletRepository.GetWalletTransactionsInfo(walletId);
            var utxos = await wallet.GetUnspentCoins(derivationScheme.AccountDerivation);
            return Ok(utxos.Select(coin =>
                {
                    walletTransactionsInfoAsync.TryGetValue(coin.OutPoint.Hash.ToString(), out var info);
                    return new OnChainWalletUTXOData()
                    {
                        Outpoint = coin.OutPoint,
                        Amount = coin.Value.GetValue(network),
                        Comment = info?.Comment,
                        Labels = info?.Labels,
                        Link = string.Format(CultureInfo.InvariantCulture, network.BlockExplorerLink,
                            coin.OutPoint.Hash.ToString())
                    };
                }).ToList()
            );
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}/wallet/transactions")]
        public async Task<IActionResult> CreateOnChainTransaction(string cryptoCode,
            [FromBody] CreateOnChainTransactionRequest request)
        {
            if (IsInvalidWalletRequest(cryptoCode, out BTCPayNetwork network,
                out DerivationSchemeSettings derivationScheme, out IActionResult actionResult)) return actionResult;
            if (network.ReadonlyWallet)
            {
                return this.CreateAPIError("not-available",
                    $"{cryptoCode} sending services are not currently available");
            }

            //This API is only meant for hot wallet usage for now. We can expand later when we allow PSBT manipulation.
            if (!(await CanUseHotWallet()).HotWallet)
            {
                return Unauthorized();
            }

            var explorerClient = _explorerClientProvider.GetExplorerClient(cryptoCode);
            var wallet = _btcPayWalletProvider.GetWallet(network);

            var utxos = await wallet.GetUnspentCoins(derivationScheme.AccountDerivation);
            if (request.SelectedInputs?.Any() is true || !utxos.Any())
            {
                utxos = utxos.Where(coin => request.SelectedInputs?.Contains(coin.OutPoint) ?? true)
                    .ToArray();
                if (utxos.Any() is false)
                {
                    //no valid utxos selected
                    request.AddModelError(transactionRequest => transactionRequest.SelectedInputs,
                        "There are no available utxos based on your request", this);
                }
            }

            var balanceAvailable = utxos.Sum(coin => coin.Value.GetValue(network));

            var subtractFeesOutputsCount = new List<int>();
            var subtractFees = request.Destinations.Any(o => o.SubtractFromAmount);
            int? payjoinOutputIndex = null;
            var sum = 0m;
            var outputs = new List<WalletSendModel.TransactionOutput>();
            for (var index = 0; index < request.Destinations.Count; index++)
            {
                var destination = request.Destinations[index];

                var address = destination.Destination;
                if (destination.SubtractFromAmount)
                {
                    subtractFeesOutputsCount.Add(index);
                }

                if (destination.Amount is null)
                {
                    //it is a bip21
                    try
                    {
                        var bip21 = new BitcoinUrlBuilder(destination.Destination, network.NBitcoinNetwork);
                        address = bip21.Address.ToString();
                        if (destination.SubtractFromAmount)
                        {
                            request.AddModelError(transactionRequest => transactionRequest.Destinations[index],
                                "You cannot use a BIP21 destination along with SubtractFromAmount", this);
                            //cant subtract from amount if using bip21
                        }

                        var amount = bip21.Amount.GetValue(network);
                        destination.Amount = amount;
                        if (request.ProceedWithPayjoin && bip21.UnknowParameters.ContainsKey("pj"))
                        {
                            payjoinOutputIndex = index;
                        }
                    }

                    catch (FormatException e)
                    {
                        request.AddModelError(transactionRequest => transactionRequest.Destinations[index],
                            "Destination must be a BIP21 payment link or an address", this);
                    }
                }
                else if (destination.Amount > 0)
                {
                    try
                    {
                        BitcoinAddress.Create(destination.Destination, network.NBitcoinNetwork);
                        address = destination.Destination;
                    }
                    catch (FormatException formatException)
                    {
                        request.AddModelError(transactionRequest => transactionRequest.Destinations[index],
                            "Destination must be a BIP21 payment link or an address", this);
                    }
                }
                else if (destination.Amount <= 0)
                {
                    request.AddModelError(transactionRequest => transactionRequest.Destinations[index].Amount,
                        "Amount must be greater than 0", this);
                }

                outputs.Add(new WalletSendModel.TransactionOutput()
                {
                    DestinationAddress = address,
                    Amount = destination.Amount,
                    SubtractFeesFromOutput = destination.SubtractFromAmount
                });
                sum += destination.Amount ?? 0;
            }

            if (subtractFeesOutputsCount.Count > 1)
            {
                foreach (var subtractFeesOutput in subtractFeesOutputsCount)
                {
                    request.AddModelError(model => model.Destinations[subtractFeesOutput].SubtractFromAmount,
                        "You can only subtract fees from one destination", this);
                }
            }

            if (balanceAvailable < sum)
            {
                request.AddModelError(transactionRequest => transactionRequest.Destinations,
                    "You are attempting to send more than is available", this);
            }
            else if (balanceAvailable == sum && !subtractFees)
            {
                request.AddModelError(transactionRequest => transactionRequest.Destinations,
                    "You are sending your entire balance, you should subtract the fees from a destination", this);
            }

            var minRelayFee = this._nbXplorerDashboard.Get(network.CryptoCode).Status.BitcoinStatus?.MinRelayTxFee ??
                              new FeeRate(1.0m);
            if (request.FeeSatoshiPerByte is null)
            {
                var feeRate = await explorerClient.GetFeeRateAsync(1);
                request.FeeSatoshiPerByte = feeRate.FeeRate;
            }
            else if (request.FeeSatoshiPerByte < minRelayFee)
            {
                ModelState.AddModelError(nameof(request.FeeSatoshiPerByte),
                    "The fee rate specified is lower than the current minimum relay fee");
            }

            if (!ModelState.IsValid)
            {
                return this.CreateValidationError(ModelState);
            }

            CreatePSBTResponse psbt;
            try
            {
                psbt = await _walletsController.CreatePSBT(network, derivationScheme,
                    new WalletSendModel()
                    {
                        SelectedInputs = request.SelectedInputs?.Select(point => point.ToString()),
                        Outputs = outputs,
                        AlwaysIncludeNonWitnessUTXO = true,
                        InputSelection = request.SelectedInputs?.Any() is true,
                        AllowFeeBump =
                            !request.RBF.HasValue ? WalletSendModel.ThreeStateBool.Maybe :
                            request.RBF.Value ? WalletSendModel.ThreeStateBool.Yes :
                            WalletSendModel.ThreeStateBool.No,
                        FeeSatoshiPerByte = request.FeeSatoshiPerByte?.SatoshiPerByte,
                        NoChange = request.NoChange
                    },
                    CancellationToken.None);
            }
            catch (NBXplorerException ex)
            {
                return this.CreateAPIError(ex.Error.Code, ex.Error.Message);
            }
            catch (NotSupportedException)
            {
                return this.CreateAPIError("not-available", "You need to update your version of NBXplorer");
            }

            derivationScheme.RebaseKeyPaths(psbt.PSBT);

            var signingContext = new SigningContextModel()
            {
                PayJoinBIP21 =
                    payjoinOutputIndex is null
                        ? null
                        : request.Destinations.ElementAt(payjoinOutputIndex.Value).Destination,
                EnforceLowR = psbt.Suggestions?.ShouldEnforceLowR,
                ChangeAddress = psbt.ChangeAddress?.ToString()
            };

            var signingKey = ExtKey.Parse(await explorerClient
                .GetMetadataAsync<string>(derivationScheme.AccountDerivation,
                    WellknownMetadataKeys.MasterHDKey), network.NBitcoinNetwork);

            var signingKeySettings = derivationScheme.GetSigningAccountKeySettings();
            if (signingKeySettings.RootFingerprint is null)
                signingKeySettings.RootFingerprint = signingKey.GetPublicKey().GetHDFingerPrint();
            RootedKeyPath rootedKeyPath = signingKeySettings.GetRootedKeyPath();
            psbt.PSBT.RebaseKeyPaths(signingKeySettings.AccountKey, rootedKeyPath);
            var accountKey = signingKey.Derive(rootedKeyPath.KeyPath);

            var changed = psbt.PSBT.PSBTChanged(() => psbt.PSBT.SignAll(derivationScheme.AccountDerivation, accountKey,
                rootedKeyPath, new SigningOptions() {EnforceLowR = !(signingContext?.EnforceLowR is false)}));

            if (!changed)
            {
                return this.CreateAPIError("psbt-signing-error",
                    "Impossible to sign the transaction. Probable cause: Incorrect account key path in wallet settings, PSBT already signed.");
            }

            psbt.PSBT.Finalize();
            var transaction = psbt.PSBT.ExtractTransaction();
            var transactionHash = transaction.GetHash();
            BroadcastResult broadcastResult;
            if (!string.IsNullOrEmpty(signingContext.PayJoinBIP21))
            {
                signingContext.OriginalPSBT = psbt.PSBT.ToBase64();
                try
                {
                    await _delayedTransactionBroadcaster.Schedule(DateTimeOffset.UtcNow + TimeSpan.FromMinutes(2.0),
                        transaction, network);
                    var payjoinPSBT = await _payjoinClient.RequestPayjoin(
                        new BitcoinUrlBuilder(signingContext.PayJoinBIP21, network.NBitcoinNetwork), derivationScheme,
                        psbt.PSBT, CancellationToken.None);
                    payjoinPSBT = psbt.PSBT.SignAll(derivationScheme.AccountDerivation, accountKey, rootedKeyPath,
                        new SigningOptions() {EnforceLowR = !(signingContext?.EnforceLowR is false)});
                    payjoinPSBT.Finalize();
                    var payjoinTransaction = payjoinPSBT.ExtractTransaction();
                    var hash = payjoinTransaction.GetHash();
                    _eventAggregator.Publish(new UpdateTransactionLabel(new WalletId(Store.Id, cryptoCode), hash,
                        UpdateTransactionLabel.PayjoinLabelTemplate()));
                    broadcastResult = await explorerClient.BroadcastAsync(payjoinTransaction);
                    if (broadcastResult.Success)
                    {
                        return RedirectToAction("GetOnChainWalletTransaction",
                            new {storeId = Store.Id, cryptoCode = cryptoCode, transactionId = hash.ToString()});
                    }
                }
                catch (PayjoinException e)
                {
                }
            }

            broadcastResult = await explorerClient.BroadcastAsync(transaction);
            if (broadcastResult.Success)
            {
                return RedirectToAction("GetOnChainWalletTransaction",
                    new {storeId = Store.Id, cryptoCode = cryptoCode, transactionId = transactionHash.ToString()});
            }
            else
            {
                return this.CreateAPIError("broadcast-error", broadcastResult.RPCMessage);
            }
        }

        private async Task<(bool HotWallet, bool RPCImport)> CanUseHotWallet()
        {
            return await _authorizationService.CanUseHotWallet(_cssThemeManager.Policies, User);
        }

        private async Task<ExtKey> GetWallet(DerivationSchemeSettings derivationScheme)
        {
            if (!derivationScheme.IsHotWallet)
                return null;

            var result = await _explorerClientProvider.GetExplorerClient(derivationScheme.Network.CryptoCode)
                .GetMetadataAsync<string>(derivationScheme.AccountDerivation,
                    WellknownMetadataKeys.MasterHDKey);
            return string.IsNullOrEmpty(result) ? null : ExtKey.Parse(result, derivationScheme.Network.NBitcoinNetwork);
        }

        private bool IsInvalidWalletRequest(string cryptoCode, out BTCPayNetwork network,
            out DerivationSchemeSettings derivationScheme, out IActionResult actionResult)
        {
            derivationScheme = null;
            actionResult = null;
            network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
            if (network is null)
            {
                {
                    actionResult = NotFound();
                    return true;
                }
            }

            if (!network.WalletSupported || !_btcPayWalletProvider.IsAvailable(network))
            {
                {
                    actionResult = this.CreateAPIError("not-available",
                        $"{cryptoCode} services are not currently available");
                    return true;
                }
            }

            derivationScheme = GetDerivationSchemeSettings(cryptoCode);
            if (derivationScheme?.AccountDerivation is null)
            {
                {
                    actionResult = NotFound();
                    return true;
                }
            }

            return false;
        }

        private DerivationSchemeSettings GetDerivationSchemeSettings(string cryptoCode)
        {
            var paymentMethod = Store
                .GetSupportedPaymentMethods(_btcPayNetworkProvider)
                .OfType<DerivationSchemeSettings>()
                .FirstOrDefault(p =>
                    p.PaymentId.PaymentType == Payments.PaymentTypes.BTCLike &&
                    p.PaymentId.CryptoCode.Equals(cryptoCode, StringComparison.InvariantCultureIgnoreCase));
            return paymentMethod;
        }

        private OnChainWalletTransactionData ToModel(WalletTransactionInfo walletTransactionsInfoAsync,
            TransactionInformation tx,
            BTCPayWallet wallet)
        {
            return new OnChainWalletTransactionData()
            {
                Comment = walletTransactionsInfoAsync?.Comment,
                Labels = walletTransactionsInfoAsync?.Labels,
                Amount = tx.BalanceChange.GetValue(wallet.Network),
                BlockHash = tx.BlockHash,
                BlockHeight = tx.Height,
                Confirmations = tx.Confirmations,
                Timestamp = tx.Timestamp,
                Status = tx.Confirmations > 0 ? TransactionStatus.Confirmed :
                    tx.ReplacedBy != null ? TransactionStatus.Replaced : TransactionStatus.Unconfirmed
            };
        }
    }
}
