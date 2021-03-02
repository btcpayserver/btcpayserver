using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.BIP78.Sender;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.Payment;
using NBXplorer;
using NBXplorer.Models;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers.GreenField
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
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
        private readonly WalletReceiveService _walletReceiveService;

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
            EventAggregator eventAggregator, 
            WalletReceiveService walletReceiveService)
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
            _walletReceiveService = walletReceiveService;
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
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}/wallet/address")]
        public async Task<IActionResult> GetOnChainWalletReceiveAddress(string storeId, string cryptoCode, bool forceGenerate = false)
        {
            if (IsInvalidWalletRequest(cryptoCode, out BTCPayNetwork network,
                out DerivationSchemeSettings derivationScheme, out IActionResult actionResult)) return actionResult;

            var kpi = await _walletReceiveService.GetOrGenerate(new WalletId(storeId, cryptoCode), forceGenerate);
            if (kpi is null)
            {
                return BadRequest();
            }
            return Ok(new OnChainWalletAddressData()
            {
                Address = kpi.Address.ToString(),
                KeyPath = kpi.KeyPath
            });
        }
        
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpDelete("~/api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}/wallet/address")]
        public async Task<IActionResult> UnReserveOnChainWalletReceiveAddress(string storeId, string cryptoCode)
        {
            if (IsInvalidWalletRequest(cryptoCode, out BTCPayNetwork network,
                out DerivationSchemeSettings derivationScheme, out IActionResult actionResult)) return actionResult;

            var addr = await _walletReceiveService.UnReserveAddress(new WalletId(storeId, cryptoCode));
            if (addr is null)
            {
                return NotFound();
            }
            return Ok();
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}/wallet/transactions")]
        public async Task<IActionResult> ShowOnChainWalletTransactions(string storeId, string cryptoCode,
            [FromQuery]TransactionStatus[] statusFilter = null)
        {
            if (IsInvalidWalletRequest(cryptoCode, out BTCPayNetwork network,
                out DerivationSchemeSettings derivationScheme, out IActionResult actionResult)) return actionResult;

            var wallet = _btcPayWalletProvider.GetWallet(network);
            var walletId = new WalletId(storeId, cryptoCode);
            var walletBlobAsync = await _walletRepository.GetWalletInfo(walletId);
            var walletTransactionsInfoAsync = await _walletRepository.GetWalletTransactionsInfo(walletId);

            var txs = await wallet.FetchTransactions(derivationScheme.AccountDerivation);
            var filteredFlatList = new List<TransactionInformation>();
            if (statusFilter is null || !statusFilter.Any() || statusFilter.Contains(TransactionStatus.Confirmed))
            {
                filteredFlatList.AddRange(txs.ConfirmedTransactions.Transactions);
            }

            if (statusFilter is null || !statusFilter.Any() || statusFilter.Contains(TransactionStatus.Unconfirmed))
            {
                filteredFlatList.AddRange(txs.UnconfirmedTransactions.Transactions);
            }

            if (statusFilter is null ||  !statusFilter.Any() ||statusFilter.Contains(TransactionStatus.Replaced))
            {
                filteredFlatList.AddRange(txs.ReplacedTransactions.Transactions);
            }

            var result = filteredFlatList.Select(information =>
            {
                walletTransactionsInfoAsync.TryGetValue(information.TransactionId.ToString(), out var transactionInfo);
                return ToModel(transactionInfo, information, wallet);
            }).ToList();
            return Ok(result);
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
        public async Task<IActionResult> CreateOnChainTransaction(string storeId, string cryptoCode,
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
            if (request.SelectedInputs != null || !utxos.Any())
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

                if (destination.SubtractFromAmount)
                {
                    subtractFeesOutputsCount.Add(index);
                }

                BitcoinUrlBuilder bip21 = null;
                var amount = destination.Amount;
                if (amount.GetValueOrDefault(0) <= 0)
                {
                    amount = null;
                }
                var address = string.Empty; 
                try
                {
                    destination.Destination = destination.Destination.Replace(network.UriScheme+":", "bitcoin:", StringComparison.InvariantCultureIgnoreCase);
                    bip21 = new BitcoinUrlBuilder(destination.Destination, network.NBitcoinNetwork);
                    amount ??= bip21.Amount.GetValue(network);
                    address = bip21.Address.ToString();
                    if (destination.SubtractFromAmount)
                    {
                        request.AddModelError(transactionRequest => transactionRequest.Destinations[index],
                            "You cannot use a BIP21 destination along with SubtractFromAmount", this);
                    }
                }
                catch (FormatException)
                {
                    try
                    {
                        address = BitcoinAddress.Create(destination.Destination, network.NBitcoinNetwork).ToString();
                    }
                    catch (Exception e)
                    {
                        request.AddModelError(transactionRequest => transactionRequest.Destinations[index],
                            "Destination must be a BIP21 payment link or an address", this);
                    }
                }

                if (amount is null || amount <= 0)
                {
                    request.AddModelError(transactionRequest => transactionRequest.Destinations[index],
                        "Amount must be specified or destination must be a BIP21 payment link, and greater than 0", this);
                }
                if (request.ProceedWithPayjoin && bip21?.UnknowParameters?.ContainsKey("pj") is true)
                {
                    payjoinOutputIndex = index;
                }

                outputs.Add(new WalletSendModel.TransactionOutput()
                {
                    DestinationAddress = address,
                    Amount = amount,
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
            if (request.FeeRate is null)
            {
                var feeRate = await explorerClient.GetFeeRateAsync(1);
                request.FeeRate = feeRate.FeeRate;
            }
            else if (request.FeeRate < minRelayFee)
            {
                ModelState.AddModelError(nameof(request.FeeRate),
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
                        FeeSatoshiPerByte = request.FeeRate?.SatoshiPerByte,
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

            var signingKeyStr = await explorerClient
                .GetMetadataAsync<string>(derivationScheme.AccountDerivation,
                    WellknownMetadataKeys.MasterHDKey);
            if (signingKeyStr is null)
            {
                return this.CreateAPIError("not-available",
                    $"{cryptoCode} sending services are not currently available");
            }
            
            var signingKey = ExtKey.Parse(signingKeyStr, network.NBitcoinNetwork);

            var signingKeySettings = derivationScheme.GetSigningAccountKeySettings();
            signingKeySettings.RootFingerprint ??= signingKey.GetPublicKey().GetHDFingerPrint();
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
                        return await GetOnChainWalletTransaction(storeId, cryptoCode, hash.ToString());
                    }
                }
                catch (PayjoinException e)
                {
                }
            }

            if (!request.ProceedWithBroadcast)
            {
                return Ok(transaction.ToHex());
            }

            broadcastResult = await explorerClient.BroadcastAsync(transaction);
            if (broadcastResult.Success)
            {
                return await GetOnChainWalletTransaction(storeId, cryptoCode, transactionHash.ToString());
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
                actionResult = NotFound();
                return true;
            }


            if (!network.WalletSupported || !_btcPayWalletProvider.IsAvailable(network))
            {
                actionResult = this.CreateAPIError("not-available",
                    $"{cryptoCode} services are not currently available");
                return true;
            }

            derivationScheme = GetDerivationSchemeSettings(cryptoCode);
            if (derivationScheme?.AccountDerivation is null)
            {
                actionResult = NotFound();
                return true;
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
                TransactionHash = tx.TransactionId,
                Comment = walletTransactionsInfoAsync?.Comment?? string.Empty,
                Labels = walletTransactionsInfoAsync?.Labels?? new Dictionary<string, LabelData>(),
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
