#if ALTCOINS
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Amazon.Runtime.Internal.Transform;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Payments;
using BTCPayServer.Security;
using BTCPayServer.Services.Altcoins.Ethereum.Filters;
using BTCPayServer.Services.Altcoins.Ethereum.Payments;
using BTCPayServer.Services.Altcoins.Ethereum.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Internal;
using NBitcoin;
using Nethereum.HdWallet;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.StandardTokenEIP20.ContractDefinition;
using Nethereum.Util;
using Nethereum.Web3;

namespace BTCPayServer.Services.Altcoins.Ethereum.UI
{
    [Route("stores/{storeId}/ethlike")]
    [OnlyIfSupportEth]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class EthereumLikeStoreController : Controller
    {
        private readonly StoreRepository _storeRepository;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly InvoiceRepository _invoiceRepository;
        private readonly EthereumService _ethereumService;
        private readonly IAuthorizationService _authorizationService;

        public EthereumLikeStoreController(StoreRepository storeRepository,
            BTCPayNetworkProvider btcPayNetworkProvider, InvoiceRepository invoiceRepository,
            EthereumService ethereumService, IAuthorizationService authorizationService)
        {
            _storeRepository = storeRepository;
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _invoiceRepository = invoiceRepository;
            _ethereumService = ethereumService;
            _authorizationService = authorizationService;
        }

        private StoreData StoreData => HttpContext.GetStoreData();

        [HttpGet()]
        public IActionResult GetStoreEthereumLikePaymentMethods()
        {
            var eth = StoreData.GetSupportedPaymentMethods(_btcPayNetworkProvider)
                .OfType<EthereumSupportedPaymentMethod>();

            var excludeFilters = StoreData.GetStoreBlob().GetExcludedPaymentMethods();
            var ethNetworks = _btcPayNetworkProvider.GetAll().OfType<EthereumBTCPayNetwork>()
                .GroupBy(network => network.ChainId);

            var vm = new ViewEthereumStoreOptionsViewModel();

            foreach (var chainId in ethNetworks)
            {
                var items = new List<ViewEthereumStoreOptionItemViewModel>();
                foreach (var network in chainId)
                {
                    var paymentMethodId = new PaymentMethodId(network.CryptoCode, EthereumPaymentType.Instance);
                    var matchedPaymentMethod = eth.SingleOrDefault(method =>
                        method.PaymentId == paymentMethodId);
                    items.Add(new ViewEthereumStoreOptionItemViewModel()
                    {
                        CryptoCode = network.CryptoCode,
                        Enabled = matchedPaymentMethod != null && !excludeFilters.Match(paymentMethodId),
                        IsToken = network is ERC20BTCPayNetwork,
                        RootAddress = matchedPaymentMethod?.GetWalletDerivator()?.Invoke(0) ?? "not configured"
                    });
                }

                vm.Items.Add(chainId.Key, items);
            }

            return View(vm);
        }

        [HttpGet("{cryptoCode}")]
        public IActionResult GetStoreEthereumLikePaymentMethod(string cryptoCode)
        {
            var eth = StoreData.GetSupportedPaymentMethods(_btcPayNetworkProvider)
                .OfType<EthereumSupportedPaymentMethod>();

            var network = _btcPayNetworkProvider.GetNetwork<EthereumBTCPayNetwork>(cryptoCode);
            if (network is null)
            {
                return NotFound();
            }

            var excludeFilters = StoreData.GetStoreBlob().GetExcludedPaymentMethods();
            var paymentMethodId = new PaymentMethodId(network.CryptoCode, EthereumPaymentType.Instance);
            var matchedPaymentMethod = eth.SingleOrDefault(method =>
                method.PaymentId == paymentMethodId);

            return View(new EditEthereumPaymentMethodViewModel()
            {
                Enabled = !excludeFilters.Match(paymentMethodId),
                XPub = matchedPaymentMethod?.XPub,
                Index = matchedPaymentMethod?.CurrentIndex ?? 0,
                Passphrase = matchedPaymentMethod?.Password,
                Seed = matchedPaymentMethod?.Seed,
                StoreSeed = !string.IsNullOrEmpty(matchedPaymentMethod?.Seed),
                OriginalIndex = matchedPaymentMethod?.CurrentIndex ?? 0,
                KeyPath = string.IsNullOrEmpty(matchedPaymentMethod?.KeyPath)
                    ? network.GetDefaultKeyPath()
                    : matchedPaymentMethod?.KeyPath
            });
        }

        [HttpPost("{cryptoCode}")]
        public async Task<IActionResult> GetStoreEthereumLikePaymentMethod(string cryptoCode,
            EditEthereumPaymentMethodViewModel viewModel)
        {
            var network = _btcPayNetworkProvider.GetNetwork<EthereumBTCPayNetwork>(cryptoCode);
            if (network is null)
            {
                return NotFound();
            }

            var store = StoreData;
            var blob = StoreData.GetStoreBlob();
            var paymentMethodId = new PaymentMethodId(network.CryptoCode, EthereumPaymentType.Instance);

            var currentPaymentMethod = StoreData.GetSupportedPaymentMethods(_btcPayNetworkProvider)
                .OfType<EthereumSupportedPaymentMethod>().SingleOrDefault(method =>
                    method.PaymentId == paymentMethodId);

            if (currentPaymentMethod != null && currentPaymentMethod.CurrentIndex != viewModel.Index &&
                viewModel.OriginalIndex == viewModel.Index)
            {
                viewModel.Index = currentPaymentMethod.CurrentIndex;
                viewModel.OriginalIndex = currentPaymentMethod.CurrentIndex;
            }
            else if (currentPaymentMethod != null && currentPaymentMethod.CurrentIndex != viewModel.Index &&
                     viewModel.OriginalIndex != currentPaymentMethod.CurrentIndex)
            {
                ModelState.AddModelError(nameof(viewModel.Index),
                    $"You tried to update the index (to {viewModel.Index}) but new derivations in the background updated the index (to {currentPaymentMethod.CurrentIndex}) ");
                viewModel.Index = currentPaymentMethod.CurrentIndex;
                viewModel.OriginalIndex = currentPaymentMethod.CurrentIndex;
            }

            Wallet wallet = null;
            try
            {
                if (!string.IsNullOrEmpty(viewModel.Seed))
                {
                    wallet = new Wallet(viewModel.Seed, viewModel.Passphrase,
                        string.IsNullOrEmpty(viewModel.KeyPath) ? network.GetDefaultKeyPath() : viewModel.KeyPath);
                }
            }
            catch (Exception)
            {
                ModelState.AddModelError(nameof(viewModel.Seed), $"seed was incorrect");
            }

            if (wallet != null)
            {
                try
                {
                    wallet.GetAccount(0);
                }
                catch (Exception)
                {
                    ModelState.AddModelError(nameof(viewModel.KeyPath), $"keypath was incorrect");
                }
            }

            PublicWallet publicWallet = null;
            try
            {
                if (!string.IsNullOrEmpty(viewModel.XPub))
                {
                    try
                    {
                        publicWallet = new PublicWallet(viewModel.XPub);
                    }
                    catch (Exception)
                    {
                        publicWallet = new PublicWallet(new BitcoinExtPubKey(viewModel.XPub, Network.Main).ExtPubKey);
                    }

                    if (wallet != null && !publicWallet.ExtPubKey.Equals(wallet.GetMasterPublicWallet().ExtPubKey))
                    {
                        ModelState.AddModelError(nameof(viewModel.XPub),
                            $"The xpub does not match the seed/pass/key path provided");
                    }
                }
            }
            catch (Exception)
            {
                ModelState.AddModelError(nameof(viewModel.XPub), $"xpub was incorrect");
            }

            if (!string.IsNullOrEmpty(viewModel.AddressCheck))
            {
                int index = -1;
                if (wallet != null)
                {
                    index = Array.IndexOf(wallet.GetAddresses(1000), viewModel.AddressCheck);
                }
                else if (publicWallet != null)
                {
                    index = Array.IndexOf(publicWallet.GetAddresses(1000), viewModel.AddressCheck);
                }

                if (viewModel.AddressCheckLastUsed && index > -1)
                {
                    viewModel.Index = index;
                }

                if (index == -1)
                {
                    ModelState.AddModelError(nameof(viewModel.AddressCheck),
                        "Could not confirm address belongs to configured wallet");
                }
            }

            if (!ModelState.IsValid)
            {
                return View(viewModel);
            }

            currentPaymentMethod ??= new EthereumSupportedPaymentMethod();
            currentPaymentMethod.Password = viewModel.StoreSeed ? viewModel.Passphrase : "";
            currentPaymentMethod.Seed = viewModel.StoreSeed ? viewModel.Seed : "";
            currentPaymentMethod.XPub = string.IsNullOrEmpty(viewModel.XPub) && wallet != null
                ? wallet.GetMasterPublicWallet().ExtPubKey.ToBytes().ToHex()
                : viewModel.XPub;
            currentPaymentMethod.CryptoCode = cryptoCode;
            currentPaymentMethod.KeyPath = string.IsNullOrEmpty(viewModel.KeyPath)
                ? network.GetDefaultKeyPath()
                : viewModel.KeyPath;
            currentPaymentMethod.CurrentIndex = viewModel.Index;

            blob.SetExcluded(paymentMethodId, !viewModel.Enabled);
            store.SetSupportedPaymentMethod(currentPaymentMethod);
            store.SetStoreBlob(blob);
            await _storeRepository.UpdateStore(store);

            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = $"updated {cryptoCode}", Severity = StatusMessageModel.StatusSeverity.Success
            });

            return RedirectToAction("GetStoreEthereumLikePaymentMethods", new {storeId = store.Id});
        }

        [HttpGet("sweep/select-chain")]
        public async Task<IActionResult> SweepFundsSelectChain()
        {
            var configuredEthereumLikePaymentMethods = StoreData
                .GetSupportedPaymentMethods(_btcPayNetworkProvider)
                .OfType<EthereumSupportedPaymentMethod>()
                .Where(method => !string.IsNullOrEmpty(method.XPub));


            if (!configuredEthereumLikePaymentMethods.Any())
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Message = $"Nothing configured to sweep", Severity = StatusMessageModel.StatusSeverity.Error
                });
                return RedirectToAction("GetStoreEthereumLikePaymentMethods", new {storeId = StoreData.Id});
            }

            var groupByChain = configuredEthereumLikePaymentMethods
                .Select(method => (
                    Network: _btcPayNetworkProvider.GetNetwork<EthereumBTCPayNetwork>(method.CryptoCode),
                    method)).GroupBy(tuple => tuple.Network.ChainId);
            if (groupByChain.Count() > 1)
            {
                return View("SweepFundsSpecifyInfo", new SweepFundsViewModel() {Chains = groupByChain});
            }

            return RedirectToAction("SweepFundsSelectXPub",
                new {storeId = StoreData.Id, chainId = groupByChain.First().Key});
        }

        [HttpGet("sweep/{chainId}")]
        public async Task<IActionResult> SweepFundsSelectXPub(int chainId)
        {
            var configuredEthereumLikePaymentMethods = StoreData
                .GetSupportedPaymentMethods(_btcPayNetworkProvider)
                .OfType<EthereumSupportedPaymentMethod>()
                .Select(method => (Network: _btcPayNetworkProvider.GetNetwork<EthereumBTCPayNetwork>(method.CryptoCode),
                    method))
                .Where(method => method.Network.ChainId == chainId && !string.IsNullOrEmpty(method.method.XPub));


            if (!configuredEthereumLikePaymentMethods.Any())
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Message = $"Nothing configured to sweep", Severity = StatusMessageModel.StatusSeverity.Error
                });
                return RedirectToAction("GetStoreEthereumLikePaymentMethods", new {storeId = StoreData.Id});
            }

            var groupedxpub =
                configuredEthereumLikePaymentMethods.GroupBy(tuple => tuple.method.XPub);


            if (groupedxpub.Count() > 1)
            {
                return View("SweepFundsSpecifyInfo", new SweepFundsViewModel() {Wallets = groupedxpub});
            }

            return RedirectToAction("SweepFundsSpecifyInfo",
                new {storeId = StoreData.Id, chainId, xpub = groupedxpub.First().Key});
        }

        [HttpGet("sweep/{chainId}/{xpub}")]
        public async Task<IActionResult> SweepFundsSpecifyInfo(int chainId, string xpub)
        {
            var configuredEthereumLikePaymentMethods = StoreData
                .GetSupportedPaymentMethods(_btcPayNetworkProvider)
                .OfType<EthereumSupportedPaymentMethod>()
                .Select(method => (Network: _btcPayNetworkProvider.GetNetwork<EthereumBTCPayNetwork>(method.CryptoCode),
                    method))
                .Where(method => method.Network.ChainId == chainId && method.method.XPub == xpub);


            if (!configuredEthereumLikePaymentMethods.Any())
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Message = $"Nothing configured to sweep", Severity = StatusMessageModel.StatusSeverity.Error
                });
                return RedirectToAction("GetStoreEthereumLikePaymentMethods", new {storeId = StoreData.Id});
            }

            var seedConfiguredMethod =
                configuredEthereumLikePaymentMethods.FirstOrDefault(tuple => !string.IsNullOrEmpty(tuple.method.Seed));

            var vm = new SweepFundsViewModel();
            vm.ChainId = chainId;
            vm.XPub = xpub;
            if (seedConfiguredMethod.method != null)
            {
                vm.Seed = seedConfiguredMethod.method.Seed;
                vm.Password = seedConfiguredMethod.method.Password;
                vm.KeyPath = seedConfiguredMethod.method.KeyPath;
            }

            vm.KeyPath ??= configuredEthereumLikePaymentMethods.First().method.KeyPath;


            return View("SweepFundsSpecifyInfo", vm);
        }

        [AllowAnonymous]
        [HttpGet("~/ethlike/sweep/unmark/{address}/{cryptoCode}")]
        public async Task<IActionResult> RemoveSweepMarker(string address, string cryptoCode)
        {
            var invoice = (await _invoiceRepository.GetInvoicesFromAddresses(new[]
                {
                    $"{address}#{new PaymentMethodId(cryptoCode, EthereumPaymentType.Instance)}"
                }))
                .FirstOrDefault();
            if (!(await _authorizationService.AuthorizeAsync(User, invoice?.StoreId, new PolicyRequirement(Policies.CanModifyStoreSettings))).Succeeded)
            {
                return NotFound();
            }

            var p = invoice.GetPayments(cryptoCode).Last(entity => entity.Accounted);
            var pd = p.GetCryptoPaymentData() as EthereumLikePaymentData;
            pd.SweepingTransaction = "";
            p.SetCryptoPaymentData(pd);
            await _invoiceRepository.UpdatePayments(new List<PaymentEntity>() {p});

            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = $"Unmarked sweep for {invoice.Id} {cryptoCode}",
                Severity = StatusMessageModel.StatusSeverity.Info
            });

            return RedirectToAction("GetStoreEthereumLikePaymentMethods", new {storeId = StoreData.Id});
        }


        [HttpPost("sweep/{chainId}/{xpub}")]
        public async Task<IActionResult> SweepFundsSpecifyInfo(SweepFundsViewModel viewModel, string command = "")
        {
            if (!ModelState.IsValid)
            {
                return View(viewModel);
            }

            var wallet = new Wallet(viewModel.Seed, viewModel.Password, viewModel.KeyPath);
            if (viewModel.XPub != wallet.GetMasterPublicWallet().ExtPubKey.ToBytes().ToHex())
            {
                ModelState.AddModelError(nameof(viewModel.Seed), "Seed/Password do not match configured wallet");
            }

            if (!ModelState.IsValid)
            {
                return View(viewModel);
            }

            if (viewModel.SweepRequests == null)
                return await ComputeSweepRequests(viewModel);

            return await CheckIfCanSweepNow(viewModel, command);
        }

        private async Task<IActionResult> ComputeSweepRequests(SweepFundsViewModel viewModel)
        {
            var invoices = await _invoiceRepository.GetInvoices(new InvoiceQuery()
            {
                StoreId = new[] {StoreData.Id},
                Status = new[] {InvoiceState.ToString(InvoiceStatusLegacy.Complete)},
            });

            var invoicesWithPayment = invoices.Select(entity => (
                    entity,
                    entity.GetSupportedPaymentMethod<EthereumSupportedPaymentMethod>(),
                    entity.GetPayments()
                        .Where(paymentEntity => paymentEntity.Accounted &&
                                                paymentEntity.Network is EthereumBTCPayNetwork ethN &&
                                                ethN.ChainId == viewModel.ChainId)
                        .Select(paymentEntity => (paymentEntity,
                            paymentEntity.GetCryptoPaymentData() as EthereumLikePaymentData))
                        .Where(tuple =>
                            tuple.Item2 != null && string.IsNullOrEmpty(tuple.Item2.SweepingTransaction) &&
                            tuple.Item2.XPub == viewModel.XPub)))
                .Where(tuple => tuple.Item3.Any());
            if (!invoicesWithPayment.Any())
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Message = $"Nothing found to sweep", Severity = StatusMessageModel.StatusSeverity.Error
                });

                return RedirectToAction("GetStoreEthereumLikePaymentMethods", new {storeId = StoreData.Id});
            }

            //need construct list address with ERC tokens and list address with ETH token
            var payments = invoicesWithPayment.SelectMany(tuple => tuple.Item3.Select(valueTuple =>
                (InvoiceId: tuple.entity.Id, PaymentEntity: valueTuple.paymentEntity, PaymentData: valueTuple.Item2)));

            var groupedByAddress = payments.GroupBy(tuple => tuple.PaymentData.Address);

            var networks = _btcPayNetworkProvider.GetAll().OfType<EthereumBTCPayNetwork>()
                .Where(network => network.ChainId == viewModel.ChainId);
            var mainNetwork = networks.SingleOrDefault(network => !(network is ERC20BTCPayNetwork));
            var pmi = new PaymentMethodId(mainNetwork.CryptoCode, EthereumPaymentType.Instance);

            //need send ETH to addresses even erc, make sure they not monitored in invoices 
            var ethInvoices =
                await _invoiceRepository.GetInvoicesFromAddresses(groupedByAddress
                    .Select(tuples => $"{tuples.Key}#{pmi}").ToArray());
            var pendingInvoices = await _invoiceRepository.GetPendingInvoices();

            var pendingEthInvoices = ethInvoices.Where(entity => (pendingInvoices).Contains(entity.Id))
                .Select(entity => (entity, entity.GetPaymentMethod(pmi))).Select(tuple => (tuple.entity, tuple.Item2,
                    tuple.Item2.GetPaymentMethodDetails() as EthereumLikeOnChainPaymentMethodDetails))
                .ToDictionary(tuple => tuple.Item3.DepositAddress);

            var requests = new List<SweepRequest>();
            foreach (IGrouping<string, (string InvoiceId, PaymentEntity PaymentEntity, EthereumLikePaymentData
                PaymentData)> grouping in groupedByAddress)
            {
                var request = new SweepRequest();
                request.Address = grouping.Key;
                request.Index = grouping.First().PaymentData.AccountIndex;
                if (pendingEthInvoices.TryGetValue(grouping.Key, out var conflict))
                {
                    request.UnableToSweepBecauseOfActiveInvoiceWithNativeCurrency = conflict.entity.Id;
                }

                foreach ((string InvoiceId, PaymentEntity PaymentEntity, EthereumLikePaymentData PaymentData) valueTuple
                    in grouping)
                {
                    var network = valueTuple.PaymentEntity.Network as EthereumBTCPayNetwork;

                    var sweepRequestItem = new SweepRequestItem();
                    sweepRequestItem.CryptoCode = network.CryptoCode;
                    sweepRequestItem.InvoiceId = valueTuple.InvoiceId;
                    sweepRequestItem.Sweep =
                        string.IsNullOrEmpty(request.UnableToSweepBecauseOfActiveInvoiceWithNativeCurrency);
                    if (network is ERC20BTCPayNetwork erc20BTCPayNetwork)
                    {
                        request.Tokens.Add(sweepRequestItem);
                    }
                    else
                    {
                        request.Native = sweepRequestItem;
                    }
                }

                requests.Add(request);
            }

            viewModel.SweepRequests = requests;

            return await CheckIfCanSweepNow(viewModel, "", false);
        }

        public async Task<IActionResult> CheckIfCanSweepNow(SweepFundsViewModel viewModel, string command,
            bool leaveActiveInvoice = false)
        {
            var web3 = _ethereumService.GetWeb3(viewModel.ChainId);
            if (web3 == null)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Message = $"Web3 not available", Severity = StatusMessageModel.StatusSeverity.Error
                });

                return RedirectToAction("GetStoreEthereumLikePaymentMethods", new {storeId = StoreData.Id});
            }

            viewModel.GasPrice ??= (ulong)(await web3.Eth.GasPrice.SendRequestAsync()).Value;
            viewModel.SweepRequests = viewModel.SweepRequests
                .Where(request =>
                    leaveActiveInvoice ||
                    string.IsNullOrEmpty(request.UnableToSweepBecauseOfActiveInvoiceWithNativeCurrency))
                .OrderByDescending(request => request.Sufficient(viewModel.GasPrice.Value, out var excess)
                ).ToList();

            var networks = _btcPayNetworkProvider.GetAll().OfType<EthereumBTCPayNetwork>()
                .Where(network => network.ChainId == viewModel.ChainId);
            var mainNetwork = networks.SingleOrDefault(network => !(network is ERC20BTCPayNetwork));
            var etherTransferService = web3.Eth.GetEtherTransferService();
            var transferHandler = web3.Eth.GetContractTransactionHandler<TransferFunction>();
            foreach (var sweepRequest in viewModel.SweepRequests)
            {
                sweepRequest.Native ??= new SweepRequestItem() {Sweep = true, CryptoCode = mainNetwork.CryptoCode,};

                var amt = (await _ethereumService.GetBalance(mainNetwork, sweepRequest.Address)).Value;
                sweepRequest.Native.Amount = amt;
                sweepRequest.Native.GasCost = amt == 0
                    ? 0
                    : (ulong)await etherTransferService.EstimateGasAsync(viewModel.DestinationAddress,
                        EthereumLikePaymentData.GetValue(mainNetwork, amt));

                foreach (SweepRequestItem sweepRequestItem in sweepRequest.Tokens)
                {
                    var network = _btcPayNetworkProvider.GetNetwork<ERC20BTCPayNetwork>(sweepRequestItem.CryptoCode);

                    sweepRequestItem.Amount = (await _ethereumService.GetBalance(network, sweepRequest.Address)).Value;
                    if (sweepRequestItem.Amount == 0 && string.IsNullOrEmpty(sweepRequestItem.TransactionHash))
                    {
                        sweepRequestItem.TransactionHash = "external sweep detected";
                        var i = await _invoiceRepository.GetInvoice(sweepRequestItem.InvoiceId);
                        var pD = i.GetPayments(network.CryptoCode).Last(entity => entity.Accounted);
                        var ethpd = pD.GetCryptoPaymentData() as EthereumLikePaymentData;

                        ethpd.SweepingTransaction = sweepRequestItem.TransactionHash;
                        pD.SetCryptoPaymentData(ethpd);
                        await _invoiceRepository.UpdatePayments(new List<PaymentEntity>() {pD});
                    }

                    var transfer = new TransferFunction()
                    {
                        To = viewModel.DestinationAddress,
                        Value = sweepRequestItem.Amount,
                        FromAddress = sweepRequest.Address
                    };
                    sweepRequestItem.GasCost =
                        (ulong)(await transferHandler.EstimateGasAsync(network.SmartContractAddress,
                            transfer))
                        .Value;
                }

                sweepRequest.Tokens = sweepRequest.Tokens.Where(item => item.Amount > 0).ToList();
            }

            if (command == "sweep")
            {
                if (await DoSweepAction(viewModel, web3))
                {
                    return await CheckIfCanSweepNow(viewModel, null);
                }
            }

            return View("SweepFundsSpecifyInfo", viewModel);
        }

        public async Task<bool> DoSweepAction(SweepFundsViewModel viewModel, Web3 web3)
        {
            var somethingHappened = false;
            var grouped = viewModel.SweepRequests
                .Select(request =>
                {
                    var suff = request.Sufficient(viewModel.GasPrice.Value, out var diff);
                    return (request, suff, diff);
                }).GroupBy(tuple => tuple.suff);

            var sufficient = grouped.First(tuples => tuples.Key);
            //do not sweep request for only native when not enough 
            var insufficient = grouped.FirstOrDefault(tuples => !tuples.Key)?
                .Where(tuple => tuple.request.Tokens.Any(item => item.Amount > 0 && item.Sweep));


            var ethForwardedTo = new Dictionary<string, ulong>();

            foreach (var tuple in sufficient)
            {
                var w = new Wallet(viewModel.Seed, viewModel.Password, viewModel.KeyPath);
                var acc = w.GetAccount((int)tuple.request.Index);
                var accWeb3 = new Web3(acc, web3.Client);

                var transferHandler = accWeb3.Eth.GetContractTransactionHandler<TransferFunction>();
                foreach (var tokenSweepRequest in tuple.request.Tokens.Where(item =>
                    item.Sweep && string.IsNullOrEmpty(item.TransactionHash)))
                {
                    var network = _btcPayNetworkProvider.GetNetwork<ERC20BTCPayNetwork>(tokenSweepRequest.CryptoCode);
                    var receipt = await transferHandler.SendRequestAndWaitForReceiptAsync(network.SmartContractAddress,
                        new TransferFunction()
                        {
                            Value = tokenSweepRequest.Amount,
                            To = viewModel.DestinationAddress,
                            GasPrice = viewModel.GasPrice.Value,
                            Gas = tokenSweepRequest.GasCost,
                            FromAddress = acc.Address,
                        });

                    tokenSweepRequest.TransactionHash = receipt.TransactionHash;
                    if (tokenSweepRequest.TransactionHash != null)
                    {
                        var i = await _invoiceRepository.GetInvoice(tokenSweepRequest.InvoiceId);
                        var pD = i.GetPayments(network.CryptoCode).Last(entity => entity.Accounted);
                        var ethpd = pD.GetCryptoPaymentData() as EthereumLikePaymentData;

                        ethpd.SweepingTransaction = tokenSweepRequest.TransactionHash;
                        pD.SetCryptoPaymentData(ethpd);
                        await _invoiceRepository.UpdatePayments(new List<PaymentEntity>() {pD});
                        somethingHappened = true;
                    }
                }

                if (tuple.diff > 0 && tuple.request.Native.Sweep)
                {
                    var anyInsufficientThatCanWorkWithExcessFromThisAccount =
                        insufficient?.Where(valueTuple =>
                        {
                            ulong requiredAmount = valueTuple.diff;

                            if (ethForwardedTo.ContainsKey(valueTuple.request.Address))
                            {
                                var contributedAmount = ethForwardedTo[valueTuple.request.Address];
                                if (requiredAmount > contributedAmount)
                                {
                                    //still need more
                                    return true;
                                }
                                else
                                {
                                    return false;
                                }
                            }

                            if (requiredAmount < tuple.diff)
                            {
                                //this takes care of it enough
                                return true;
                            }

                            return false;
                        });

                    var etherTransferService = accWeb3.Eth.GetEtherTransferService();
                    TransactionReceipt tx = null;
                    if (anyInsufficientThatCanWorkWithExcessFromThisAccount?.Any() is true)
                    {
                        var destination = anyInsufficientThatCanWorkWithExcessFromThisAccount.First();
                        var network =
                            _btcPayNetworkProvider.GetNetwork<EthereumBTCPayNetwork>(tuple.request.Native.CryptoCode);
                        var netAmount = EthereumLikePaymentData.GetValue(network, tuple.diff);
                        tx = await etherTransferService.TransferEtherAndWaitForReceiptAsync(destination.request.Address,
                            netAmount,
                            Web3.Convert.FromWei((BigInteger)viewModel.GasPrice, UnitConversion.EthUnit.Gwei),
                            tuple.request.Native.GasCost);
                        if (ethForwardedTo.TryGetValue(destination.request.Address, out var existingAmount))
                        {
                            ethForwardedTo[destination.request.Address] = existingAmount + tuple.diff;
                        }
                        else
                        {
                            ethForwardedTo.Add(destination.request.Address, tuple.diff);
                        }
                    }
                    else
                    {
                        var destination = viewModel.DestinationAddress;
                        var network =
                            _btcPayNetworkProvider.GetNetwork<EthereumBTCPayNetwork>(tuple.request.Native.CryptoCode);
                        var netAmount = EthereumLikePaymentData.GetValue(network, tuple.diff);
                        tx = await etherTransferService.TransferEtherAndWaitForReceiptAsync(destination,
                            netAmount,
                            Web3.Convert.FromWei((BigInteger)viewModel.GasPrice, UnitConversion.EthUnit.Gwei),
                            tuple.request.Native.GasCost);
                    }

                    if (tx != null)
                    {
                        tuple.request.Native.TransactionHash = tx.TransactionHash;
                        somethingHappened = true;
                    }

                    if (tx?.TransactionHash != null && !string.IsNullOrEmpty(tuple.request.Native.InvoiceId))
                    {
                        var i = await _invoiceRepository.GetInvoice(tuple.request.Native.InvoiceId);
                        var pD = i.GetPayments(tuple.request.Native.CryptoCode).Last(entity => entity.Accounted);
                        var ethpd = pD.GetCryptoPaymentData() as EthereumLikePaymentData;

                        ethpd.SweepingTransaction = tuple.request.Native.TransactionHash;
                        pD.SetCryptoPaymentData(ethpd);
                        await _invoiceRepository.UpdatePayments(new List<PaymentEntity>() {pD});
                    }
                }
            }

            return somethingHappened;
        }
    }

    public class SweepRequestItem
    {
        public string CryptoCode { get; set; }
        public ulong Amount { get; set; }
        public ulong GasCost { get; set; }
        public bool Sweep { get; set; }
        public string InvoiceId { get; set; }
        public string TransactionHash { get; set; }
    }

    public class SweepRequest
    {
        public string UnableToSweepBecauseOfActiveInvoiceWithNativeCurrency { get; set; }
        public string Address { get; set; }
        public long Index { get; set; }

        public List<SweepRequestItem> Tokens { get; set; } = new List<SweepRequestItem>();
        public SweepRequestItem Native { get; set; }

        public bool Sufficient(ulong gasPrice, out ulong difference)
        {
            ulong gasCost = 0;
            foreach (var item in Tokens)
            {
                if (item.Sweep) gasCost += item.GasCost;
            }

            var nativeAmount = (Native?.Amount ?? 0);
            var sweepCost = gasCost * gasPrice;
            var result = nativeAmount >= sweepCost;
            difference = result ? nativeAmount - sweepCost : sweepCost - nativeAmount;

            if (result && nativeAmount > 0 && Native?.Sweep is true)
            {
                gasCost += Native.GasCost;
                sweepCost = gasCost * gasPrice;
                result = nativeAmount >= sweepCost;
                difference = result ? nativeAmount - sweepCost : sweepCost - nativeAmount;
            }

            return result;
        }
    }

    public class SweepFundsViewModel
    {
        public IEnumerable<IGrouping<int, (EthereumBTCPayNetwork Network, EthereumSupportedPaymentMethod method)>> Chains { get; set; }

        public IEnumerable<IGrouping<string, (EthereumBTCPayNetwork Network, EthereumSupportedPaymentMethod method)>> Wallets { get; set; }

        public List<SweepRequest> SweepRequests { get; set; }
        [Required] public string DestinationAddress { get; set; }

        [Required] public string Seed { get; set; }
        public string Password { get; set; }
        [Required] public string KeyPath { get; set; }
        public string XPub { get; set; }
        public int ChainId { get; set; }
        public ulong? GasPrice { get; set; }
    }

    public class EditEthereumPaymentMethodViewModel
    {
        public string XPub { get; set; }
        public string Seed { get; set; }
        public string Passphrase { get; set; }

        public string KeyPath { get; set; }
        public long OriginalIndex { get; set; }

        [Display(Name = "Current address index")]

        public long Index { get; set; }

        public bool Enabled { get; set; }

        [Display(Name = "Hot wallet")] public bool StoreSeed { get; set; }

        [Display(Name = "Address Check")] public string AddressCheck { get; set; }

        public bool AddressCheckLastUsed { get; set; }
    }

    public class ViewEthereumStoreOptionsViewModel
    {
        public Dictionary<int, List<ViewEthereumStoreOptionItemViewModel>> Items { get; set; } = new Dictionary<int, List<ViewEthereumStoreOptionItemViewModel>>();
    }

    public class ViewEthereumStoreOptionItemViewModel
    {
        public string CryptoCode { get; set; }
        public bool IsToken { get; set; }
        public bool Enabled { get; set; }
        public string RootAddress { get; set; }
    }
}
#endif
