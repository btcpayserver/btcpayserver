#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.BIP78.Sender;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Payments.PayJoin;
using BTCPayServer.Payments.PayJoin.Sender;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Labels;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitcoin.Payment;
using NBXplorer;
using NBXplorer.Models;
using Newtonsoft.Json.Linq;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers.Greenfield
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldStoreOnChainWalletsController : Controller
    {
        private StoreData Store => HttpContext.GetStoreData();

        public PoliciesSettings PoliciesSettings { get; }

        private readonly IAuthorizationService _authorizationService;
        private readonly BTCPayWalletProvider _btcPayWalletProvider;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly WalletRepository _walletRepository;
        private readonly ExplorerClientProvider _explorerClientProvider;
        private readonly NBXplorerDashboard _nbXplorerDashboard;
        private readonly UIWalletsController _walletsController;
        private readonly PayjoinClient _payjoinClient;
        private readonly DelayedTransactionBroadcaster _delayedTransactionBroadcaster;
        private readonly EventAggregator _eventAggregator;
        private readonly WalletReceiveService _walletReceiveService;
        private readonly IFeeProviderFactory _feeProviderFactory;
        private readonly UTXOLocker _utxoLocker;
        private readonly TransactionLinkProviders _transactionLinkProviders;

        public GreenfieldStoreOnChainWalletsController(
            IAuthorizationService authorizationService,
            BTCPayWalletProvider btcPayWalletProvider,
            PaymentMethodHandlerDictionary handlers,
            WalletRepository walletRepository,
            ExplorerClientProvider explorerClientProvider,
            NBXplorerDashboard nbXplorerDashboard,
            PoliciesSettings policiesSettings,
            UIWalletsController walletsController,
            PayjoinClient payjoinClient,
            DelayedTransactionBroadcaster delayedTransactionBroadcaster,
            EventAggregator eventAggregator,
            WalletReceiveService walletReceiveService,
            IFeeProviderFactory feeProviderFactory,
            UTXOLocker utxoLocker,
            TransactionLinkProviders transactionLinkProviders
        )
        {
            _authorizationService = authorizationService;
            _btcPayWalletProvider = btcPayWalletProvider;
            _handlers = handlers;
            _walletRepository = walletRepository;
            _explorerClientProvider = explorerClientProvider;
            PoliciesSettings = policiesSettings;
            _nbXplorerDashboard = nbXplorerDashboard;
            _walletsController = walletsController;
            _payjoinClient = payjoinClient;
            _delayedTransactionBroadcaster = delayedTransactionBroadcaster;
            _eventAggregator = eventAggregator;
            _walletReceiveService = walletReceiveService;
            _feeProviderFactory = feeProviderFactory;
            _utxoLocker = utxoLocker;
            _transactionLinkProviders = transactionLinkProviders;
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/{paymentMethodId}/wallet")]
        public async Task<IActionResult> ShowOnChainWalletOverview(string storeId, string paymentMethodId)
        {
            if (IsInvalidWalletRequest(paymentMethodId, out var network,
                    out var derivationScheme, out var actionResult))
                return actionResult;

            var wallet = _btcPayWalletProvider.GetWallet(network);
            var balance = await wallet.GetBalance(derivationScheme.AccountDerivation);

            return Ok(new OnChainWalletOverviewData()
            {
                Label = derivationScheme.ToPrettyString(),
                Balance = balance.Total.GetValue(network),
                UnconfirmedBalance = balance.Unconfirmed.GetValue(network),
                ConfirmedBalance = balance.Confirmed.GetValue(network),
            });
        }

        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/{paymentMethodId}/wallet/feerate")]
        public async Task<IActionResult> GetOnChainFeeRate(string storeId, string paymentMethodId, int? blockTarget = null)
        {
            if (IsInvalidWalletRequest(paymentMethodId, out var network,
                    out _, out var actionResult))
                return actionResult;

            var feeRateTarget = blockTarget ?? Store.GetStoreBlob().RecommendedFeeBlockTarget;
            return Ok(new OnChainWalletFeeRateData()
            {
                FeeRate = await _feeProviderFactory.CreateFeeProvider(network)
                    .GetFeeRateAsync(feeRateTarget),
            });
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/{paymentMethodId}/wallet/address")]
        public async Task<IActionResult> GetOnChainWalletReceiveAddress(string storeId, string paymentMethodId,
            bool forceGenerate = false)
        {
            if (IsInvalidWalletRequest(paymentMethodId, out var network,
                    out var derivationScheme, out var actionResult))
                return actionResult;

            var kpi = await _walletReceiveService.GetOrGenerate(new WalletId(storeId, network.CryptoCode), forceGenerate);
            if (kpi is null)
            {
                return BadRequest();
            }

            var bip21 = network.GenerateBIP21(kpi.Address?.ToString(), null);
            var allowedPayjoin = derivationScheme.IsHotWallet && Store.GetStoreBlob().PayJoinEnabled;
            if (allowedPayjoin)
            {
                bip21.QueryParams.Add(PayjoinClient.BIP21EndpointKey,
                    Request.GetAbsoluteUri(Url.Action(nameof(PayJoinEndpointController.Submit), "PayJoinEndpoint",
                        new { network.CryptoCode })));
            }

            return Ok(new OnChainWalletAddressData()
            {
                Address = kpi.Address?.ToString(),
                PaymentLink = bip21.ToString(),
                KeyPath = kpi.KeyPath
            });
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpDelete("~/api/v1/stores/{storeId}/payment-methods/{paymentMethodId}/wallet/address")]
        public async Task<IActionResult> UnReserveOnChainWalletReceiveAddress(string storeId, string paymentMethodId)
        {
            if (IsInvalidWalletRequest(paymentMethodId, out var network,
                    out _, out var actionResult))
                return actionResult;

            var addr = await _walletReceiveService.UnReserveAddress(new WalletId(storeId, network.CryptoCode));
            if (addr is null)
            {
                return this.CreateAPIError("no-reserved-address",
                    $"There was no reserved address for {network.CryptoCode} on this store.");
            }

            return Ok();
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/{paymentMethodId}/wallet/transactions")]
        public async Task<IActionResult> ShowOnChainWalletTransactions(
            string storeId,
            string paymentMethodId,
            [FromQuery] TransactionStatus[]? statusFilter = null,
            [FromQuery] string? labelFilter = null,
            [FromQuery] int skip = 0,
            [FromQuery] int limit = int.MaxValue,
            CancellationToken cancellationToken = default
        )
        {
            if (IsInvalidWalletRequest(paymentMethodId, out var network,
                    out var derivationScheme, out var actionResult))
                return actionResult;

            var wallet = _btcPayWalletProvider.GetWallet(network);
            var walletId = new WalletId(storeId, network.CryptoCode);
            var walletTransactionsInfoAsync = await _walletRepository.GetWalletTransactionsInfo(walletId, (string[]?)null);

            var preFiltering = true;
            if (statusFilter?.Any() is true || !string.IsNullOrWhiteSpace(labelFilter))
                preFiltering = false;
            var txs = await wallet.FetchTransactionHistory(derivationScheme.AccountDerivation, preFiltering ? skip : 0,
                preFiltering ? limit : int.MaxValue, cancellationToken: cancellationToken);
            if (!preFiltering)
            {
                var filteredList = new List<TransactionHistoryLine>(txs.Count);
                foreach (var t in txs)
                {
                    if (!string.IsNullOrWhiteSpace(labelFilter))
                    {
                        walletTransactionsInfoAsync.TryGetValue(t.TransactionId.ToString(), out var transactionInfo);
                        if (transactionInfo?.LabelColors.ContainsKey(labelFilter) is true)
                            filteredList.Add(t);
                    }

                    if (statusFilter?.Any() is true)
                    {
                        if (statusFilter.Contains(TransactionStatus.Confirmed) && t.Confirmations != 0)
                            filteredList.Add(t);
                        else if (statusFilter.Contains(TransactionStatus.Unconfirmed) && t.Confirmations == 0)
                            filteredList.Add(t);
                    }
                }

                txs = filteredList;
            }

            var result = txs.Skip(skip).Take(limit).Select(information =>
            {
                walletTransactionsInfoAsync.TryGetValue(information.TransactionId.ToString(), out var transactionInfo);
                return ToModel(transactionInfo, information, wallet);
            }).ToList();
            return Ok(result);
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/{paymentMethodId}/wallet/transactions/{transactionId}")]
        public async Task<IActionResult> GetOnChainWalletTransaction(string storeId, string paymentMethodId,
            string transactionId)
        {
            if (IsInvalidWalletRequest(paymentMethodId, out var network,
                    out var derivationScheme, out var actionResult))
                return actionResult;

            var wallet = _btcPayWalletProvider.GetWallet(network);
            var tx = await wallet.FetchTransaction(derivationScheme.AccountDerivation, uint256.Parse(transactionId));
            if (tx is null)
            {
                return this.CreateAPIError(404, "transaction-not-found", "The transaction was not found.");
            }

            var walletId = new WalletId(storeId, network.CryptoCode);
            var walletTransactionsInfoAsync =
                (await _walletRepository.GetWalletTransactionsInfo(walletId, new[] { transactionId })).Values
                .FirstOrDefault();

            return Ok(ToModel(walletTransactionsInfoAsync, tx, wallet));
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPatch(
            "~/api/v1/stores/{storeId}/payment-methods/{paymentMethodId}/wallet/transactions/{transactionId}")]
        public async Task<IActionResult> PatchOnChainWalletTransaction(
            string storeId,
            string paymentMethodId,
            string transactionId,
            [FromBody] PatchOnChainTransactionRequest request,
            bool force = false
        )
        {
            if (IsInvalidWalletRequest(paymentMethodId, out var network,
                    out var derivationScheme, out var actionResult))
                return actionResult;

            var wallet = _btcPayWalletProvider.GetWallet(network);
            var tx = await wallet.FetchTransaction(derivationScheme.AccountDerivation, uint256.Parse(transactionId));
            if (!force && tx is null)
            {
                return this.CreateAPIError(404, "transaction-not-found", "The transaction was not found.");
            }

            var walletId = new WalletId(storeId, network.CryptoCode);
            var txObjectId = new WalletObjectId(walletId, WalletObjectData.Types.Tx, transactionId);

            if (request.Comment != null)
            {
                await _walletRepository.SetWalletObjectComment(txObjectId, request.Comment);
            }

            if (request.Labels != null)
            {
                await _walletRepository.AddWalletObjectLabels(txObjectId, request.Labels.ToArray());
            }

            var walletTransactionsInfo =
                (await _walletRepository.GetWalletTransactionsInfo(walletId, new[] { transactionId }))
                .Values
                .FirstOrDefault();

            return Ok(ToModel(walletTransactionsInfo, tx, wallet));
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/{paymentMethodId}/wallet/utxos")]
        public async Task<IActionResult> GetOnChainWalletUTXOs(string storeId, string paymentMethodId)
        {
            if (IsInvalidWalletRequest(paymentMethodId, out var network,
                    out var derivationScheme, out var actionResult))
                return actionResult;

            var wallet = _btcPayWalletProvider.GetWallet(network);

            var walletId = new WalletId(storeId, network.CryptoCode);
            var utxos = await wallet.GetUnspentCoins(derivationScheme.AccountDerivation);
            var walletTransactionsInfoAsync = await _walletRepository.GetWalletTransactionsInfo(walletId,
                utxos.SelectMany(GetWalletObjectsQuery.Get).Distinct().ToArray());
            var pmi = PaymentMethodId.Parse(paymentMethodId);
            return Ok(utxos.Select(coin =>
                {
                    walletTransactionsInfoAsync.TryGetValue(coin.OutPoint.Hash.ToString(), out var info1);
                    walletTransactionsInfoAsync.TryGetValue(coin.Address.ToString(), out var info2);
                    walletTransactionsInfoAsync.TryGetValue(coin.OutPoint.ToString(), out var info3);
                    var info = _walletRepository.Merge(info1, info2, info3);

                    return new OnChainWalletUTXOData()
                    {
                        Outpoint = coin.OutPoint,
                        Amount = coin.Value.GetValue(network),
                        Comment = info?.Comment,
#pragma warning disable CS0612 // Type or member is obsolete
                        Labels = info?.LegacyLabels ?? new Dictionary<string, LabelData>(),
#pragma warning restore CS0612 // Type or member is obsolete
                        Link = _transactionLinkProviders.GetTransactionLink(network.CryptoCode, coin.OutPoint.ToString()),
                        Timestamp = coin.Timestamp,
                        KeyPath = coin.KeyPath,
                        Confirmations = coin.Confirmations,
                        Address = network.NBXplorerNetwork
                            .CreateAddress(derivationScheme.AccountDerivation, coin.KeyPath, coin.ScriptPubKey)
                            .ToString()
                    };
                }).ToList()
            );
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/payment-methods/{paymentMethodId}/wallet/transactions")]
        public async Task<IActionResult> CreateOnChainTransaction(string storeId, string paymentMethodId,
            [FromBody] CreateOnChainTransactionRequest request)
        {
            if (IsInvalidWalletRequest(paymentMethodId, out var network,
                    out var derivationScheme, out var actionResult))
                return actionResult;
            if (network.ReadonlyWallet)
            {
                return this.CreateAPIError(503, "not-available",
                    $"{network.CryptoCode} sending services are not currently available");
            }

            //This API is only meant for hot wallet usage for now. We can expand later when we allow PSBT manipulation.
            if (!(await CanUseHotWallet()).HotWallet)
            {
                return this.CreateAPIError(503, "not-available",
                    $"You need to allow non-admins to use hotwallets for their stores (in /server/policies)");
            }

            if (request.Destinations == null || !request.Destinations.Any())
            {
                ModelState.AddModelError(
                    nameof(request.Destinations),
                    "At least one destination must be specified"
                );

                return this.CreateValidationError(ModelState);
            }

            if (request.SelectedInputs != null && request.ExcludeUnconfirmed == true)
            {
                ModelState.AddModelError(
                    nameof(request.ExcludeUnconfirmed),
                    "Can't automatically exclude unconfirmed UTXOs while selection custom inputs"
                );

                return this.CreateValidationError(ModelState);
            }

            var explorerClient = _explorerClientProvider.GetExplorerClient(network);
            var wallet = _btcPayWalletProvider.GetWallet(network);

            var utxos = await wallet.GetUnspentCoins(derivationScheme.AccountDerivation, request.ExcludeUnconfirmed);
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

                BitcoinUrlBuilder? bip21 = null;
                var amount = destination.Amount;
                if (amount.GetValueOrDefault(0) <= 0)
                {
                    amount = null;
                }

                var address = string.Empty;
                try
                {
                    bip21 = new BitcoinUrlBuilder(destination.Destination, network.NBitcoinNetwork);
                    amount ??= bip21.Amount.GetValue(network);
                    if (bip21.Address is null)
                        request.AddModelError(transactionRequest => transactionRequest.Destinations[index],
                            "This BIP21 destination is missing a bitcoin address", this);
                    else
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
                    catch (Exception)
                    {
                        request.AddModelError(transactionRequest => transactionRequest.Destinations[index],
                            "Destination must be a BIP21 payment link or an address", this);
                    }
                }

                if (amount is null || amount <= 0)
                {
                    request.AddModelError(transactionRequest => transactionRequest.Destinations[index],
                        "Amount must be specified or destination must be a BIP21 payment link, and greater than 0",
                        this);
                }

                if (request.ProceedWithPayjoin &&
                    bip21?.UnknownParameters?.ContainsKey(PayjoinClient.BIP21EndpointKey) is true)
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

            var minRelayFee = _nbXplorerDashboard.Get(network.CryptoCode).Status.BitcoinStatus?.MinRelayTxFee ??
                              new FeeRate(1.0m);
            if (request.FeeRate != null && request.FeeRate < minRelayFee)
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
                return this.CreateAPIError(503, "not-available", "You need to update your version of NBXplorer");
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
            if (!derivationScheme.IsHotWallet || signingKeyStr is null)
            {
                return this.CreateAPIError(503, "not-available",
                    $"{network.CryptoCode} sending services are not currently available");
            }

            var signingKey = ExtKey.Parse(signingKeyStr, network.NBitcoinNetwork);

            var signingKeySettings = derivationScheme.GetSigningAccountKeySettings();
            signingKeySettings.RootFingerprint ??= signingKey.GetPublicKey().GetHDFingerPrint();
            RootedKeyPath rootedKeyPath = signingKeySettings.GetRootedKeyPath();
            psbt.PSBT.RebaseKeyPaths(signingKeySettings.AccountKey, rootedKeyPath);
            var accountKey = signingKey.Derive(rootedKeyPath.KeyPath);

            if (signingContext?.EnforceLowR is bool v)
                psbt.PSBT.Settings.SigningOptions.EnforceLowR = v;
            else if (psbt.Suggestions?.ShouldEnforceLowR is bool v2)
                psbt.PSBT.Settings.SigningOptions.EnforceLowR = v2;

            var changed = psbt.PSBT.PSBTChanged(() => psbt.PSBT.SignAll(derivationScheme.AccountDerivation, accountKey,
                rootedKeyPath));

            if (!changed)
            {
                return this.CreateAPIError("psbt-signing-error",
                    "Impossible to sign the transaction. Probable cause: Incorrect account key path in wallet settings, PSBT already signed.");
            }

            psbt.PSBT.Finalize();
            var transaction = psbt.PSBT.ExtractTransaction();
            var transactionHash = transaction.GetHash();
            BroadcastResult broadcastResult;
            if (!string.IsNullOrEmpty(signingContext?.PayJoinBIP21))
            {
                signingContext.OriginalPSBT = psbt.PSBT.ToBase64();
                try
                {
                    await _delayedTransactionBroadcaster.Schedule(DateTimeOffset.UtcNow + TimeSpan.FromMinutes(2.0),
                        transaction, network);
                    _payjoinClient.MinimumFeeRate = minRelayFee;
                    var payjoinPSBT = await _payjoinClient.RequestPayjoin(
                        new BitcoinUrlBuilder(signingContext.PayJoinBIP21, network.NBitcoinNetwork),
                        new PayjoinWallet(derivationScheme),
                        psbt.PSBT, CancellationToken.None);
                    psbt.PSBT.Settings.SigningOptions =
                        new SigningOptions() { EnforceLowR = !(signingContext?.EnforceLowR is false) };
                    payjoinPSBT = psbt.PSBT.SignAll(derivationScheme.AccountDerivation, accountKey, rootedKeyPath);
                    payjoinPSBT.Finalize();
                    var payjoinTransaction = payjoinPSBT.ExtractTransaction();
                    var hash = payjoinTransaction.GetHash();
                    await this._walletRepository.AddWalletTransactionAttachment(new WalletId(Store.Id, network.CryptoCode),
                        hash, Attachment.Payjoin());
                    broadcastResult = await explorerClient.BroadcastAsync(payjoinTransaction);
                    if (broadcastResult.Success)
                    {
                        return await GetOnChainWalletTransaction(storeId, paymentMethodId, hash.ToString());
                    }
                }
                catch (PayjoinException)
                {
                    //not a critical thing, payjoin is great if possible, fine if not
                }
            }

            if (!request.ProceedWithBroadcast)
            {
                return Ok(new JValue(transaction.ToHex()));
            }

            broadcastResult = await explorerClient.BroadcastAsync(transaction);
            if (broadcastResult.Success)
            {
                return await GetOnChainWalletTransaction(storeId, paymentMethodId, transactionHash.ToString());
            }
            else
            {
                return this.CreateAPIError("broadcast-error", broadcastResult.RPCMessage);
            }
        }

        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/{paymentMethodId}/wallet/objects")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> GetOnChainWalletObjects(string storeId, string paymentMethodId, string? type = null, [FromQuery(Name = "ids")] string[]? ids = null, bool? includeNeighbourData = null)
        {
            if (ids?.Length is 0 && !Request.Query.ContainsKey("ids"))
                ids = null;
            if (type is null && ids is not null)
                ModelState.AddModelError(nameof(ids), "If ids is specified, type should be specified");
            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);
            if (IsInvalidWalletRequest(paymentMethodId, out var network, out var actionResult))
                return actionResult;
            var walletId = new WalletId(storeId, network.CryptoCode);
            return Ok((await _walletRepository.GetWalletObjects(new(walletId, type, ids) { IncludeNeighbours = includeNeighbourData ?? true })).Select(kv => kv.Value).Select(ToModel).ToArray());
        }
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/{paymentMethodId}/wallet/objects/{objectType}/{objectId}")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> GetOnChainWalletObject(string storeId, string paymentMethodId,
            string objectType, string objectId,
            bool? includeNeighbourData = null)
        {
            if (IsInvalidWalletRequest(paymentMethodId, out var network, out var actionResult))
                return actionResult;
            var walletId = new WalletId(storeId, network.CryptoCode);
            var wo = await _walletRepository.GetWalletObject(new(walletId, objectType, objectId), includeNeighbourData ?? true);
            if (wo is null)
                return WalletObjectNotFound();
            return Ok(ToModel(wo));
        }

        [HttpDelete("~/api/v1/stores/{storeId}/payment-methods/{paymentMethodId}/wallet/objects/{objectType}/{objectId}")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> RemoveOnChainWalletObject(string storeId, string paymentMethodId,
            string objectType, string objectId)
        {
            if (IsInvalidWalletRequest(paymentMethodId, out var network, out var actionResult))
                return actionResult;
            var walletId = new WalletId(storeId, network.CryptoCode);
            if (await _walletRepository.RemoveWalletObjects(new WalletObjectId(walletId, objectType, objectId)))
                return Ok();
            else
                return WalletObjectNotFound();
        }

        private IActionResult WalletObjectNotFound()
        {
            return this.CreateAPIError(404, "wallet-object-not-found", "This wallet object's can't be found");
        }

        [HttpPost("~/api/v1/stores/{storeId}/payment-methods/{paymentMethodId}/wallet/objects")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> AddOrUpdateOnChainWalletObject(string storeId,
            string paymentMethodId,
            [FromBody] AddOnChainWalletObjectRequest request)
        {
            if (IsInvalidWalletRequest(paymentMethodId, out var network, out var actionResult))
                return actionResult;
            if (request?.Type is null)
                ModelState.AddModelError(nameof(request.Type), "Type is required");
            if (request?.Id is null)
                ModelState.AddModelError(nameof(request.Id), "Id is required");
            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);

            var walletId = new WalletId(storeId, network.CryptoCode);

            try
            {
                await _walletRepository.SetWalletObject(
                        new WalletObjectId(walletId, request!.Type, request.Id), request.Data);
                return await GetOnChainWalletObject(storeId, network.CryptoCode, request!.Type, request.Id);
            }
            catch (DbUpdateException)
            {
                return WalletObjectNotFound();
            }
        }

        [HttpPost("~/api/v1/stores/{storeId}/payment-methods/{paymentMethodId}/wallet/objects/{objectType}/{objectId}/links")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> AddOrUpdateOnChainWalletLinks(string storeId, string paymentMethodId,
            string objectType, string objectId,
            [FromBody] AddOnChainWalletObjectLinkRequest request)
        {
            if (IsInvalidWalletRequest(paymentMethodId, out var network, out var actionResult))
                return actionResult;
            if (request?.Type is null)
                ModelState.AddModelError(nameof(request.Type), "Type is required");
            if (request?.Id is null)
                ModelState.AddModelError(nameof(request.Id), "Id is required");
            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);

            var walletId = new WalletId(storeId, network.CryptoCode);
            try
            {
                await _walletRepository.SetWalletObjectLink(
                        new WalletObjectId(walletId, objectType, objectId),
                        new WalletObjectId(walletId, request!.Type, request.Id),
                        request?.Data);
                return Ok();
            }
            catch (DbUpdateException)
            {
                return WalletObjectNotFound();
            }
        }

        [HttpDelete("~/api/v1/stores/{storeId}/payment-methods/{paymentMethodId}/wallet/objects/{objectType}/{objectId}/links/{linkType}/{linkId}")]
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> RemoveOnChainWalletLink(string storeId, string paymentMethodId,
            string objectType, string objectId,
            string linkType, string linkId)
        {
            if (IsInvalidWalletRequest(paymentMethodId, out var network, out var actionResult))
                return actionResult;
            var walletId = new WalletId(storeId, network.CryptoCode);
            if (await _walletRepository.RemoveWalletObjectLink(
                    new WalletObjectId(walletId, objectType, objectId),
                    new WalletObjectId(walletId, linkType, linkId)))
                return Ok();
            else
                return WalletObjectNotFound();
        }

        private OnChainWalletObjectData ToModel(WalletObjectData data)
        {
            return new OnChainWalletObjectData()
            {
                Data = string.IsNullOrEmpty(data.Data) ? null : JObject.Parse(data.Data),
                Type = data.Type,
                Id = data.Id,
                Links = data.GetLinks().Select(linkData => ToModel(linkData)).ToArray()
            };
        }

        private OnChainWalletObjectData.OnChainWalletObjectLink ToModel((string type, string id, string linkdata, string objectdata) data)
        {
            return new OnChainWalletObjectData.OnChainWalletObjectLink()
            {
                LinkData = string.IsNullOrEmpty(data.linkdata) ? null : JObject.Parse(data.linkdata),
                ObjectData = string.IsNullOrEmpty(data.objectdata) ? null : JObject.Parse(data.objectdata),
                Type = data.type,
                Id = data.id,
            };
        }


        private async Task<(bool HotWallet, bool RPCImport)> CanUseHotWallet()
        {
            return await _authorizationService.CanUseHotWallet(PoliciesSettings, User);
        }

        private bool IsInvalidWalletRequest(string paymentMethodId, [MaybeNullWhen(true)] out BTCPayNetwork network,
            [MaybeNullWhen(true)] out DerivationSchemeSettings derivationScheme,
            [MaybeNullWhen(false)] out IActionResult actionResult)
        {
            derivationScheme = null;
            if (IsInvalidWalletRequest(paymentMethodId, out network, out actionResult))
                return true;

            derivationScheme = GetDerivationSchemeSettings(network.CryptoCode);
            if (derivationScheme?.AccountDerivation is null)
            {
                actionResult = this.CreateAPIError(503, "not-available",
                    $"{network.CryptoCode} doesn't have any derivation scheme set");
                return true;
            }

            actionResult = null;
            return false;
        }

        private bool IsInvalidWalletRequest(string paymentMethodId, [MaybeNullWhen(true)] out BTCPayNetwork network,
            [MaybeNullWhen(false)] out IActionResult actionResult)
        {
            if (!PaymentMethodId.TryParse(paymentMethodId, out var pmi)
                || !_handlers.TryGetValue(pmi, out var handler)
                || handler is not IHasNetwork { Network: { WalletSupported: true } })
            {
                throw new JsonHttpException(this.CreateAPIError(404, "unknown-paymentMethodId",
                    "This payment method doesn't exists or doesn't offer wallet services"));
            }
            network = ((IHasNetwork)handler).Network;

            if (!_btcPayWalletProvider.IsAvailable(network))
            {
                actionResult = this.CreateAPIError(503, "not-available",
                    $"{pmi} services are not currently available");
                return true;
            }
            actionResult = null;
            return false;
        }

        private DerivationSchemeSettings? GetDerivationSchemeSettings(string cryptoCode)
        {
            return Store.GetPaymentMethodConfig<DerivationSchemeSettings>(PaymentTypes.CHAIN.GetPaymentMethodId(cryptoCode), _handlers);
        }

        private OnChainWalletTransactionData ToModel(WalletTransactionInfo? walletTransactionsInfoAsync,
            TransactionHistoryLine tx,
            BTCPayWallet wallet)
        {
            return new OnChainWalletTransactionData()
            {
                TransactionHash = tx.TransactionId,
                Comment = walletTransactionsInfoAsync?.Comment ?? string.Empty,
#pragma warning disable CS0612 // Type or member is obsolete
                Labels = walletTransactionsInfoAsync?.LegacyLabels ?? new Dictionary<string, LabelData>(),
#pragma warning restore CS0612 // Type or member is obsolete
                Amount = tx.BalanceChange.GetValue(wallet.Network),
                BlockHash = tx.BlockHash,
                BlockHeight = tx.Height,
                Confirmations = tx.Confirmations,
                Timestamp = tx.SeenAt,
                Status = tx.Confirmations > 0 ? TransactionStatus.Confirmed : TransactionStatus.Unconfirmed
            };
        }
    }
}
