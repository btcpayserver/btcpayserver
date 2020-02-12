using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.ModelBinders;
using BTCPayServer.Models;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Payments;
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
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBXplorer;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Newtonsoft.Json;
using static BTCPayServer.Controllers.StoresController;

namespace BTCPayServer.Controllers
{
    [Route("wallets")]
    [Authorize(Policy = Policies.CanModifyStoreSettings.Key, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [AutoValidateAntiforgeryToken]
    public partial class WalletsController : Controller
    {
        public StoreRepository Repository { get; }
        public WalletRepository WalletRepository { get; }
        public BTCPayNetworkProvider NetworkProvider { get; }
        public ExplorerClientProvider ExplorerClientProvider { get; }

        private readonly UserManager<ApplicationUser> _userManager;
        private readonly JsonSerializerSettings _serializerSettings;
        private readonly NBXplorerDashboard _dashboard;
        private readonly IAuthorizationService _authorizationService;
        private readonly IFeeProviderFactory _feeRateProvider;
        private readonly BTCPayWalletProvider _walletProvider;
        private readonly WalletReceiveStateService _WalletReceiveStateService;
        private readonly EventAggregator _EventAggregator;
        private readonly SettingsRepository _settingsRepository;
        public RateFetcher RateFetcher { get; }

        CurrencyNameTable _currencyTable;
        public WalletsController(StoreRepository repo,
                                 WalletRepository walletRepository,
                                 CurrencyNameTable currencyTable,
                                 BTCPayNetworkProvider networkProvider,
                                 UserManager<ApplicationUser> userManager,
                                 MvcNewtonsoftJsonOptions mvcJsonOptions,
                                 NBXplorerDashboard dashboard,
                                 RateFetcher rateProvider,
                                 IAuthorizationService authorizationService,
                                 ExplorerClientProvider explorerProvider,
                                 IFeeProviderFactory feeRateProvider,
                                 BTCPayWalletProvider walletProvider,
                                 WalletReceiveStateService walletReceiveStateService,
                                 EventAggregator eventAggregator,
                                 SettingsRepository settingsRepository)
        {
            _currencyTable = currencyTable;
            Repository = repo;
            WalletRepository = walletRepository;
            RateFetcher = rateProvider;
            _authorizationService = authorizationService;
            NetworkProvider = networkProvider;
            _userManager = userManager;
            _serializerSettings = mvcJsonOptions.SerializerSettings;
            _dashboard = dashboard;
            ExplorerClientProvider = explorerProvider;
            _feeRateProvider = feeRateProvider;
            _walletProvider = walletProvider;
            _WalletReceiveStateService = walletReceiveStateService;
            _EventAggregator = eventAggregator;
            _settingsRepository = settingsRepository;
        }

        // Borrowed from https://github.com/ManageIQ/guides/blob/master/labels.md
        string[] LabelColorScheme = new string[] 
        {
            "#fbca04",
            "#0e8a16",
            "#ff7619",
            "#84b6eb",
            "#5319e7",
            "#000000",
            "#cc317c",
        };

        const int MaxLabelSize = 20;
        const int MaxCommentSize = 200;
        [HttpPost]
        [Route("{walletId}")]
        public async Task<IActionResult> ModifyTransaction(
            // We need addlabel and addlabelclick. addlabel is the + button if the label does not exists,
            // addlabelclick is if the user click on existing label. For some reason, reusing the same name attribute for both
            // does not work
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, string transactionId, 
                                string addlabel = null, 
                                string addlabelclick = null,
                                string addcomment = null, 
                                string removelabel = null)
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
            
            DerivationSchemeSettings paymentMethod = GetDerivationSchemeSettings(walletId);
            if (paymentMethod == null)
                return NotFound();

            var walletBlobInfoAsync = WalletRepository.GetWalletInfo(walletId);
            var walletTransactionsInfoAsync = WalletRepository.GetWalletTransactionsInfo(walletId);
            var wallet = _walletProvider.GetWallet(paymentMethod.Network);
            var walletBlobInfo = await walletBlobInfoAsync;
            var walletTransactionsInfo = await walletTransactionsInfoAsync;
            if (addlabel != null)
            {
                addlabel = addlabel.Trim().ToLowerInvariant().Replace(',',' ').Truncate(MaxLabelSize);
                var labels = walletBlobInfo.GetLabels();
                if (!walletTransactionsInfo.TryGetValue(transactionId, out var walletTransactionInfo))
                {
                    walletTransactionInfo = new WalletTransactionInfo();
                }
                if (!labels.Any(l => l.Value.Equals(addlabel, StringComparison.OrdinalIgnoreCase)))
                {
                    List<string> allColors = new List<string>();
                    allColors.AddRange(LabelColorScheme);
                    allColors.AddRange(labels.Select(l => l.Color));
                    var chosenColor =
                        allColors
                        .GroupBy(k => k)
                        .OrderBy(k => k.Count())
                        .ThenBy(k => Array.IndexOf(LabelColorScheme, k.Key))
                        .First().Key;
                    walletBlobInfo.LabelColors.Add(addlabel, chosenColor);
                    await WalletRepository.SetWalletInfo(walletId, walletBlobInfo);
                }
                if (walletTransactionInfo.Labels.Add(addlabel))
                {
                    await WalletRepository.SetWalletTransactionInfo(walletId, transactionId, walletTransactionInfo);
                }
            }
            else if (removelabel != null)
            {
                removelabel = removelabel.Trim().ToLowerInvariant().Truncate(MaxLabelSize);
                if (walletTransactionsInfo.TryGetValue(transactionId, out var walletTransactionInfo))
                {
                    if (walletTransactionInfo.Labels.Remove(removelabel))
                    {
                        var canDelete = !walletTransactionsInfo.SelectMany(txi => txi.Value.Labels).Any(l => l == removelabel);
                        if (canDelete)
                        {
                            walletBlobInfo.LabelColors.Remove(removelabel);
                            await WalletRepository.SetWalletInfo(walletId, walletBlobInfo);
                        }
                        await WalletRepository.SetWalletTransactionInfo(walletId, transactionId, walletTransactionInfo);
                    }
                }
            }
            else if (addcomment != null)
            {
                addcomment = addcomment.Trim().Truncate(MaxCommentSize);
                if (!walletTransactionsInfo.TryGetValue(transactionId, out var walletTransactionInfo))
                {
                    walletTransactionInfo = new WalletTransactionInfo();
                }
                walletTransactionInfo.Comment = addcomment;
                await WalletRepository.SetWalletTransactionInfo(walletId, transactionId, walletTransactionInfo);
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
                                .SelectMany(s => s.GetSupportedPaymentMethods(NetworkProvider)
                                              .OfType<DerivationSchemeSettings>()
                                              .Select(d => ((Wallet: _walletProvider.GetWallet(d.Network),
                                                            DerivationStrategy: d.AccountDerivation,
                                                            Network: d.Network)))
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
                walletVm.IsOwner = wallet.Store.Role == StoreRoles.Owner;
                if (!walletVm.IsOwner)
                {
                    walletVm.Balance = "";
                }
                walletVm.CryptoCode = wallet.Network.CryptoCode;
                walletVm.StoreId = wallet.Store.Id;
                walletVm.Id = new WalletId(wallet.Store.Id, wallet.Network.CryptoCode);
                walletVm.StoreName = wallet.Store.StoreName;
            }

            return View(wallets);
        }

        [HttpGet]
        [Route("{walletId}")]
        [Route("{walletId}/transactions")]
        public async Task<IActionResult> WalletTransactions(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, string labelFilter = null)
        {
            DerivationSchemeSettings paymentMethod = GetDerivationSchemeSettings(walletId);
            if (paymentMethod == null)
                return NotFound();

            var wallet = _walletProvider.GetWallet(paymentMethod.Network);
            var walletBlobAsync = WalletRepository.GetWalletInfo(walletId);
            var walletTransactionsInfoAsync = WalletRepository.GetWalletTransactionsInfo(walletId);
            var transactions = await wallet.FetchTransactions(paymentMethod.AccountDerivation);
            var walletBlob = await walletBlobAsync;
            var walletTransactionsInfo = await walletTransactionsInfoAsync;
            var model = new ListTransactionsViewModel();
            if (transactions == null)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Message =
                        "There was an error retrieving the transactions list. Is NBXplorer configured correctly?"
                });
                model.Transactions = new List<ListTransactionsViewModel.TransactionViewModel>();
            }
            else
            {
                foreach (var tx in transactions.UnconfirmedTransactions.Transactions
                    .Concat(transactions.ConfirmedTransactions.Transactions).ToArray())
                {
                    var vm = new ListTransactionsViewModel.TransactionViewModel();
                    vm.Id = tx.TransactionId.ToString();
                    vm.Link = string.Format(CultureInfo.InvariantCulture, paymentMethod.Network.BlockExplorerLink,
                        vm.Id);
                    vm.Timestamp = tx.Timestamp;
                    vm.Positive = tx.BalanceChange.GetValue(wallet.Network) >= 0;
                    vm.Balance = tx.BalanceChange.ToString();
                    vm.IsConfirmed = tx.Confirmations != 0;

                    if (walletTransactionsInfo.TryGetValue(tx.TransactionId.ToString(), out var transactionInfo))
                    {
                        var labels = walletBlob.GetLabels(transactionInfo);
                        vm.Labels.AddRange(labels);
                        model.Labels.AddRange(labels);
                        vm.Comment = transactionInfo.Comment;
                    }

                    if (labelFilter == null ||
                        vm.Labels.Any(l => l.Value.Equals(labelFilter, StringComparison.OrdinalIgnoreCase)))
                        model.Transactions.Add(vm);
                }

                model.Transactions = model.Transactions.OrderByDescending(t => t.Timestamp).ToList();
            }

            return View(model);
        }

        private static string GetLabelTarget(WalletId walletId, uint256 txId)
        {
            return $"{walletId}:{txId}";
        }

        [HttpGet]
        [Route("{walletId}/receive")]
        public IActionResult WalletReceive([ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId)
        {
            if (walletId?.StoreId == null)
                return NotFound();
            DerivationSchemeSettings paymentMethod = GetDerivationSchemeSettings(walletId);
            if (paymentMethod == null)
                return NotFound();
            var network = NetworkProvider.GetNetwork<BTCPayNetwork>(walletId?.CryptoCode);
            if (network == null)
                return NotFound();

            var address = _WalletReceiveStateService.Get(walletId)?.Address;
            return View(new WalletReceiveViewModel()
            {
                CryptoCode = walletId.CryptoCode,
                Address = address?.ToString(),
                CryptoImage = GetImage(paymentMethod.PaymentId, network)
            });
        }

        [HttpPost]
        [Route("{walletId}/receive")]
        public async Task<IActionResult> WalletReceive([ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, WalletReceiveViewModel viewModel, string command)
        {
            if (walletId?.StoreId == null)
                return NotFound();
            DerivationSchemeSettings paymentMethod = GetDerivationSchemeSettings(walletId);
            if (paymentMethod == null)
                return NotFound();
            var network = this.NetworkProvider.GetNetwork<BTCPayNetwork>(walletId?.CryptoCode);
            if (network == null)
                return NotFound();
            var wallet = _walletProvider.GetWallet(network);
            switch (command)
            {
                case "unreserve-current-address":
                    KeyPathInformation cachedAddress = _WalletReceiveStateService.Get(walletId);
                    if (cachedAddress == null)
                    {
                        break;
                    }
                    var address = cachedAddress.ScriptPubKey.GetDestinationAddress(network.NBitcoinNetwork);
                    ExplorerClientProvider.GetExplorerClient(network)
                        .CancelReservation(cachedAddress.DerivationStrategy, new[] {cachedAddress.KeyPath});
                    this.TempData.SetStatusMessageModel(new StatusMessageModel()
                    {
                        AllowDismiss = true,
                        Message = $"Address {address} was unreserved.",
                        Severity = StatusMessageModel.StatusSeverity.Success,
                    });
                    _WalletReceiveStateService.Remove(walletId);
                    break;
                case "generate-new-address":
                    var reserve = (await wallet.ReserveAddressAsync(paymentMethod.AccountDerivation));
                    _WalletReceiveStateService.Set(walletId, reserve);
                    break;
            }
            return RedirectToAction(nameof(WalletReceive), new {walletId});
        }

        private async Task<bool> CanUseHotWallet()
        {
            var isAdmin = (await _authorizationService.AuthorizeAsync(User, Policies.CanModifyServerSettings.Key)).Succeeded;
            if (isAdmin)
                return true;
            var policies = await _settingsRepository.GetSettingAsync<PoliciesSettings>();
            return policies?.AllowHotWalletForAll is true;
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
            DerivationSchemeSettings paymentMethod = GetDerivationSchemeSettings(walletId);
            if (paymentMethod == null)
                return NotFound();
            var network = this.NetworkProvider.GetNetwork<BTCPayNetwork>(walletId?.CryptoCode);
            if (network == null || network.ReadonlyWallet)
                return NotFound();
            var storeData = store.GetStoreBlob();
            var rateRules = store.GetStoreBlob().GetRateRules(NetworkProvider);
            rateRules.Spread = 0.0m;
            var currencyPair = new Rating.CurrencyPair(paymentMethod.PaymentId.CryptoCode, GetCurrencyCode(storeData.DefaultLang) ?? "USD");
            double.TryParse(defaultAmount, out var amount);
           var model = new WalletSendModel()
            {
                Outputs = new List<WalletSendModel.TransactionOutput>()
                {
                    new WalletSendModel.TransactionOutput()
                    {
                        Amount = Convert.ToDecimal(amount),
                        DestinationAddress = defaultDestination
                    }
                },
                CryptoCode = walletId.CryptoCode
            };
            

            var feeProvider = _feeRateProvider.CreateFeeProvider(network);
            var recommendedFees = feeProvider.GetFeeRateAsync();
            var balance = _walletProvider.GetWallet(network).GetBalance(paymentMethod.AccountDerivation);
            model.NBXSeedAvailable = await CanUseHotWallet() && !string.IsNullOrEmpty(await ExplorerClientProvider.GetExplorerClient(network)
                .GetMetadataAsync<string>(GetDerivationSchemeSettings(walletId).AccountDerivation,
                    WellknownMetadataKeys.Mnemonic));
            model.CurrentBalance = await balance;
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
            WalletId walletId, WalletSendModel vm, string command = "", CancellationToken cancellation = default)
        {
            if (walletId?.StoreId == null)
                return NotFound();
            var store = await Repository.FindStore(walletId.StoreId, GetUserId());
            if (store == null)
                return NotFound();
            var network = this.NetworkProvider.GetNetwork<BTCPayNetwork>(walletId?.CryptoCode);
            if (network == null || network.ReadonlyWallet)
                return NotFound();
            vm.SupportRBF = network.SupportRBF;
            decimal transactionAmountSum  = 0;
            
            if (command == "add-output")
            {
                ModelState.Clear();
                vm.Outputs.Add(new WalletSendModel.TransactionOutput());
                return View(vm);
            }
            if (command.StartsWith("remove-output", StringComparison.InvariantCultureIgnoreCase))
            {
                ModelState.Clear();
                var index = int.Parse(command.Substring(command.IndexOf(":",StringComparison.InvariantCultureIgnoreCase) + 1),  CultureInfo.InvariantCulture);
                vm.Outputs.RemoveAt(index);
                return View(vm);
            }
            

            if (!vm.Outputs.Any())
            {
                ModelState.AddModelError(string.Empty,
                    "Please add at least one transaction output");
                return View(vm);
            }

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

                try
                {
                    BitcoinAddress.Create(transactionOutput.DestinationAddress, network.NBitcoinNetwork);    
                }
                catch
                {
                    var inputName = 
                        string.Format(CultureInfo.InvariantCulture, "Outputs[{0}].", i.ToString(CultureInfo.InvariantCulture)) + 
                        nameof(transactionOutput.DestinationAddress);

                    ModelState.AddModelError(inputName, "Invalid address");
                }

                if (transactionOutput.Amount.HasValue)
                {
                    transactionAmountSum += transactionOutput.Amount.Value;

                    if (vm.CurrentBalance == transactionOutput.Amount.Value &&
                        !transactionOutput.SubtractFeesFromOutput)
                        vm.AddModelError(model => model.Outputs[i].SubtractFeesFromOutput,
                            "You are sending your entire balance to the same destination, you should subtract the fees",
                            this);
                }
            }

            if (subtractFeesOutputsCount.Count > 1)
            {
                foreach (var subtractFeesOutput in subtractFeesOutputsCount)
                {
                    vm.AddModelError(model => model.Outputs[subtractFeesOutput].SubtractFeesFromOutput,
                        "You can only subtract fees from one output", this);
                }
            }else if (vm.CurrentBalance == transactionAmountSum && !substractFees)
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

            if (!ModelState.IsValid) 
                return View(vm);

            DerivationSchemeSettings derivationScheme = GetDerivationSchemeSettings(walletId);

            CreatePSBTResponse psbt = null;
            try
            {
                psbt = await CreatePSBT(network, derivationScheme, vm, cancellation);
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
            derivationScheme.RebaseKeyPaths(psbt.PSBT);
            
            switch (command)
            {
                case "vault":
                    return ViewVault(walletId, psbt.PSBT);
                case "nbx-seed":
                  var extKey = await ExplorerClientProvider.GetExplorerClient(network)
                        .GetMetadataAsync<string>(derivationScheme.AccountDerivation, WellknownMetadataKeys.MasterHDKey, cancellation);

                  return await SignWithSeed(walletId, new SignWithSeedViewModel()
                  {
                      SeedOrKey = extKey,
                      PSBT = psbt.PSBT.ToBase64()
                  });
                case "ledger":
                    return ViewWalletSendLedger(walletId, psbt.PSBT, psbt.ChangeAddress);
                case "seed":
                    return SignWithSeed(walletId, psbt.PSBT.ToBase64());
                case "analyze-psbt":
                    var name =
                        $"Send-{string.Join('_', vm.Outputs.Select(output => $"{output.Amount}->{output.DestinationAddress}{(output.SubtractFeesFromOutput ? "-Fees" : string.Empty)}"))}.psbt";
                    return RedirectToWalletPSBT(walletId, psbt.PSBT, name);
                default:
                    return View(vm);
            }
            
        }

        private IActionResult ViewVault(WalletId walletId, PSBT psbt)
        {
            return View("WalletSendVault", new WalletSendVaultModel()
            {
                WalletId = walletId.ToString(),
                PSBT = psbt.ToBase64(),
                WebsocketPath = this.Url.Action(nameof(VaultController.VaultBridgeConnection), "Vault", new { walletId = walletId.ToString() })
            });
        }

        private IActionResult RedirectToWalletPSBT(WalletId walletId, PSBT psbt, string fileName = null)
        {
            var vm = new PostRedirectViewModel()
            {
                AspController = "Wallets",
                AspAction = nameof(WalletPSBT),
                Parameters =
                {
                    new KeyValuePair<string, string>("psbt", psbt.ToBase64())
                }
            };
            if (!string.IsNullOrEmpty(fileName))
                vm.Parameters.Add(new KeyValuePair<string, string>("fileName", fileName));
            return View("PostRedirect", vm);
        }

        void SetAmbientPSBT(PSBT psbt)
        {
            if (psbt != null)
                TempData["AmbientPSBT"] = psbt.ToBase64();
            else
                TempData.Remove("AmbientPSBT");
        }
        PSBT GetAmbientPSBT(Network network, bool peek)
        {
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            if ((peek ? TempData.Peek("AmbientPSBT") : TempData["AmbientPSBT"]) is string str)
            {
                try
                {
                    return PSBT.Parse(str, network);
                }
                catch { }
            }
            return null;
        }

        private ViewResult ViewWalletSendLedger(WalletId walletId, PSBT psbt, BitcoinAddress hintChange = null)
        {
            SetAmbientPSBT(psbt);
            return View("WalletSendLedger", new WalletSendLedgerModel()
            {
                PSBT = psbt.ToBase64(),
                HintChange = hintChange?.ToString(),
                WebsocketPath = this.Url.Action(nameof(LedgerConnection), new { walletId = walletId.ToString() })
            });
        }
      
        [HttpGet("{walletId}/psbt/seed")]
        public IActionResult SignWithSeed([ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId,string psbt)
        {
            return View(nameof(SignWithSeed), new SignWithSeedViewModel()
            {
                PSBT = psbt
            });
        }

        [HttpPost("{walletId}/psbt/seed")]
        public async Task<IActionResult> SignWithSeed([ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, SignWithSeedViewModel viewModel)
        {
            if (!ModelState.IsValid)
            {
                return View("SignWithSeed", viewModel);
            }
            var network = NetworkProvider.GetNetwork<BTCPayNetwork>(walletId.CryptoCode);
            if (network == null)
                throw new FormatException("Invalid value for crypto code");

            ExtKey extKey = viewModel.GetExtKey(network.NBitcoinNetwork);

            if (extKey == null)
            {
                ModelState.AddModelError(nameof(viewModel.SeedOrKey),
                    "Seed or Key was not in a valid format. It is either the 12/24 words or starts with xprv");
            }

            var psbt = PSBT.Parse(viewModel.PSBT, network.NBitcoinNetwork);

            if (!psbt.IsReadyToSign())
            {
                ModelState.AddModelError(nameof(viewModel.PSBT), "PSBT is not ready to be signed");
            }

            if (!ModelState.IsValid)
            {
                return View("SignWithSeed", viewModel);
            }

            ExtKey signingKey = null;
            var settings = GetDerivationSchemeSettings(walletId);
            var signingKeySettings = settings.GetSigningAccountKeySettings();
            if (signingKeySettings.RootFingerprint is null)
                signingKeySettings.RootFingerprint = extKey.GetPublicKey().GetHDFingerPrint();

            RootedKeyPath rootedKeyPath = signingKeySettings.GetRootedKeyPath();
            if (rootedKeyPath == null)
            {
                ModelState.AddModelError(nameof(viewModel.SeedOrKey), "The master fingerprint and/or account key path of your seed are not set in the wallet settings.");
                return View("SignWithSeed", viewModel);
            }
            // The user gave the root key, let's try to rebase the PSBT, and derive the account private key
            if (rootedKeyPath.MasterFingerprint == extKey.GetPublicKey().GetHDFingerPrint())
            {
                psbt.RebaseKeyPaths(signingKeySettings.AccountKey, rootedKeyPath);
                signingKey = extKey.Derive(rootedKeyPath.KeyPath);
            }
            else
            {
                ModelState.AddModelError(nameof(viewModel.SeedOrKey), "The master fingerprint does not match the one set in your wallet settings. Probable cause are: wrong seed, wrong passphrase or wrong fingerprint in your wallet settings.");
                return View(viewModel);
            }

            var changed = PSBTChanged(psbt, () => psbt.SignAll(settings.AccountDerivation, signingKey, rootedKeyPath));
            if (!changed)
            {
                ModelState.AddModelError(nameof(viewModel.SeedOrKey), "Impossible to sign the transaction. Probable cause: Incorrect account key path in wallet settings, PSBT already signed.");
                return View(viewModel);
            }
            ModelState.Remove(nameof(viewModel.PSBT));
            return await WalletPSBTReady(walletId, psbt.ToBase64(), signingKey.GetWif(network.NBitcoinNetwork).ToString(), rootedKeyPath?.ToString());
        }

        private bool PSBTChanged(PSBT psbt, Action act)
        {
            var before = psbt.ToBase64();
            act();
            var after = psbt.ToBase64();
            return before != after;
        }

        private string ValueToString(Money v, BTCPayNetworkBase network)
        {
            return v.ToString() + " " + network.CryptoCode;
        }

        private IActionResult RedirectToWalletTransaction(WalletId walletId, Transaction transaction)
        {
            var network = NetworkProvider.GetNetwork<BTCPayNetwork>(walletId.CryptoCode);
            if (transaction != null)
            {
                var wallet = _walletProvider.GetWallet(network);
                var derivationSettings = GetDerivationSchemeSettings(walletId);
                wallet.InvalidateCache(derivationSettings.AccountDerivation);
                TempData[WellKnownTempData.SuccessMessage] = $"Transaction broadcasted successfully ({transaction.GetHash().ToString()})";
            }
            return RedirectToAction(nameof(WalletTransactions), new { walletId = walletId.ToString() });
        }

        [HttpGet]
        [Route("{walletId}/rescan")]
        public async Task<IActionResult> WalletRescan(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId)
        {
            if (walletId?.StoreId == null)
                return NotFound();
            DerivationSchemeSettings paymentMethod = GetDerivationSchemeSettings(walletId);
            if (paymentMethod == null)
                return NotFound();

            var vm = new RescanWalletModel();
            vm.IsFullySync = _dashboard.IsFullySynched(walletId.CryptoCode, out var unused);
            vm.IsServerAdmin = (await _authorizationService.AuthorizeAsync(User, Policies.CanModifyServerSettings.Key)).Succeeded;
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
        [Authorize(Policy = Policies.CanModifyServerSettings.Key, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public async Task<IActionResult> WalletRescan(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, RescanWalletModel vm)
        {
            if (walletId?.StoreId == null)
                return NotFound();
            DerivationSchemeSettings paymentMethod = GetDerivationSchemeSettings(walletId);
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

        public StoreData CurrentStore
        {
            get
            {
                return HttpContext.GetStoreData();
            }
        }

        private DerivationSchemeSettings GetDerivationSchemeSettings(WalletId walletId)
        {
            var paymentMethod = CurrentStore
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
                    return (await wallet.GetBalance(derivationStrategy, cts.Token)).ToString(CultureInfo.InvariantCulture);
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
            string hintChange = null
            )
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
                return NotFound();
            var storeData = CurrentStore;
            var network = NetworkProvider.GetNetwork<BTCPayNetwork>(walletId.CryptoCode);
            if (network == null)
                throw new FormatException("Invalid value for crypto code");
            PSBT psbt = GetAmbientPSBT(network.NBitcoinNetwork, true);
            var derivationSettings = GetDerivationSchemeSettings(walletId);

            var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();

            using (var normalOperationTimeout = new CancellationTokenSource())
            using (var signTimeout = new CancellationTokenSource())
            {
                normalOperationTimeout.CancelAfter(TimeSpan.FromMinutes(30));
                var hw = new LedgerHardwareWalletService(webSocket);
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

                        derivationSettings.RebaseKeyPaths(psbt);
                        var changeAddress = string.IsNullOrEmpty(hintChange) ? null : BitcoinAddress.Create(hintChange, network.NBitcoinNetwork);
                        signTimeout.CancelAfter(TimeSpan.FromMinutes(5));
                        psbt = await hw.SignTransactionAsync(psbt, accountKey.GetRootedKeyPath(), accountKey.AccountKey, changeAddress?.ScriptPubKey, signTimeout.Token);
                        SetAmbientPSBT(null);
                        result = new SendToAddressResult() { PSBT = psbt.ToBase64() };
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
                        var bytes = UTF8NOBOM.GetBytes(JsonConvert.SerializeObject(result, _serializerSettings));
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
            var derivationSchemeSettings = GetDerivationSchemeSettings(walletId);
            if (derivationSchemeSettings == null || derivationSchemeSettings.Network.ReadonlyWallet)
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
            WalletId walletId, WalletSettingsViewModel vm, string command = "save", CancellationToken cancellationToken = default)
        {
            if (!ModelState.IsValid)
                return View(vm);
            var derivationScheme = GetDerivationSchemeSettings(walletId);
            if (derivationScheme == null || derivationScheme.Network.ReadonlyWallet)
                return NotFound();

            if (command == "save")
            {
                derivationScheme.Label = vm.Label;
                derivationScheme.SigningKey = string.IsNullOrEmpty(vm.SelectedSigningKey) ? null : new BitcoinExtPubKey(vm.SelectedSigningKey, derivationScheme.Network.NBitcoinNetwork);
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
                TempData[WellKnownTempData.SuccessMessage] = "Wallet settings updated";
                return RedirectToAction(nameof(WalletSettings));
            }
            else if (command == "prune")
            {
                var result = await ExplorerClientProvider.GetExplorerClient(walletId.CryptoCode).PruneAsync(derivationScheme.AccountDerivation, new PruneRequest(),  cancellationToken);
                if (result.TotalPruned == 0)
                {
                    TempData[WellKnownTempData.SuccessMessage] = $"The wallet is already pruned";
                }
                else
                {
                    TempData[WellKnownTempData.SuccessMessage] = $"The wallet has been successfully pruned ({result.TotalPruned} transactions have been removed from the history)";
                }
                return RedirectToAction(nameof(WalletSettings));
            }
            else
            {
                return NotFound();
            }
        }
        


        private string GetImage(PaymentMethodId paymentMethodId, BTCPayNetwork network)
        {
            var res = paymentMethodId.PaymentType == PaymentTypes.BTCLike
                ? Url.Content(network.CryptoImagePath)
                : Url.Content(network.LightningImagePath);
            return "/" + res;
        }
    }

    public class WalletReceiveViewModel
    {
        public string CryptoImage { get; set; }
        public string CryptoCode { get; set; }
        public string Address { get; set; }
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
