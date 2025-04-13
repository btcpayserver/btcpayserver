#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Mime;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.BIP78.Sender;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.ModelBinders;
using BTCPayServer.Models;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Payments.PayJoin;
using BTCPayServer.Payouts;
using BTCPayServer.Rating;
using BTCPayServer.Services;
using BTCPayServer.Services.Fees;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Labels;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using BTCPayServer.Services.Wallets.Export;
using Dapper;
using ExchangeSharp.BinanceGroup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NBitcoin;
using NBXplorer;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Newtonsoft.Json;
using static BTCPayServer.Models.WalletViewModels.WalletBumpFeeViewModel;
using static BTCPayServer.Services.Wallets.ReplacementInfo;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers
{
    [Route("wallets")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [AutoValidateAntiforgeryToken]
    //16mb psbts
    [RequestFormLimits(ValueLengthLimit = FormReader.DefaultValueLengthLimit * 4)]
    public partial class UIWalletsController : Controller
    {
        private StoreRepository Repository { get; }
        private WalletRepository WalletRepository { get; }
        private BTCPayNetworkProvider NetworkProvider { get; }
        private ExplorerClientProvider ExplorerClientProvider { get; }
        private IServiceProvider ServiceProvider { get; }
        private RateFetcher RateFetcher { get; }
        private IStringLocalizer StringLocalizer { get; }

        private readonly UserManager<ApplicationUser> _userManager;
        private readonly NBXplorerDashboard _dashboard;
        private readonly IAuthorizationService _authorizationService;
        private readonly IFeeProviderFactory _feeRateProvider;
        private readonly BTCPayWalletProvider _walletProvider;
        private readonly WalletReceiveService _walletReceiveService;
        private readonly SettingsRepository _settingsRepository;
        private readonly DelayedTransactionBroadcaster _broadcaster;
        private readonly PayjoinClient _payjoinClient;
        private readonly LabelService _labelService;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly DefaultRulesCollection _defaultRules;
        private readonly Dictionary<PaymentMethodId, ICheckoutModelExtension> _paymentModelExtensions;
        private readonly TransactionLinkProviders _transactionLinkProviders;
        private readonly PullPaymentHostedService _pullPaymentHostedService;
        private readonly WalletHistogramService _walletHistogramService;

        private readonly PendingTransactionService _pendingTransactionService;
        readonly CurrencyNameTable _currencyTable;
        private readonly DisplayFormatter _displayFormatter;

        public UIWalletsController(
            PendingTransactionService pendingTransactionService,
            StoreRepository repo,
            WalletRepository walletRepository,
            CurrencyNameTable currencyTable,
            BTCPayNetworkProvider networkProvider,
            UserManager<ApplicationUser> userManager,
            NBXplorerDashboard dashboard,
            WalletHistogramService walletHistogramService,
            RateFetcher rateProvider,
            IAuthorizationService authorizationService,
            ExplorerClientProvider explorerProvider,
            IFeeProviderFactory feeRateProvider,
            BTCPayWalletProvider walletProvider,
            WalletReceiveService walletReceiveService,
            SettingsRepository settingsRepository,
            DelayedTransactionBroadcaster broadcaster,
            PayjoinClient payjoinClient,
            IServiceProvider serviceProvider,
            PullPaymentHostedService pullPaymentHostedService,
            LabelService labelService,
            DefaultRulesCollection defaultRules,
            PaymentMethodHandlerDictionary handlers,
            Dictionary<PaymentMethodId, ICheckoutModelExtension> paymentModelExtensions,
            IStringLocalizer stringLocalizer,
            TransactionLinkProviders transactionLinkProviders,
            DisplayFormatter displayFormatter)
        {
            _pendingTransactionService = pendingTransactionService;
            _currencyTable = currencyTable;
            _labelService = labelService;
            _defaultRules = defaultRules;
            _handlers = handlers;
            _paymentModelExtensions = paymentModelExtensions;
            _transactionLinkProviders = transactionLinkProviders;
            Repository = repo;
            WalletRepository = walletRepository;
            RateFetcher = rateProvider;
            _authorizationService = authorizationService;
            NetworkProvider = networkProvider;
            _userManager = userManager;
            _dashboard = dashboard;
            ExplorerClientProvider = explorerProvider;
            _feeRateProvider = feeRateProvider;
            _walletProvider = walletProvider;
            _walletReceiveService = walletReceiveService;
            _settingsRepository = settingsRepository;
            _broadcaster = broadcaster;
            _payjoinClient = payjoinClient;
            _pullPaymentHostedService = pullPaymentHostedService;
            ServiceProvider = serviceProvider;
            _walletHistogramService = walletHistogramService;
            StringLocalizer = stringLocalizer;
            _displayFormatter = displayFormatter;
        }

        [HttpGet("{walletId}/pending/{transactionId}/cancel")]
        public IActionResult CancelPendingTransaction(
            [ModelBinder(typeof(WalletIdModelBinder))] WalletId walletId,
            string transactionId)
        {
            return View("Confirm", new ConfirmModel("Abort Pending Transaction",
                "Proceeding with this action will invalidate Pending Transaction and all accepted signatures.",
                "Confirm Abort"));
        }
        [HttpPost("{walletId}/pending/{transactionId}/cancel")]
        public async Task<IActionResult> CancelPendingTransactionConfirmed(
            [ModelBinder(typeof(WalletIdModelBinder))] WalletId walletId,
            string transactionId)
        {
            await _pendingTransactionService.CancelPendingTransaction(walletId.CryptoCode, walletId.StoreId, transactionId);
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Severity = StatusMessageModel.StatusSeverity.Success,
                Message = $"Aborted Pending Transaction {transactionId}"
            });
            return RedirectToAction(nameof(WalletTransactions), new { walletId = walletId.ToString() });
        }


        [HttpGet("{walletId}/pending/{transactionId}")]
        public async Task<IActionResult> ViewPendingTransaction(
            [ModelBinder(typeof(WalletIdModelBinder))] WalletId walletId,
            string transactionId)
        {
            var network = NetworkProvider.GetNetwork<BTCPayNetwork>(walletId.CryptoCode);
            var pendingTransaction =
                await _pendingTransactionService.GetPendingTransaction(walletId.CryptoCode, walletId.StoreId,
                    transactionId);
            if (pendingTransaction is null)
                return NotFound();
            var blob = pendingTransaction.GetBlob();
            if (blob?.PSBT is null)
                return NotFound();
            var currentPsbt = PSBT.Parse(blob.PSBT, network.NBitcoinNetwork);
            foreach (CollectedSignature collectedSignature in blob.CollectedSignatures)
            {
                var psbt = PSBT.Parse(collectedSignature.ReceivedPSBT, network.NBitcoinNetwork);
                currentPsbt = currentPsbt.Combine(psbt);
            }

            var derivationSchemeSettings = GetDerivationSchemeSettings(walletId);

            var vm = new WalletPSBTViewModel()
            {
                CryptoCode = network.CryptoCode,
                SigningContext = new SigningContextModel(currentPsbt)
                {
                    PendingTransactionId = transactionId,
                    PSBT = currentPsbt.ToBase64(),
                },
            };
            await FetchTransactionDetails(walletId, derivationSchemeSettings, vm, network);
            await vm.GetPSBT(network.NBitcoinNetwork, ModelState);
            return View("WalletPSBTDecoded", vm);
        }

        [Route("{walletId}/transactions/bump")]
        [Route("{walletId}/transactions/{transactionId}/bump")]
        public async Task<IActionResult> WalletBumpFee([ModelBinder(typeof(WalletIdModelBinder))]
            [FromQuery]
            WalletId walletId,
            WalletBumpFeeViewModel model,
          CancellationToken cancellationToken = default)
        {
            var paymentMethod = GetDerivationSchemeSettings(walletId);
            if (paymentMethod is null)
                return NotFound();
            
            var wallet = _walletProvider.GetWallet(walletId.CryptoCode);
			var bumpable = await wallet.GetBumpableTransactions(paymentMethod.AccountDerivation, cancellationToken);

            var bumpTarget = model.GetBumpTarget()
                                // Remove from the selected targets everything that isn't bumpable
                                .Filter(bumpable.Where(o => (o.Value.CPFP || o.Value.RBF) && o.Value.ReplacementInfo != null).Select(o => o.Key).ToHashSet());

            var explorer = this.ExplorerClientProvider.GetExplorerClient(walletId.CryptoCode);
            var txs = await GetUnconfWalletTxInfo(explorer, paymentMethod.AccountDerivation, bumpTarget.GetTransactionIds(), cancellationToken);

            // Remove from the selected targets everything for which we don't have the transaction info
            bumpTarget = bumpTarget.Filter(txs.Select(t => t.Key).ToHashSet());

			model.ReturnUrl ??= Url.WalletTransactions(walletId)!;

			decimal minBumpFee = 0.0m;
            if (bumpTarget.GetSingleTransactionId() is { } txId)
            {
                var inf = bumpable[txId];
                if (inf.RBF)
                    model.BumpFeeMethods.Add(new("RBF", "RBF"));
                if (inf.CPFP)
                    model.BumpFeeMethods.Add(new("CPFP", "CPFP"));

                // We calculate the effective fee rate using all the ancestors and descendant.
                model.CurrentFeeSatoshiPerByte = inf.ReplacementInfo!.GetEffectiveFeeRate().SatoshiPerByte;
                minBumpFee = inf.ReplacementInfo.CalculateNewMinFeeRate().SatoshiPerByte;
            }
            else if (bumpTarget.GetTransactionIds().Any())
            {
                model.BumpFeeMethods.Add(new("CPFP", "CPFP"));
                // If we bump multiple transactions, we calculate the effective fee rate without
                // taking into account descendants. This isn't super correct... but good enough for our purposes.
                // This is because we would have the risk of double counting the fees otherwise.
                var currentFeeRate = GetTransactionsFeeInfo(bumpTarget, txs, null).CurrentFeeRate.SatoshiPerByte;
                model.CurrentFeeSatoshiPerByte = currentFeeRate;
                minBumpFee = currentFeeRate + 1.0m;
            }
            else
            {
                this.TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Message =
                    bumpable switch
                    {
                        { Support: BumpableSupport.NotCompatible } => StringLocalizer["This version of NBXplorer is not compatible. Please update to 2.5.22 or above"],
                        { Support: BumpableSupport.NotConfigured } => StringLocalizer["Please set NBXPlorer's PostgreSQL connection string to make this feature available."],
                        { Support: BumpableSupport.NotSynched } => StringLocalizer["Please wait for your node to be synched"],
                        _ => StringLocalizer["None of the selected transaction can be fee bumped"]
                    }
                });
                return LocalRedirect(model.ReturnUrl);
            }

            model.IsMultiSigOnServer = paymentMethod.IsMultiSigOnServer;
            var feeProvider = _feeRateProvider.CreateFeeProvider(wallet.Network);
            var recommendedFees = await GetRecommendedFees(wallet.Network, _feeRateProvider);

            foreach (var option in recommendedFees)
            {
                if (option is null)
                    continue;
                if (minBumpFee is decimal v && option.FeeRate < v)
                    option.FeeRate = v;
            }
            
            model.RecommendedSatoshiPerByte =
                recommendedFees.Where(option => option != null).ToList();
            model.FeeSatoshiPerByte ??= recommendedFees.Skip(1).FirstOrDefault()?.FeeRate;

            if (HttpContext.Request.Method != HttpMethods.Post)
            {
                model.Command = null;
            }
            if (!ModelState.IsValid || model.Command is null || model.FeeSatoshiPerByte is null)
                return View(nameof(WalletBumpFee), model);

            var targetFeeRate = new FeeRate(model.FeeSatoshiPerByte.Value);
            model.BumpMethod ??= model.BumpFeeMethods switch
            {
                { Count: 1 } => model.BumpFeeMethods[0].Value,
                _ => "RBF"
            };
            PSBT? psbt = null;
            SigningContextModel? signingContext = null;
            var feeBumpUrl = Url.Action(nameof(WalletBumpFee), new { walletId, transactionId = bumpTarget.GetSingleTransactionId(), model.FeeSatoshiPerByte, model.BumpMethod, model.TransactionHashes, model.Outpoints })!;
            if (model.BumpMethod == "CPFP")
            {
                var utxos = await explorer.GetUTXOsAsync(paymentMethod.AccountDerivation);

                List<OutPoint> bumpableUTXOs = bumpTarget.GetMatchedOutpoints(utxos.GetUnspentUTXOs().Where(u => u.Confirmations == 0).Select(u => u.Outpoint));
                if (bumpableUTXOs.Count == 0)
                {
                    TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["There isn't any UTXO available to bump fee with CPFP"].Value;
                    return LocalRedirect(model.ReturnUrl);
                }

                var createPSBT = new CreatePSBTRequest()
				{
					RBF = true,
					AlwaysIncludeNonWitnessUTXO = paymentMethod.DefaultIncludeNonWitnessUtxo,
					IncludeOnlyOutpoints = bumpableUTXOs,
					SpendAllMatchingOutpoints = true,
					FeePreference = new FeePreference()
					{
						ExplicitFee = GetTransactionsFeeInfo(bumpTarget, txs, targetFeeRate).MissingFee,
						ExplicitFeeRate = targetFeeRate
					}
				};

                try
                {
                    var psbtResponse = await explorer.CreatePSBTAsync(paymentMethod.AccountDerivation, createPSBT, cancellationToken);

                    signingContext = new SigningContextModel
                    {
                        EnforceLowR = psbtResponse.Suggestions?.ShouldEnforceLowR,
                        ChangeAddress = psbtResponse.ChangeAddress?.ToString(),
                        PSBT = psbtResponse.PSBT.ToHex()
                    };
                    psbt = psbtResponse.PSBT;
                }
                catch (Exception ex)
                {
                    TempData[WellKnownTempData.ErrorMessage] = ex.Message;

                    return LocalRedirect(model.ReturnUrl);
                }
            }
            else if (model.BumpMethod == "RBF")
            {
                // RBF is only supported for a single tx
                var tx = txs[bumpTarget.GetSingleTransactionId()!];
                var changeOutput = tx.Outputs.FirstOrDefault(o => o.Feature == DerivationFeature.Change);
                if (tx.Inputs.Count != tx.Transaction?.Inputs.Count ||
                    changeOutput is null)
                {
                    this.ModelState.AddModelError(nameof(model.BumpMethod), StringLocalizer["This transaction can't be RBF'd"]);
                    return View(nameof(WalletBumpFee), model);
                }
                IActionResult ChangeTooSmall(WalletBumpFeeViewModel model, Money? missing)
                {
                    if (missing is not null)
                        ModelState.AddModelError(nameof(model.FeeSatoshiPerByte), StringLocalizer["The change output is too small to pay for additional fee. (Missing {0} BTC)", missing.ToDecimal(MoneyUnit.BTC)]);
                    else
                        ModelState.AddModelError(nameof(model.FeeSatoshiPerByte), StringLocalizer["The change output is too small to pay for additional fee."]);
                    return View(nameof(WalletBumpFee), model);
                }

                var bumpResult = bumpable[tx.TransactionId].ReplacementInfo!.CalculateBumpResult(targetFeeRate);
                var createPSBT = new CreatePSBTRequest()
                {
                    RBF = true,
                    AlwaysIncludeNonWitnessUTXO = paymentMethod.DefaultIncludeNonWitnessUtxo,
                    IncludeOnlyOutpoints = tx.Transaction.Inputs.Select(i => i.PrevOut).ToList(),
                    SpendAllMatchingOutpoints = true,
                    DisableFingerprintRandomization = true,
                    FeePreference = new FeePreference()
                    {
                        ExplicitFee = bumpResult.NewTxFee
                    },
                    ExplicitChangeAddress = changeOutput.Address,
                    Destinations = tx.Transaction.Outputs.AsIndexedOutputs()
                                        .Select(o => new CreatePSBTDestination()
                                        {
                                            Amount = o.N == changeOutput.Index ? (Money)o.TxOut.Value - bumpResult.BumpTxFee : (Money)o.TxOut.Value,
                                            Destination = o.TxOut.ScriptPubKey,
                                        }).ToList()
                };
                var missingFundsOutput = createPSBT.Destinations.FirstOrDefault(d => d.Amount < Money.Zero);
                if (missingFundsOutput is not null)
                    return ChangeTooSmall(model, -missingFundsOutput.Amount);

                try
                {
                    var psbtResponse = await explorer.CreatePSBTAsync(paymentMethod.AccountDerivation, createPSBT, cancellationToken);

                    signingContext = new SigningContextModel
                    {
                        EnforceLowR = psbtResponse.Suggestions?.ShouldEnforceLowR,
                        ChangeAddress = psbtResponse.ChangeAddress?.ToString(),
                        PSBT = psbtResponse.PSBT.ToHex(),
                        BalanceChangeFromReplacement = (-(Money)tx.BalanceChange).Satoshi
                    };
                    psbt = psbtResponse.PSBT;
                }
                catch (NBXplorerException ex) when (ex.Error.Code == "output-too-small")
                {
                    return ChangeTooSmall(model, null);
                }
                catch (NBXplorerException ex)
                {
                    ModelState.AddModelError(nameof(model.TransactionId), StringLocalizer["Unable to create the replacement transaction ({0})", ex.Error.Message]);
                    return View(nameof(WalletBumpFee), model);
                }
            }

            if (psbt is not null && signingContext is not null)
            {
                if (psbt.TryGetFinalizedHash(out var hash))
                    await this.WalletRepository.EnsureWalletObject(new WalletObjectId(walletId, WalletObjectData.Types.Tx, hash.ToString()),
                        new Newtonsoft.Json.Linq.JObject()
                        {
                            ["bumpFeeMethod"] = model.BumpMethod
                        });
                switch (model.Command)
                {
                    case "createpending":
                        var pt = await _pendingTransactionService.CreatePendingTransaction(walletId.StoreId, walletId.CryptoCode, psbt);
                        return RedirectToWalletList(walletId);
                    default:
                        // case "sign":
                        return await WalletSign(walletId, new WalletPSBTViewModel()
                        {
                            SigningContext = signingContext,
                            BackUrl = feeBumpUrl,
                            ReturnUrl = model.ReturnUrl
                        });
                }
            }

            // Ask choice to user
            return View(nameof(WalletBumpFee), model);
        }

        private async Task<Dictionary<uint256, TransactionInformation>> GetUnconfWalletTxInfo(ExplorerClient client, DerivationStrategyBase derivationStrategyBase, HashSet<uint256> txs, CancellationToken cancellationToken)
        {
			var txWalletInfo = new Dictionary<uint256, TransactionInformation>();
            var getTransactionAsync = txs.Select(t => client.GetTransactionAsync(derivationStrategyBase, t, cancellationToken)).ToArray();
            await Task.WhenAll(getTransactionAsync);
            foreach (var t in getTransactionAsync)
            {
                var r = await t;
                if (r is not
					{
						Confirmations: 0,
						Transaction: not null
					})
                    continue;
				txWalletInfo.Add(r.TransactionId, r);
            }
            return txWalletInfo;
        }

        private (Money MissingFee, FeeRate CurrentFeeRate) GetTransactionsFeeInfo(BumpTarget target, Dictionary<uint256, TransactionInformation> txs, FeeRate? newFeeRate)
        {
            Money missingFee = Money.Zero;
            int totalSize = 0;
            Money totalFee = Money.Zero;
            // In theory, we should calculate using the effective fee rate of all bumped transactions.
            // In practice, it's a bit complicated to get... meh, that's good enough.
            foreach (var bumpedTx in target.GetTransactionIds().Select(o => txs[o]))
            {
                var size = bumpedTx.Metadata?.VirtualSize ?? bumpedTx.Transaction?.GetVirtualSize() ?? 200;
                var feePaid = bumpedTx.Metadata?.Fees;
                if (feePaid is null)
                    // This shouldn't normally happen, as NBX indexes the fee if the transaction is in the mempool
                    continue;
                if (newFeeRate is not null)
                {
                    var expectedFeePaid = newFeeRate.GetFee(size);
                    missingFee += Money.Max(Money.Zero, expectedFeePaid - feePaid);
                }
                totalSize += size;
                totalFee += feePaid;
            }
            return (missingFee, new FeeRate(totalFee, totalSize));
        }

        private IActionResult RedirectToWalletList(WalletId walletId)
        {
            return RedirectToAction(nameof(WalletTransactions), new { walletId = walletId.ToString() });
        }

        [HttpPost]
        [Route("{walletId}")]
        public async Task<IActionResult> ModifyTransaction(
            // We need addlabel and addlabelclick. addlabel is the + button if the label does not exists,
            // addlabelclick is if the user click on existing label. For some reason, reusing the same name attribute for both
            // does not work
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, string transactionId,
            string? addlabel = null,
            string? addlabelclick = null,
            string? addcomment = null,
            string? removelabel = null)
        {
            addlabel = addlabel ?? addlabelclick;
            // Hack necessary when the user enter a empty comment and submit.
            // For some reason asp.net consider addcomment null instead of empty string...
            try
            {
                if (addcomment == null && Request?.Form?.TryGetValue(nameof(addcomment), out _) is true)
                {
                    addcomment = string.Empty;
                }
            }
            catch { }
            /////////

            var paymentMethod = GetDerivationSchemeSettings(walletId);
            if (paymentMethod == null)
                return NotFound();

            var txObjId = new WalletObjectId(walletId, WalletObjectData.Types.Tx, transactionId);
            if (addlabel != null)
            {
                await WalletRepository.AddWalletObjectLabels(txObjId, addlabel);
            }
            else if (removelabel != null)
            {
                await WalletRepository.RemoveWalletObjectLabels(txObjId, removelabel);
            }
            else if (addcomment != null)
            {
                await WalletRepository.SetWalletObjectComment(txObjId, addcomment);
            }
            return RedirectToAction(nameof(WalletTransactions), new { walletId = walletId.ToString() });
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> ListWallets()
        {
            if (GetUserId() == null)
            {
                return Challenge(AuthenticationSchemes.Cookie);
            }
            var wallets = new ListWalletsViewModel();
            var stores = await Repository.GetStoresByUserId(GetUserId());

            var onChainWallets = stores
                .SelectMany(s => s.GetPaymentMethodConfigs<DerivationSchemeSettings>(_handlers)
                    .Select(d => (
                        Wallet: _walletProvider.GetWallet(((IHasNetwork)_handlers[d.Key]).Network),
                        DerivationStrategy: d.Value.AccountDerivation,
                        Network: ((IHasNetwork)_handlers[d.Key]).Network))
                    .Where(_ => _.Wallet != null && _.Network.WalletSupported)
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


                walletVm.CryptoCode = wallet.Network.CryptoCode;
                walletVm.StoreId = wallet.Store.Id;
                walletVm.Id = new WalletId(wallet.Store.Id, wallet.Network.CryptoCode);
                walletVm.StoreName = wallet.Store.StoreName;

                var money = await GetBalanceAsMoney(wallet.Wallet, wallet.DerivationStrategy);
                wallets.BalanceForCryptoCode[wallet.Network] = wallets.BalanceForCryptoCode.ContainsKey(wallet.Network)
                    ? wallets.BalanceForCryptoCode[wallet.Network].Add(money)
                    : money;
            }

            return View(wallets);
        }

        [HttpGet("{walletId}")]
        [HttpGet("{walletId}/transactions")]
        public async Task<IActionResult> WalletTransactions(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId,
            string? labelFilter = null,
            int skip = 0,
            int count = 50,
            bool loadTransactions = false,
            CancellationToken cancellationToken = default
        )
        {
            var paymentMethod = GetDerivationSchemeSettings(walletId);
            if (paymentMethod == null)
                return NotFound();
            var network = _handlers.GetBitcoinHandler(walletId.CryptoCode).Network;
            var wallet = _walletProvider.GetWallet(network);

            // We can't filter at the database level if we need to apply label filter
            var preFiltering = string.IsNullOrEmpty(labelFilter);
            var model = new ListTransactionsViewModel { Skip = skip, Count = count };

            model.PendingTransactions = await _pendingTransactionService.GetPendingTransactions(walletId.CryptoCode, walletId.StoreId);

            model.Labels.AddRange(
                (await WalletRepository.GetWalletLabels(walletId))
                .Select(c => (c.Label, c.Color, ColorPalette.Default.TextColor(c.Color))));

            IList<TransactionHistoryLine>? transactions = null;
            Dictionary<string, WalletTransactionInfo>? walletTransactionsInfo = null;
            if (loadTransactions)
            {
                transactions = await wallet.FetchTransactionHistory(paymentMethod.AccountDerivation, preFiltering ? skip : null, preFiltering ? count : null, cancellationToken: cancellationToken);
                walletTransactionsInfo = await WalletRepository.GetWalletTransactionsInfo(walletId, transactions.Select(t => t.TransactionId.ToString()).ToArray());
            }
            if (labelFilter != null)
            {
                model.PaginationQuery = new Dictionary<string, object> { { "labelFilter", labelFilter } };
            }
            if (transactions == null || walletTransactionsInfo is null)
            {
                model.Transactions = new List<ListTransactionsViewModel.TransactionViewModel>();
            }
            else
            {
                var bumpable = transactions.Any(tx => tx.Confirmations == 0) ? await wallet.GetBumpableTransactions(paymentMethod.AccountDerivation, cancellationToken) : new();
                var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(walletId.CryptoCode);
                foreach (var tx in transactions)
                {
                    var vm = new ListTransactionsViewModel.TransactionViewModel();
                    vm.Id = tx.TransactionId.ToString();
                    vm.Link = _transactionLinkProviders.GetTransactionLink(pmi, vm.Id);
                    vm.Timestamp = tx.SeenAt;
                    vm.Positive = tx.BalanceChange.GetValue(wallet.Network) >= 0;
                    vm.Balance = tx.BalanceChange.ShowMoney(wallet.Network);
                    vm.IsConfirmed = tx.Confirmations != 0;
                    // If support isn't possible, we want the user to be able to click so he can see why it doesn't work
                    vm.CanBumpFee =
                        tx.Confirmations == 0 &&
                        (bumpable.Support is not BumpableSupport.Ok || (bumpable.TryGetValue(tx.TransactionId, out var i) ? i.RBF || i.CPFP : false));
                    if (walletTransactionsInfo.TryGetValue(tx.TransactionId.ToString(), out var transactionInfo))
                    {
                        var labels = _labelService.CreateTransactionTagModels(transactionInfo, Request);
                        vm.Tags.AddRange(labels);
                        vm.Comment = transactionInfo.Comment;
                    }

                    if (labelFilter == null ||
                        vm.Tags.Any(l => l.Text.Equals(labelFilter, StringComparison.OrdinalIgnoreCase)))
                        model.Transactions.Add(vm);
                }

                model.Total = preFiltering ? null : model.Transactions.Count;
                // if we couldn't filter at the db level, we need to apply skip and count
                if (!preFiltering)
                {
                    model.Transactions = model.Transactions.Skip(skip).Take(count).ToList();
                }
            }

            model.CryptoCode = walletId.CryptoCode;

            //If ajax call then load the partial view
            return Request.Headers["X-Requested-With"] == "XMLHttpRequest"
                ? PartialView("_WalletTransactionsList", model)
                : View(model);
        }

        [HttpGet("{walletId}/histogram/{type}")]
        public async Task<IActionResult> WalletHistogram(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, HistogramType type)
        {
            var store = GetCurrentStore();
            var data = await _walletHistogramService.GetHistogram(store, walletId, type);
            if (data == null)
                return NotFound();

            return Json(data);
        }

        [HttpGet("{walletId}/receive")]
        public async Task<IActionResult> WalletReceive([ModelBinder(typeof(WalletIdModelBinder))] WalletId walletId,
            [FromQuery] string? returnUrl = null)
        {
            if (walletId?.StoreId == null)
                return NotFound();
            var paymentMethod = GetDerivationSchemeSettings(walletId);
            if (paymentMethod == null)
                return NotFound();
            var network = NetworkProvider.GetNetwork<BTCPayNetwork>(walletId.CryptoCode);
            if (network == null)
                return NotFound();
            var store = GetCurrentStore();
            var address = (await _walletReceiveService.GetOrGenerate(walletId)).Address;
            var allowedPayjoin = paymentMethod.IsHotWallet && store.GetStoreBlob().PayJoinEnabled;
            var bip21 = network.GenerateBIP21(address?.ToString(), null);
            if (allowedPayjoin)
            {
                var endpoint = Url.ActionAbsolute(Request, nameof(PayJoinEndpointController.Submit), "PayJoinEndpoint",
                        new { cryptoCode = walletId.CryptoCode }).ToString();
                bip21.QueryParams.Add(PayjoinClient.BIP21EndpointKey, endpoint);
            }

            string[]? labels = null;
            if (address is not null)
            {
                var info = await WalletRepository.GetWalletObject(new WalletObjectId(walletId, WalletObjectData.Types.Address,
                    address.ToString()));
                labels = info?.GetNeighbours().Where(data => data.Type == WalletObjectData.Types.Label)
                    .Select(data => data.Id).ToArray();
            }
            return View(new WalletReceiveViewModel
            {
                CryptoCode = walletId.CryptoCode,
                Address = address?.ToString(),
                CryptoImage = GetImage(network),
                PaymentLink = bip21.ToString(),
                ReturnUrl = returnUrl,
                SelectedLabels = labels ?? Array.Empty<string>()
            });
        }

        [HttpPost("{walletId}/receive")]
        public async Task<IActionResult> WalletReceive([ModelBinder(typeof(WalletIdModelBinder))] WalletId walletId,
            WalletReceiveViewModel vm, string command)
        {
            if (walletId?.StoreId == null)
                return NotFound();
            var paymentMethod = GetDerivationSchemeSettings(walletId);
            if (paymentMethod == null)
                return NotFound();
            var network = NetworkProvider.GetNetwork<BTCPayNetwork>(walletId.CryptoCode);
            if (network == null)
                return NotFound();
            switch (command)
            {
                case "generate-new-address":
                    await _walletReceiveService.GetOrGenerate(walletId, true);
                    break;
                case "fill-wallet":
                    var cheater = ServiceProvider.GetService<Cheater>();
                    if (cheater != null)
                        await SendFreeMoney(cheater, walletId, paymentMethod);
                    break;
            }
            return RedirectToAction(nameof(WalletReceive), new { walletId, returnUrl = vm.ReturnUrl });
        }

        private async Task SendFreeMoney(Cheater cheater, WalletId walletId, DerivationSchemeSettings paymentMethod)
        {
            var c = this.ExplorerClientProvider.GetExplorerClient(walletId.CryptoCode);
            var cashCow = cheater.GetCashCow(walletId.CryptoCode);
            if (walletId.CryptoCode == "LBTC")
            {
                await cashCow.SendCommandAsync("rescanblockchain");
            }
            var addresses = Enumerable.Range(0, 10).Select(_ => c.GetUnusedAsync(paymentMethod.AccountDerivation, DerivationFeature.Deposit, reserve: true)).ToArray();
            
            await Task.WhenAll(addresses);
            await cashCow.GenerateAsync(addresses.Length / 8);
            var b = cashCow.PrepareBatch();
            Random r = new Random();
            List<Task<uint256>> sending = new List<Task<uint256>>();
            foreach (var a in addresses)
            {
                sending.Add(b.SendToAddressAsync((await a).Address, Money.Coins(0.1m) + Money.Satoshis(r.Next(0, 90_000_000))));
            }
            await b.SendBatchAsync();
            await cashCow.GenerateAsync(1);

            var factory = ServiceProvider.GetRequiredService<NBXplorerConnectionFactory>();

            // Wait it sync...
            await Task.Delay(1000);
            await ExplorerClientProvider.GetExplorerClient(walletId.CryptoCode).WaitServerStartedAsync();
            await Task.Delay(1000);
            await using var conn = await factory.OpenConnection();

            var txIds = sending.Select(s => s.Result.ToString()).ToArray();
            await conn.ExecuteAsync(
                "UPDATE txs t SET seen_at=(NOW() - (random() * (interval '90 days'))) " +
                "FROM unnest(@txIds) AS r (tx_id) WHERE r.tx_id=t.tx_id;", new { txIds });
            await Task.Delay(1000);
            await conn.ExecuteAsync("REFRESH MATERIALIZED VIEW wallets_history;");
        }

        private async Task<bool> CanUseHotWallet()
        {
            var policies = await _settingsRepository.GetSettingAsync<PoliciesSettings>();
            return (await _authorizationService.CanUseHotWallet(policies, User)).CanCreateHotWallet;
        }

        [HttpGet("{walletId}/send")]
        public async Task<IActionResult> WalletSend(
            [ModelBinder(typeof(WalletIdModelBinder))] WalletId walletId,
            string? defaultDestination = null, string? defaultAmount = null, string[]? bip21 = null,
            [FromQuery] string? returnUrl = null)
        {
            if (walletId?.StoreId == null)
                return NotFound();
            var store = await Repository.FindStore(walletId.StoreId);
            var paymentMethod = GetDerivationSchemeSettings(walletId);
            if (paymentMethod == null || store is null)
                return NotFound();
            var network = this.NetworkProvider.GetNetwork<BTCPayNetwork>(walletId.CryptoCode);
            if (network == null || network.ReadonlyWallet)
                return NotFound();
            
            double.TryParse(defaultAmount, out var amount);

            var model = new WalletSendModel
            {
                CryptoCode = walletId.CryptoCode,
                ReturnUrl = returnUrl ?? HttpContext.Request.GetTypedHeaders().Referer?.AbsolutePath,
                IsMultiSigOnServer = paymentMethod.IsMultiSigOnServer,
                AlwaysIncludeNonWitnessUTXO = paymentMethod.DefaultIncludeNonWitnessUtxo
            };
            if (bip21?.Any() is true)
            {
                var messagePresent = TempData.HasStatusMessage();
                foreach (var link in bip21)
                {
                    if (!string.IsNullOrEmpty(link))
                    {
                        await LoadFromBIP21(walletId, model, link, network, messagePresent);
                    }
                }
            }

            if (!(model.Outputs?.Any() is true))
            {
                model.Outputs = new List<WalletSendModel.TransactionOutput>()
                {
                    new WalletSendModel.TransactionOutput()
                    {
                        Amount = Convert.ToDecimal(amount), DestinationAddress = defaultDestination
                    }
                };
            }
            var recommendedFeesAsync = GetRecommendedFees(network, _feeRateProvider);
            var balance = _walletProvider.GetWallet(network).GetBalance(paymentMethod.AccountDerivation);
            model.NBXSeedAvailable = await GetSeed(walletId, network) != null;
            var Balance = await balance;
            model.CurrentBalance = (Balance.Available ?? Balance.Total).GetValue(network);
            if (Balance.Immature is null)
                model.ImmatureBalance = 0;
            else
                model.ImmatureBalance = Balance.Immature.GetValue(network);

            var recommendedFees = await recommendedFeesAsync;
            model.RecommendedSatoshiPerByte =
                recommendedFees.Where(option => option != null).ToList();

            model.FeeSatoshiPerByte = recommendedFees.Skip(1).FirstOrDefault()?.FeeRate;
            model.CryptoDivisibility = network.Divisibility;
            
            try
            {
                var r = await FetchRate(walletId);
                
                model.Rate = r.Rate;
                model.FiatDivisibility = _currencyTable.GetNumberFormatInfo(r.Fiat, true)
                    .CurrencyDecimalDigits;
                model.Fiat = r.Fiat;
            }
            catch (Exception ex) { model.RateError = ex.Message; }
                    
            return View(model);
        }

        public record FiatRate(decimal Rate, string Fiat);
        private async Task<FiatRate> FetchRate(WalletId walletId)
        {            
            var store = await Repository.FindStore(walletId.StoreId);
            if (store is null)
                throw new Exception("Store not found");
            var storeData = store.GetStoreBlob();
            var rateRules = storeData.GetRateRules(_defaultRules);
            rateRules.Spread = 0.0m;
            var currencyPair = new CurrencyPair(walletId.CryptoCode, storeData.DefaultCurrency);
            
            using CancellationTokenSource cts = new();
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var result = await RateFetcher.FetchRate(currencyPair, rateRules, new StoreIdRateContext(store.Id), cts.Token)
                .WithCancellation(cts.Token);

            if (result.BidAsk == null)
            {
                throw new Exception(
                    $"{result.EvaluatedRule} ({string.Join(", ", result.Errors.OfType<object>().ToArray())})");
            }

            return new (result.BidAsk.Center, currencyPair.Right);
        }

        private static async Task<WalletSendModel.FeeRateOption?[]> GetRecommendedFees(BTCPayNetwork network, IFeeProviderFactory feeProviderFactory)
        {
            var feeProvider = feeProviderFactory.CreateFeeProvider(network);
            List<WalletSendModel.FeeRateOption?> options = new();
            foreach (var time in new[] {
                        TimeSpan.FromMinutes(10.0), TimeSpan.FromMinutes(60.0), TimeSpan.FromHours(6.0),
                        TimeSpan.FromHours(24.0),
                    })
            {
                try
                {
                    var result = await feeProvider.GetFeeRateAsync(
                        (int)network.NBitcoinNetwork.Consensus.GetExpectedBlocksFor(time));
                    options.Add(new WalletSendModel.FeeRateOption()
                    {
                        Target = time,
                        FeeRate = result.SatoshiPerByte
                    });
                }
                catch (Exception)
                {
                    options.Add(null);
                }
            }
            return options.ToArray();
        }

        private async Task<string?> GetSeed(WalletId walletId, BTCPayNetwork network)
        {
            return await CanUseHotWallet() &&
                   GetDerivationSchemeSettings(walletId) is DerivationSchemeSettings s &&
                   s.IsHotWallet &&
                   ExplorerClientProvider.GetExplorerClient(network) is ExplorerClient client &&
                   await client.GetMetadataAsync<string>(s.AccountDerivation, WellknownMetadataKeys.MasterHDKey) is
                       string seed &&
                   !string.IsNullOrEmpty(seed)
                ? seed
                : null;
        }

        [HttpPost("{walletId}/send")]
        public async Task<IActionResult> WalletSend(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, WalletSendModel vm, string command = "", CancellationToken cancellation = default,
            string? bip21 = "")
        {
            if (walletId?.StoreId == null)
                return NotFound();
            var store = await Repository.FindStore(walletId.StoreId);
            if (store == null)
                return NotFound();
            var network = NetworkProvider.GetNetwork<BTCPayNetwork>(walletId.CryptoCode);
            if (network == null || network.ReadonlyWallet)
                return NotFound();

            vm.NBXSeedAvailable = await GetSeed(walletId, network) != null;
            if (!string.IsNullOrEmpty(bip21))
            {
                vm.Outputs?.Clear();
                await LoadFromBIP21(walletId, vm, bip21, network, TempData.HasStatusMessage());
            }

            decimal transactionAmountSum = 0;
            if (command == "toggle-input-selection")
            {
                vm.InputSelection = !vm.InputSelection;
            }
            if (vm.InputSelection)
            {
                var schemeSettings = GetDerivationSchemeSettings(walletId);
                if (schemeSettings is null)
                    return NotFound();

                var utxos = await _walletProvider.GetWallet(network)
                    .GetUnspentCoins(schemeSettings.AccountDerivation, false, cancellation);
                var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(vm.CryptoCode);
                var walletTransactionsInfoAsync = await this.WalletRepository.GetWalletTransactionsInfo(walletId,
                    utxos.SelectMany(GetWalletObjectsQuery.Get).Distinct().ToArray());
                vm.InputsAvailable = utxos.Select(coin =>
                {
                    walletTransactionsInfoAsync.TryGetValue(coin.OutPoint.Hash.ToString(), out var info1);
                    walletTransactionsInfoAsync.TryGetValue(coin.Address.ToString(), out var info2);
                    walletTransactionsInfoAsync.TryGetValue(coin.OutPoint.ToString(), out var info3);
                    var info = WalletRepository.Merge(info1, info2, info3);
                    return new WalletSendModel.InputSelectionOption()
                    {
                        Outpoint = coin.OutPoint.ToString(),
                        Amount = coin.Value.GetValue(network),
                        Comment = info?.Comment,
                        Labels = _labelService.CreateTransactionTagModels(info, Request),
                        Link = _transactionLinkProviders.GetTransactionLink(pmi, coin.OutPoint.ToString()),
                        Confirmations = coin.Confirmations
                    };
                }).ToArray();
            }

            if (command == "toggle-input-selection")
            {
                ModelState.Clear();
                return View(vm);
            }
            vm.Outputs ??= new();
            if (!string.IsNullOrEmpty(bip21))
            {
                if (!vm.Outputs.Any())
                {
                    vm.Outputs.Add(new WalletSendModel.TransactionOutput());
                }
                return View(vm);
            }
            if (command == "add-output")
            {
                ModelState.Clear();
                vm.Outputs.Add(new WalletSendModel.TransactionOutput());
                return View(vm);
            }
            if (command.StartsWith("remove-output", StringComparison.InvariantCultureIgnoreCase))
            {
                ModelState.Clear();
                var index = int.Parse(
                    command.Substring(command.IndexOf(":", StringComparison.InvariantCultureIgnoreCase) + 1),
                    CultureInfo.InvariantCulture);
                vm.Outputs.RemoveAt(index);
                return View(vm);
            }

            if (!vm.Outputs.Any())
            {
                ModelState.AddModelError(string.Empty,
                    "Please add at least one transaction output");
                return View(vm);
            }

            var bypassBalanceChecks = command == "schedule";

            var subtractFeesOutputsCount = new List<int>();
            var substractFees = vm.Outputs.Any(o => o.SubtractFeesFromOutput);
            for (var i = 0; i < vm.Outputs.Count; i++)
            {
                var transactionOutput = vm.Outputs[i];
                if (transactionOutput.SubtractFeesFromOutput)
                {
                    subtractFeesOutputsCount.Add(i);
                }
                transactionOutput.DestinationAddress = transactionOutput.DestinationAddress?.Trim() ?? string.Empty;

                var inputName =
                    string.Format(CultureInfo.InvariantCulture, "Outputs[{0}].",
                        i.ToString(CultureInfo.InvariantCulture)) +
                    nameof(transactionOutput.DestinationAddress);
                try
                {
                    var address = BitcoinAddress.Create(transactionOutput.DestinationAddress, network.NBitcoinNetwork);
                    if (address is TaprootAddress)
                    {
                        var supportTaproot = _dashboard.Get(network.CryptoCode)?.Status?.BitcoinStatus?.Capabilities
                            ?.CanSupportTaproot;
                        if (!(supportTaproot is true))
                        {
                            ModelState.AddModelError(inputName,
                                "You need to update your full node, and/or NBXplorer (Version >= 2.1.56) to be able to send to a taproot address.");
                        }
                    }
                }
                catch
                {
                    ModelState.AddModelError(inputName, "Invalid address");
                }

                if (!bypassBalanceChecks && transactionOutput.Amount.HasValue)
                {
                    transactionAmountSum += transactionOutput.Amount.Value;

                    if (vm.CurrentBalance == transactionOutput.Amount.Value &&
                        !transactionOutput.SubtractFeesFromOutput)
                        vm.AddModelError(model => model.Outputs[i].SubtractFeesFromOutput,
                            "You are sending your entire balance to the same destination, you should subtract the fees",
                            this);
                }
            }

            if (!bypassBalanceChecks)
            {
                if (subtractFeesOutputsCount.Count > 1)
                {
                    foreach (var subtractFeesOutput in subtractFeesOutputsCount)
                    {
                        vm.AddModelError(model => model.Outputs[subtractFeesOutput].SubtractFeesFromOutput,
                            "You can only subtract fees from one output", this);
                    }
                }
                else if (vm.CurrentBalance == transactionAmountSum && !substractFees)
                {
                    ModelState.AddModelError(string.Empty,
                        "You are sending your entire balance, you should subtract the fees from an output");
                }

                if (vm.CurrentBalance < transactionAmountSum)
                {
                    for (var i = 0; i < vm.Outputs.Count; i++)
                    {
                        vm.AddModelError(model => model.Outputs[i].Amount,
                            "You are sending more than what you own", this);
                    }
                }

                if (vm.FeeSatoshiPerByte is decimal fee)
                {
                    if (fee < 0)
                    {
                        vm.AddModelError(model => model.FeeSatoshiPerByte,
                            "The fee rate should be above 0", this);
                    }
                }
            }

            if (!ModelState.IsValid)
                return View(vm);

            foreach (var transactionOutput in vm.Outputs.Where(output => output.Labels?.Any() is true))
            {
                var labels = transactionOutput.Labels.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
                var walletObjectAddress = new WalletObjectId(walletId, WalletObjectData.Types.Address, transactionOutput.DestinationAddress);
                var obj = await WalletRepository.GetWalletObject(walletObjectAddress);
                if (obj is null)
                {
                    await WalletRepository.EnsureWalletObject(walletObjectAddress);
                }
                await WalletRepository.AddWalletObjectLabels(walletObjectAddress, labels);
            }

            var derivationScheme = GetDerivationSchemeSettings(walletId);
            if (derivationScheme is null)
                return NotFound();
            CreatePSBTResponse psbtResponse;
            if (command == "schedule")
            {
                var pmi = PayoutTypes.CHAIN.GetPayoutMethodId(walletId.CryptoCode);
                var claims =
                    vm.Outputs.Where(output => string.IsNullOrEmpty(output.PayoutId)).Select(output => new ClaimRequest()
                    {
                        Destination = new AddressClaimDestination(
                            BitcoinAddress.Create(output.DestinationAddress, network.NBitcoinNetwork)),
                        ClaimedAmount = output.Amount,
                        PayoutMethodId = pmi,
                        StoreId = walletId.StoreId,
                        PreApprove = true,
                    }).ToArray();
                var someFailed = false;
                string? message = null;
                string? errorMessage = null;
                var result = new Dictionary<ClaimRequest, ClaimRequest.ClaimResult>();
                foreach (ClaimRequest claimRequest in claims)
                {
                    var response = await _pullPaymentHostedService.Claim(claimRequest);
                    result.Add(claimRequest, response.Result);
                    if (response.Result == ClaimRequest.ClaimResult.Ok)
                    {
                        if (message is null)
                        {
                            message = "Payouts scheduled:<br/>";
                        }

                        message += $"{claimRequest.ClaimedAmount} to {claimRequest.Destination.ToString()}<br/>";

                    }
                    else
                    {
                        someFailed = true;
                        if (errorMessage is null)
                        {
                            errorMessage = "Payouts failed to be scheduled:<br/>";
                        }

                        switch (response.Result)
                        {
                            case ClaimRequest.ClaimResult.Duplicate:
                                errorMessage += $"{claimRequest.ClaimedAmount} to {claimRequest.Destination.ToString()} - address reuse<br/>";
                                break;
                            case ClaimRequest.ClaimResult.AmountTooLow:
                                errorMessage += $"{claimRequest.ClaimedAmount} to {claimRequest.Destination.ToString()} - amount too low<br/>";
                                break;
                        }
                    }
                }

                if (message is not null && errorMessage is not null)
                {
                    message += $"<br/><br/>{errorMessage}";
                }
                else if (message is null && errorMessage is not null)
                {
                    message = errorMessage;
                }
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Severity = someFailed ? StatusMessageModel.StatusSeverity.Warning :
                        StatusMessageModel.StatusSeverity.Success,
                    Html = message
                });
                return RedirectToAction("Payouts", "UIStorePullPayments",
                    new
                    {
                        storeId = walletId.StoreId,
                        PaymentMethodId = pmi.ToString(),
                        payoutState = PayoutState.AwaitingPayment,
                    });
            }

            try
            {
                psbtResponse = await CreatePSBT(network, derivationScheme, vm, cancellation);
            }
            catch (NBXplorerException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Error.Message);
                return View(vm);
            }
            catch (NotSupportedException)
            {
                ModelState.AddModelError(string.Empty, "You need to update your version of NBXplorer");
                return View(vm);
            }

            var psbt = psbtResponse.PSBT;
            derivationScheme.RebaseKeyPaths(psbt);

            var signingContext = new SigningContextModel
            {
                PayJoinBIP21 = vm.PayJoinBIP21,
                EnforceLowR = psbtResponse.Suggestions?.ShouldEnforceLowR,
                ChangeAddress = psbtResponse.ChangeAddress?.ToString(),
                PSBT = psbt.ToHex()
            };
            switch (command)
            {
                case "createpending":
                    var pt = await _pendingTransactionService.CreatePendingTransaction(walletId.StoreId, walletId.CryptoCode, psbt);
                    return RedirectToAction(nameof(WalletTransactions), new { walletId = walletId.ToString() });
                case "sign":
                    return await WalletSign(walletId, new WalletPSBTViewModel
                    {
                        SigningContext = signingContext,
                        ReturnUrl = vm.ReturnUrl,
                        BackUrl = this.Url.WalletSend(walletId)
                    });
                case "analyze-psbt":
                    var name =
                        $"Send-{string.Join('_', vm.Outputs.Select(output => $"{output.Amount}->{output.DestinationAddress}{(output.SubtractFeesFromOutput ? "-Fees" : string.Empty)}"))}.psbt";
                    return RedirectToWalletPSBT(new WalletPSBTViewModel { PSBT = psbt.ToBase64(), FileName = name });
                default:
                    return View(vm);
            }
        }


        private async Task LoadFromBIP21(WalletId walletId, WalletSendModel vm, string bip21,
            BTCPayNetwork network, bool statusMessagePresent)
        {
            BitcoinAddress? address = null;
            vm.Outputs ??= new();
            try
            {
                var uriBuilder = new NBitcoin.Payment.BitcoinUrlBuilder(bip21, network.NBitcoinNetwork);
                var output = new WalletSendModel.TransactionOutput
                {
                    Amount = uriBuilder.Amount?.ToDecimal(MoneyUnit.BTC),
                    DestinationAddress = uriBuilder.Address?.ToString(),
                    SubtractFeesFromOutput = false,
                    PayoutId = uriBuilder.UnknownParameters.ContainsKey("payout")
                        ? uriBuilder.UnknownParameters["payout"]
                        : null
                };
                if (!string.IsNullOrEmpty(uriBuilder.Label))
                {
                    output.Labels = output.Labels.Append(uriBuilder.Label).ToArray();
                }
                vm.Outputs.Add(output);
                address = uriBuilder.Address;
                // only set SetStatusMessageModel if there is not message already or there is label / message in uri builder
                if (!statusMessagePresent)
                {
                    if (!string.IsNullOrEmpty(uriBuilder.Label) || !string.IsNullOrEmpty(uriBuilder.Message))
                    {
                        TempData.SetStatusMessageModel(new StatusMessageModel
                        {
                            Severity = StatusMessageModel.StatusSeverity.Info,
                            Html =
                                $"Payment {(string.IsNullOrEmpty(uriBuilder.Label) ? string.Empty : $" to <strong>{uriBuilder.Label}</strong>")} {(string.IsNullOrEmpty(uriBuilder.Message) ? string.Empty : $" for <strong>{uriBuilder.Message}</strong>")}"
                        });
                    }
                }

                if (uriBuilder.TryGetPayjoinEndpoint(out _))
                    vm.PayJoinBIP21 = uriBuilder.ToString();
            }
            catch
            {
                try
                {
                    address = BitcoinAddress.Create(bip21, network.NBitcoinNetwork);
                    vm.Outputs.Add(new WalletSendModel.TransactionOutput
                    {
                        DestinationAddress = address.ToString()
                    });
                }
                catch
                {
                    TempData.SetStatusMessageModel(new StatusMessageModel
                    {
                        Severity = StatusMessageModel.StatusSeverity.Error,
                        Message = StringLocalizer["The provided BIP21 payment URI was malformed"].Value
                    });
                }
            }

            ModelState.Clear();
            if (address is not null)
            {
                var addressLabels = await WalletRepository.GetWalletLabels(new WalletObjectId(walletId, WalletObjectData.Types.Address, address.ToString()));
                vm.Outputs.Last().Labels = vm.Outputs.Last().Labels.Concat(addressLabels.Select(tuple => tuple.Label)).ToArray();
            }
        }

        private IActionResult ViewVault(WalletId walletId, WalletPSBTViewModel vm)
        {
            return View(nameof(WalletSendVault),
                new WalletSendVaultModel
                {
                    SigningContext = vm.SigningContext,
                    WalletId = walletId.ToString(),
                    WebsocketPath = Url.Action(nameof(UIVaultController.VaultBridgeConnection), "UIVault",
                        new { walletId = walletId.ToString() }),
                    ReturnUrl = vm.ReturnUrl,
                    BackUrl = vm.BackUrl
                });
        }

        [HttpPost("{walletId}/vault")]
        public async Task<IActionResult> WalletSendVault([ModelBinder(typeof(WalletIdModelBinder))] WalletId walletId,
            WalletSendVaultModel model)
        {
            return await RedirectToWalletPSBTReady(walletId, new WalletPSBTReadyViewModel
            {
                SigningContext = model.SigningContext,
                ReturnUrl = model.ReturnUrl,
                BackUrl = model.BackUrl
            });
        }

        private async Task<IActionResult> RedirectToWalletPSBTReady(WalletId walletId, WalletPSBTReadyViewModel vm)
        {
            if (vm.SigningContext.PendingTransactionId is not null)
            {
                var psbt = PSBT.Parse(vm.SigningContext.PSBT, NetworkProvider.GetNetwork<BTCPayNetwork>(walletId.CryptoCode).NBitcoinNetwork);
                var pendingTransaction = await _pendingTransactionService.CollectSignature(psbt, CancellationToken.None);

                if (pendingTransaction != null)
                    return RedirectToAction(nameof(WalletTransactions), new { walletId = walletId.ToString() });
            }

            var redirectVm = new PostRedirectViewModel
            {
                AspController = "UIWallets",
                AspAction = nameof(WalletPSBTReady),
                RouteParameters = { { "walletId", this.RouteData?.Values["walletId"]?.ToString() } },
                FormParameters =
                {
                    { "SigningKey", vm.SigningKey },
                    { "SigningKeyPath", vm.SigningKeyPath },
                    { "command", "decode" }
                }
            };
            AddSigningContext(redirectVm, vm.SigningContext);
            if (!string.IsNullOrEmpty(vm.SigningContext.OriginalPSBT) &&
                !string.IsNullOrEmpty(vm.SigningContext.PSBT))
            {
                //if a hw device signed a payjoin, we want it broadcast instantly
                redirectVm.FormParameters.Remove("command");
                redirectVm.FormParameters.Add("command", "broadcast");
            }
            if (vm.ReturnUrl != null)
            {
                redirectVm.FormParameters.Add("returnUrl", vm.ReturnUrl);
            }
            if (vm.BackUrl != null)
            {
                redirectVm.FormParameters.Add("backUrl", vm.BackUrl);
            }
            return View("PostRedirect", redirectVm);
        }

        private void AddSigningContext(PostRedirectViewModel redirectVm, SigningContextModel signingContext)
        {
            if (signingContext is null)
                return;
            redirectVm.FormParameters.Add("SigningContext.PSBT", signingContext.PSBT);
            redirectVm.FormParameters.Add("SigningContext.OriginalPSBT", signingContext.OriginalPSBT);
            redirectVm.FormParameters.Add("SigningContext.PayJoinBIP21", signingContext.PayJoinBIP21);
            redirectVm.FormParameters.Add("SigningContext.EnforceLowR",
                signingContext.EnforceLowR?.ToString(CultureInfo.InvariantCulture));
            redirectVm.FormParameters.Add("SigningContext.ChangeAddress", signingContext.ChangeAddress);
            redirectVm.FormParameters.Add("SigningContext.PendingTransactionId", signingContext.PendingTransactionId);
            redirectVm.FormParameters.Add("SigningContext.BalanceChangeFromReplacement", signingContext.BalanceChangeFromReplacement.ToString());
        }

        private IActionResult RedirectToWalletPSBT(WalletPSBTViewModel vm)
        {
            var redirectVm = new PostRedirectViewModel
            {
                AspController = "UIWallets",
                AspAction = nameof(WalletPSBT),
                RouteParameters = { { "walletId", RouteData.Values["walletId"]?.ToString() } },
                FormParameters =
                {
                    { "psbt", vm.PSBT },
                    { "fileName", vm.FileName },
                    { "backUrl", vm.BackUrl },
                    { "returnUrl", vm.ReturnUrl },
                    { "command", "decode" }
                }
            };
            return View("PostRedirect", redirectVm);
        }

        [HttpGet("{walletId}/psbt/seed")]
        public IActionResult SignWithSeed([ModelBinder(typeof(WalletIdModelBinder))] WalletId walletId,
            SigningContextModel signingContext, string returnUrl, string backUrl)
        {
            return View(nameof(SignWithSeed), new SignWithSeedViewModel
            {
                SigningContext = signingContext,
                ReturnUrl = returnUrl,
                BackUrl = backUrl
            });
        }

        [HttpPost("{walletId}/psbt/seed")]
        public async Task<IActionResult> SignWithSeed([ModelBinder(typeof(WalletIdModelBinder))] WalletId walletId,
            SignWithSeedViewModel viewModel)
        {
            if (!ModelState.IsValid)
            {
                return View("SignWithSeed", viewModel);
            }
            var network = NetworkProvider.GetNetwork<BTCPayNetwork>(walletId.CryptoCode);
            if (network == null)
                throw new FormatException("Invalid value for crypto code");
            ExtKey extKey = viewModel.GetExtKey(network.NBitcoinNetwork);

            if (extKey is null)
            {
                ModelState.AddModelError(nameof(viewModel.SeedOrKey),
                    "Seed or Key was not in a valid format. It is either the 12/24 words or starts with xprv");
            }

            var psbt = PSBT.Parse(viewModel.SigningContext.PSBT, network.NBitcoinNetwork);

            if (!psbt.IsReadyToSign())
            {
                ModelState.AddModelError(nameof(viewModel.SigningContext.PSBT), "PSBT is not ready to be signed");
            }

            if (!ModelState.IsValid)
            {
                return View("SignWithSeed", viewModel);
            }
            // It will never throw, this make nullable check below happy
            ArgumentNullException.ThrowIfNull(extKey);

            ExtKey? signingKey = null;
            var settings = GetDerivationSchemeSettings(walletId);
            if (settings is null)
                return NotFound();
            var signingKeySettings = settings.GetSigningAccountKeySettings();
            if (signingKeySettings.RootFingerprint is null)
                signingKeySettings.RootFingerprint = extKey.GetPublicKey().GetHDFingerPrint();

            RootedKeyPath rootedKeyPath = signingKeySettings.GetRootedKeyPath();
            if (rootedKeyPath == null)
            {
                ModelState.AddModelError(nameof(viewModel.SeedOrKey),
                    "The master fingerprint and/or account key path of your seed are not set in the wallet settings.");
                return View(nameof(SignWithSeed), viewModel);
            }
            // The user gave the root key, let's try to rebase the PSBT, and derive the account private key
            if (rootedKeyPath.MasterFingerprint == extKey.GetPublicKey().GetHDFingerPrint())
            {
                psbt.RebaseKeyPaths(signingKeySettings.AccountKey, rootedKeyPath);
                signingKey = extKey.Derive(rootedKeyPath.KeyPath);
            }
            else
            {
                ModelState.AddModelError(nameof(viewModel.SeedOrKey),
                    "The master fingerprint does not match the one set in your wallet settings. Probable causes are: wrong seed, wrong passphrase or wrong fingerprint in your wallet settings.");
                return View(nameof(SignWithSeed), viewModel);
            }

            psbt.Settings.SigningOptions = new SigningOptions()
            {
                EnforceLowR = !(viewModel.SigningContext?.EnforceLowR is false)
            };
            var changed = psbt.PSBTChanged(() => psbt.SignAll(settings.AccountDerivation, signingKey, rootedKeyPath));
            if (!changed)
            {
                var update = new UpdatePSBTRequest() { PSBT = psbt, DerivationScheme = settings.AccountDerivation };
                update.RebaseKeyPaths = settings.GetPSBTRebaseKeyRules().ToList();
                psbt = (await ExplorerClientProvider.GetExplorerClient(network).UpdatePSBTAsync(update))?.PSBT;
                changed = psbt is not null && psbt.PSBTChanged(() =>
                    psbt.SignAll(settings.AccountDerivation, signingKey, rootedKeyPath));
                if (!changed)
                {
                    ModelState.AddModelError(nameof(viewModel.SeedOrKey),
                        "Impossible to sign the transaction. Probable causes: Incorrect account key path in wallet settings or PSBT already signed.");
                    return View(nameof(SignWithSeed), viewModel);
                }
            }
            ModelState.Remove(nameof(viewModel.SigningContext.PSBT));
            viewModel.SigningContext ??= new();
            viewModel.SigningContext.PSBT = psbt?.ToBase64();
            return await RedirectToWalletPSBTReady(walletId, new WalletPSBTReadyViewModel
            {
                SigningKey = signingKey.GetWif(network.NBitcoinNetwork).ToString(),
                SigningKeyPath = rootedKeyPath?.ToString(),
                SigningContext = viewModel.SigningContext,
                ReturnUrl = viewModel.ReturnUrl,
                BackUrl = viewModel.BackUrl
            });
        }

        private WalletPSBTReadyViewModel.StringAmounts ValueToString(Money v, BTCPayNetworkBase network,
            FiatRate? rate) =>
            new(
                CryptoAmount : _displayFormatter.Currency(v.ToDecimal(MoneyUnit.BTC), network.CryptoCode),
                FiatAmount : rate is null ? null
                    : _displayFormatter.Currency(rate.Rate * v.ToDecimal(MoneyUnit.BTC), rate.Fiat)
            );

        [HttpGet("{walletId}/rescan")]
        public async Task<IActionResult> WalletRescan(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId)
        {
            if (walletId?.StoreId == null)
                return NotFound();
            var paymentMethod = GetDerivationSchemeSettings(walletId);
            if (paymentMethod == null)
                return NotFound();

            var vm = new RescanWalletModel();
            vm.IsFullySync = _dashboard.IsFullySynched(walletId.CryptoCode, out var unused);
            vm.IsServerAdmin = (await _authorizationService.AuthorizeAsync(User, Policies.CanModifyServerSettings))
                .Succeeded;
            vm.IsSupportedByCurrency =
                _dashboard.Get(walletId.CryptoCode)?.Status?.BitcoinStatus?.Capabilities?.CanScanTxoutSet == true;
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
                    vm.TimeOfScan = (scanProgress.Progress!.CompletedAt!.Value - scanProgress.Progress.StartedAt)
                        .PrettyPrint();
                }
            }

            return View(vm);
        }

        [HttpPost("{walletId}/rescan")]
        [Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> WalletRescan(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, RescanWalletModel vm)
        {
            if (walletId?.StoreId == null)
                return NotFound();
            var paymentMethod = GetDerivationSchemeSettings(walletId);
            if (paymentMethod == null)
                return NotFound();
            var explorer = ExplorerClientProvider.GetExplorerClient(walletId.CryptoCode);
            try
            {
                await explorer.ScanUTXOSetAsync(paymentMethod.AccountDerivation, vm.BatchSize, vm.GapLimit,
                    vm.StartingIndex);
                _walletProvider.GetWallet(walletId.CryptoCode).InvalidateCache(paymentMethod.AccountDerivation);
            }
            catch (NBXplorerException ex) when (ex.Error.Code == "scanutxoset-in-progress")
            {
            }

            return RedirectToAction();
        }

        internal DerivationSchemeSettings? GetDerivationSchemeSettings(WalletId walletId)
        {
            return GetCurrentStore().GetDerivationSchemeSettings(_handlers, walletId.CryptoCode);
        }

        private static async Task<IMoney> GetBalanceAsMoney(BTCPayWallet wallet,
            DerivationStrategyBase derivationStrategy)
        {
            using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                var b = await wallet.GetBalance(derivationStrategy, cts.Token);
                return b.Available ?? b.Total;
            }
            catch
            {
                return Money.Zero;
            }
        }

        internal async Task<string> GetBalanceString(BTCPayWallet wallet, DerivationStrategyBase? derivationStrategy)
        {
            if (derivationStrategy is null)
                return "--";
            try
            {
                return (await GetBalanceAsMoney(wallet, derivationStrategy)).ShowMoney(wallet.Network);
            }
            catch
            {
                return "--";
            }
        }

        [HttpPost("{walletId}/actions")]
        public async Task<IActionResult> WalletActions(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, string command,
            string[] selectedTransactions,
            CancellationToken cancellationToken = default)
        {
            var derivationScheme = GetDerivationSchemeSettings(walletId);
            var network = _handlers.GetBitcoinHandler(walletId.CryptoCode).Network;
            if (derivationScheme == null || network.ReadonlyWallet)
                return NotFound();

            switch (command)
            {
                case "cpfp":
                    {
                        selectedTransactions ??= Array.Empty<string>();
                        if (selectedTransactions.Length == 0)
                        {
                            TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["No transaction selected"].Value;
                            return RedirectToAction(nameof(WalletTransactions), new { walletId });
                        }

                        var parameters = new MultiValueDictionary<string, string>();
                        parameters.Add("walletId", walletId.ToString());
                        int i = 0;
                        foreach (var tx in selectedTransactions)
                        {
                            parameters.Add($"transactionHashes[{i}]", tx);
                            i++;
                        }
                        return View("PostRedirect",
                            new PostRedirectViewModel
                            {
                                AspController = "UIWallets",
                                AspAction = nameof(WalletBumpFee),
                                RouteParameters = { { "walletId", walletId.ToString() } },
                                FormParameters = parameters
                            });
                    }
                case "prune":
                    {
                        var result = await ExplorerClientProvider.GetExplorerClient(walletId.CryptoCode)
                            .PruneAsync(derivationScheme.AccountDerivation, new PruneRequest(), cancellationToken);
                        if (result.TotalPruned == 0)
                        {
                            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["The wallet is already pruned"].Value;
                        }
                        else
                        {
                            TempData[WellKnownTempData.SuccessMessage] =
                                StringLocalizer["The wallet has been successfully pruned ({0} transactions have been removed from the history)", result.TotalPruned].Value;
                        }

                        return RedirectToAction(nameof(WalletTransactions), new { walletId });
                    }
                case "clear" when User.IsInRole(Roles.ServerAdmin):
                    {
                        if (Version.TryParse(_dashboard.Get(walletId.CryptoCode)?.Status?.Version ?? "0.0.0.0",
                                out var v) &&
                            v < new Version(2, 2, 4))
                        {
                            TempData[WellKnownTempData.ErrorMessage] =
                                "This version of NBXplorer doesn't support this operation, please upgrade to 2.2.4 or above";
                        }
                        else
                        {
                            await ExplorerClientProvider.GetExplorerClient(walletId.CryptoCode)
                                .WipeAsync(derivationScheme.AccountDerivation, cancellationToken);
                            TempData[WellKnownTempData.SuccessMessage] =
                                "The transactions have been wiped out, to restore your balance, rescan the wallet.";
                        }

                        return RedirectToAction(nameof(WalletTransactions), new { walletId });
                    }
                default:
                    return NotFound();
            }
        }

        [HttpGet("{walletId}/export")]
        public async Task<IActionResult> Export(
            [ModelBinder(typeof(WalletIdModelBinder))] WalletId walletId,
            string format, string? labelFilter = null, CancellationToken cancellationToken = default)
        {
            var paymentMethod = GetDerivationSchemeSettings(walletId);
            if (paymentMethod == null)
                return NotFound();

            var network = _handlers.GetBitcoinHandler(walletId.CryptoCode).Network;
            var wallet = _walletProvider.GetWallet(network);
            var walletTransactionsInfoAsync = WalletRepository.GetWalletTransactionsInfo(walletId, (string[]?)null);
            var input = await wallet.FetchTransactionHistory(paymentMethod.AccountDerivation, cancellationToken: cancellationToken);
            var walletTransactionsInfo = await walletTransactionsInfoAsync;
            var export = new TransactionsExport(wallet, walletTransactionsInfo);
            var res = export.Process(input, format);
            var fileType = format switch
            {
                "csv" => "csv",
                "json" => "json",
                "bip329" => "jsonl",
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
            };
            var mimeType = format switch
            {
                "csv" => "text/csv",
                "json" => "application/json",
                "bip329" => "application/jsonl", // Ongoing discussion: https://github.com/wardi/jsonlines/issues/19
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
            };
            var cd = new ContentDisposition
            {
                FileName = $"btcpay-{walletId}-{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.{fileType}",
                Inline = true
            };
            Response.Headers.Add("Content-Disposition", cd.ToString());
            Response.Headers.Add("X-Content-Type-Options", "nosniff");
            return Content(res, mimeType);
        }

        public class UpdateLabelsRequest
        {
            public string? Id { get; set; }
            public string? Type { get; set; }
            public string[]? Labels { get; set; }
        }

        [HttpPost("{walletId}/update-labels")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> UpdateLabels(
            [ModelBinder(typeof(WalletIdModelBinder))] WalletId walletId,
            [FromBody] UpdateLabelsRequest request)
        {
            if (string.IsNullOrEmpty(request.Type) || string.IsNullOrEmpty(request.Id) || request.Labels is null)
                return BadRequest();

            var objid = new WalletObjectId(walletId, request.Type, request.Id);
            var obj = await WalletRepository.GetWalletObject(objid);
            if (obj is null)
            {
                await WalletRepository.EnsureWalletObject(objid);
            }
            else
            {
                var currentLabels = obj.GetNeighbours().Where(data => data.Type == WalletObjectData.Types.Label).ToArray();
                var toRemove = currentLabels.Where(data => !request.Labels.Contains(data.Id)).Select(data => data.Id).ToArray();
                await WalletRepository.RemoveWalletObjectLabels(objid, toRemove);
            }
            await WalletRepository.AddWalletObjectLabels(objid, request.Labels);
            return Ok();
        }

        [HttpGet("{walletId}/labels.json")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> LabelsJson(
            [ModelBinder(typeof(WalletIdModelBinder))] WalletId walletId,
            bool excludeTypes,
            string? type = null,
            string? id = null)
        {
            var walletObjectId = !string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(id)
                ? new WalletObjectId(walletId, type, id)
                : null;
            var labels = walletObjectId == null
                ? await WalletRepository.GetWalletLabels(walletId)
                : await WalletRepository.GetWalletLabels(walletObjectId);
            return Ok(labels
                .Where(l => !excludeTypes || !WalletObjectData.Types.AllTypes.Contains(l.Label))
                .Select(tuple => new WalletLabelModel
                {
                    Label = tuple.Label,
                    Color = tuple.Color,
                    TextColor = ColorPalette.Default.TextColor(tuple.Color)
                }));
        }

        [HttpGet("{walletId}/labels")]
        public async Task<IActionResult> WalletLabels(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId)
        {
            if (walletId.StoreId == null)
                return NotFound();

            var labels = await WalletRepository.GetWalletLabels(walletId);

            var vm = new WalletLabelsModel
            {
                WalletId = walletId,
                Labels = labels
                    .Where(l => !WalletObjectData.Types.AllTypes.Contains(l.Label))
                    .Select(tuple => new WalletLabelModel
                    {
                        Label = tuple.Label,
                        Color = tuple.Color,
                        TextColor = ColorPalette.Default.TextColor(tuple.Color)
                    })
            };

            return View(vm);
        }

        [HttpPost("{walletId}/labels/{id}/remove")]
        public async Task<IActionResult> RemoveWalletLabel(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, string id)
        {
            if (walletId.StoreId == null)
                return NotFound();

            var labels = new[] { id };
            ;
            if (await WalletRepository.RemoveWalletLabels(walletId, labels))
            {
                TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["The label has been successfully removed."].Value;
            }
            else
            {
                TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["The label could not be removed."].Value;
            }

            return RedirectToAction(nameof(WalletLabels), new { walletId });
        }

        private string? GetImage(BTCPayNetwork network)
        {
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
            if (_paymentModelExtensions.TryGetValue(pmi, out var extension))
            {
                return Request.GetRelativePathOrAbsolute(Url.Content(extension.Image));
            }
            return null;
        }

        private string GetUserId() => _userManager.GetUserId(User)!;

        private StoreData GetCurrentStore() => HttpContext.GetStoreData();
    }

    public class WalletReceiveViewModel
    {
        public string? CryptoImage { get; set; }
        public string? CryptoCode { get; set; }
        public string? Address { get; set; }
        public string? PaymentLink { get; set; }
        public string? ReturnUrl { get; set; }
        public string[]? SelectedLabels { get; set; }
    }

    public class SendToAddressResult
    {
        [JsonProperty("psbt")] public string? PSBT { get; set; }
    }
}
