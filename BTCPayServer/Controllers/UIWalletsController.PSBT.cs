using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.BIP78.Sender;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.ModelBinders;
using BTCPayServer.Models;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Payments.PayJoin.Sender;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.Payment;
using NBXplorer;
using NBXplorer.Models;

namespace BTCPayServer.Controllers
{
    public partial class UIWalletsController
    {
        [NonAction]
        public async Task<CreatePSBTResponse> CreatePSBT(BTCPayNetwork network, DerivationSchemeSettings derivationSettings, WalletSendModel sendModel, CancellationToken cancellationToken)
        {
            var nbx = ExplorerClientProvider.GetExplorerClient(network);
            CreatePSBTRequest psbtRequest = new CreatePSBTRequest();
            if (sendModel.InputSelection)
            {
                psbtRequest.IncludeOnlyOutpoints = sendModel.SelectedInputs?.Select(OutPoint.Parse)?.ToList() ?? new List<OutPoint>();
            }
            foreach (var transactionOutput in sendModel.Outputs)
            {
                var psbtDestination = new CreatePSBTDestination();
                psbtRequest.Destinations.Add(psbtDestination);
                psbtDestination.Destination = BitcoinAddress.Create(transactionOutput.DestinationAddress, network.NBitcoinNetwork);
                psbtDestination.Amount = Money.Coins(transactionOutput.Amount.Value);
                psbtDestination.SubstractFees = transactionOutput.SubtractFeesFromOutput;
            }
            psbtRequest.RBF = network.SupportRBF ? true : null;
            psbtRequest.AlwaysIncludeNonWitnessUTXO = sendModel.AlwaysIncludeNonWitnessUTXO;

            psbtRequest.FeePreference = new FeePreference();
            if (sendModel.FeeSatoshiPerByte is decimal v &&
                v > decimal.Zero)
            {
                psbtRequest.FeePreference.ExplicitFeeRate = new FeeRate(v);
            }
            if (sendModel.NoChange)
            {
                psbtRequest.ExplicitChangeAddress = psbtRequest.Destinations.First().Destination;
            }

            var psbt = (await nbx.CreatePSBTAsync(derivationSettings.AccountDerivation, psbtRequest, cancellationToken));
            if (psbt == null)
                throw new NotSupportedException("You need to update your version of NBXplorer");
            // Not supported by coldcard, remove when they do support it
            psbt.PSBT.GlobalXPubs.Clear();
            return psbt;
        }

        [HttpPost("{walletId}/cpfp")]
        public async Task<IActionResult> WalletCPFP([ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, string[] outpoints, string[] transactionHashes, string returnUrl)
        {
            outpoints ??= Array.Empty<string>();
            transactionHashes ??= Array.Empty<string>();
            var network = NetworkProvider.GetNetwork<BTCPayNetwork>(walletId.CryptoCode);
            var explorer = ExplorerClientProvider.GetExplorerClient(network);
            var fr = _feeRateProvider.CreateFeeProvider(network);

            var targetFeeRate = await fr.GetFeeRateAsync(1);
            // Since we don't know the actual fee rate paid by a tx from NBX
            // we just assume that it is 20 blocks
            var assumedFeeRate = await fr.GetFeeRateAsync(20);

            var derivationScheme = (this.GetCurrentStore().GetDerivationSchemeSettings(_handlers, network.CryptoCode))?.AccountDerivation;
            if (derivationScheme is null)
                return NotFound();

            var utxos = await explorer.GetUTXOsAsync(derivationScheme);
            var outpointsHashet = outpoints.ToHashSet();
            var transactionHashesSet = transactionHashes.ToHashSet();
            var bumpableUTXOs = utxos.GetUnspentUTXOs().Where(u => u.Confirmations == 0 &&
                                                                (outpointsHashet.Contains(u.Outpoint.ToString()) ||
                                                                 transactionHashesSet.Contains(u.Outpoint.Hash.ToString()))).ToArray();

            if (bumpableUTXOs.Length == 0)
            {
                TempData[WellKnownTempData.ErrorMessage] = "There isn't any UTXO available to bump fee";
                return LocalRedirect(returnUrl);
            }
            Money bumpFee = Money.Zero;
            foreach (var txid in bumpableUTXOs.Select(u => u.TransactionHash).ToHashSet())
            {
                var tx = await explorer.GetTransactionAsync(txid);
                var vsize = tx.Transaction.GetVirtualSize();
                var assumedFeePaid = assumedFeeRate.GetFee(vsize);
                var expectedFeePaid = targetFeeRate.GetFee(vsize);
                bumpFee += Money.Max(Money.Zero, expectedFeePaid - assumedFeePaid);
            }
            var returnAddress = (await explorer.GetUnusedAsync(derivationScheme, NBXplorer.DerivationStrategy.DerivationFeature.Deposit)).Address;
            TransactionBuilder builder = explorer.Network.NBitcoinNetwork.CreateTransactionBuilder();
            builder.AddCoins(bumpableUTXOs.Select(utxo => utxo.AsCoin(derivationScheme)));
            // The fee of the bumped transaction should pay for both, the fee
            // of the bump transaction and those that are being bumped
            builder.SendEstimatedFees(targetFeeRate);
            builder.SendFees(bumpFee);
            builder.SendAll(returnAddress);

            try
            {
                var psbt = builder.BuildPSBT(false);
                psbt = (await explorer.UpdatePSBTAsync(new UpdatePSBTRequest()
                {
                    PSBT = psbt,
                    DerivationScheme = derivationScheme
                })).PSBT;

                return View("PostRedirect", new PostRedirectViewModel
                {
                    AspController = "UIWallets",
                    AspAction = nameof(WalletSign),
                    RouteParameters = {
                        { "walletId", walletId.ToString() }
                    },
                    FormParameters =
                    {
                        { "walletId", walletId.ToString() },
                        { "psbt", psbt.ToHex() },
                        { "backUrl", returnUrl },
                        { "returnUrl", returnUrl }
                    }
                });
            }
            catch (Exception ex)
            {
                TempData[WellKnownTempData.ErrorMessage] = ex.Message;

                return LocalRedirect(returnUrl);
            }
        }

        [HttpPost("{walletId}/sign")]
        public async Task<IActionResult> WalletSign([ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, WalletPSBTViewModel vm, string command = null)
        {
            var network = NetworkProvider.GetNetwork<BTCPayNetwork>(walletId.CryptoCode);
            var psbt = await vm.GetPSBT(network.NBitcoinNetwork, ModelState);

            vm.BackUrl ??= HttpContext.Request.GetTypedHeaders().Referer?.AbsolutePath;

            if (psbt is null || vm.InvalidPSBT)
            {
                return View("WalletSigningOptions", new WalletSigningOptionsModel
                {
                    SigningContext = vm.SigningContext,
                    ReturnUrl = vm.ReturnUrl,
                    BackUrl = vm.BackUrl
                });
            }
            switch (command)
            {
                case "vault":
                    return ViewVault(walletId, vm);
                case "seed":
                    return SignWithSeed(walletId, vm.SigningContext, vm.ReturnUrl, vm.BackUrl);
                case "decode":
                    return await WalletPSBT(walletId, vm, "decode");
                default:
                    break;
            }

            if (await CanUseHotWallet())
            {
                var derivationScheme = GetDerivationSchemeSettings(walletId);
                if (derivationScheme.IsHotWallet)
                {
                    var extKey = await ExplorerClientProvider.GetExplorerClient(walletId.CryptoCode)
                        .GetMetadataAsync<string>(derivationScheme.AccountDerivation,
                            WellknownMetadataKeys.MasterHDKey);
                    if (extKey != null)
                    {
                        return await SignWithSeed(walletId, new SignWithSeedViewModel
                        {
                            SeedOrKey = extKey,
                            SigningContext = vm.SigningContext,
                            ReturnUrl = vm.ReturnUrl,
                            BackUrl = vm.BackUrl
                        });
                    }
                }
            }
            return View("WalletSigningOptions", new WalletSigningOptionsModel
            {
                SigningContext = vm.SigningContext,
                ReturnUrl = vm.ReturnUrl,
                BackUrl = vm.BackUrl
            });
        }

        [HttpGet("{walletId}/psbt")]
        public async Task<IActionResult> WalletPSBT([ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, string returnUrl)
        {
            var network = NetworkProvider.GetNetwork<BTCPayNetwork>(walletId.CryptoCode);
            var referer = HttpContext.Request.GetTypedHeaders().Referer?.AbsolutePath;
            var vm = new WalletPSBTViewModel
            {
                BackUrl = string.IsNullOrEmpty(returnUrl) ? null : referer,
                ReturnUrl = returnUrl ?? referer,
                CryptoCode = network.CryptoCode
            };

            var derivationSchemeSettings = GetDerivationSchemeSettings(walletId);
            if (derivationSchemeSettings == null)
                return NotFound();
            vm.NBXSeedAvailable = await CanUseHotWallet() && derivationSchemeSettings.IsHotWallet;
            return View(vm);
        }

        [HttpPost("{walletId}/psbt")]
        public async Task<IActionResult> WalletPSBT(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId,
            WalletPSBTViewModel vm, string command)
        {
            var network = NetworkProvider.GetNetwork<BTCPayNetwork>(walletId.CryptoCode);
            vm.CryptoCode = network.CryptoCode;

            var derivationSchemeSettings = GetDerivationSchemeSettings(walletId);
            if (derivationSchemeSettings == null)
                return NotFound();

            vm.NBXSeedAvailable = await CanUseHotWallet() && derivationSchemeSettings.IsHotWallet;
            vm.BackUrl ??= HttpContext.Request.GetTypedHeaders().Referer?.AbsolutePath;

            var psbt = await vm.GetPSBT(network.NBitcoinNetwork, ModelState);
            if (vm.InvalidPSBT)
            {
                return View(vm);
            }
            if (psbt is null)
            {
                return View("WalletPSBT", vm);
            }
            switch (command)
            {
                case "sign":
                    return await WalletSign(walletId, vm);
                case "decode":
                    ModelState.Remove(nameof(vm.PSBT));
                    ModelState.Remove(nameof(vm.FileName));
                    ModelState.Remove(nameof(vm.UploadedPSBTFile));
                    await FetchTransactionDetails(walletId, derivationSchemeSettings, vm, network);
                    return View("WalletPSBTDecoded", vm);

                case "save-psbt":
                    return FilePSBT(psbt, vm.FileName);

                case "update":
                    psbt = await ExplorerClientProvider.UpdatePSBT(derivationSchemeSettings, psbt);
                    if (psbt == null)
                    {
                        TempData[WellKnownTempData.ErrorMessage] = "You need to update your version of NBXplorer";
                        return View(vm);
                    }
                    TempData[WellKnownTempData.SuccessMessage] = "PSBT updated!";
                    return RedirectToWalletPSBT(new WalletPSBTViewModel
                    {
                        PSBT = psbt.ToBase64(),
                        FileName = vm.FileName,
                        ReturnUrl = vm.ReturnUrl,
                        BackUrl = vm.BackUrl
                    });

                case "combine":
                    ModelState.Remove(nameof(vm.PSBT));
                    return View(nameof(WalletPSBTCombine), new WalletPSBTCombineViewModel
                    {
                        OtherPSBT = psbt.ToBase64(),
                        ReturnUrl = vm.ReturnUrl,
                        BackUrl = vm.BackUrl
                    });

                case "broadcast":
                    return RedirectToWalletPSBTReady(new WalletPSBTReadyViewModel
                    {
                        SigningContext = new SigningContextModel(psbt),
                        ReturnUrl = vm.ReturnUrl,
                        BackUrl = vm.BackUrl
                    });

                default:
                    return View("WalletPSBTDecoded", vm);
            }
        }

        private async Task<PSBT> GetPayjoinProposedTX(BitcoinUrlBuilder bip21, PSBT psbt, DerivationSchemeSettings derivationSchemeSettings, BTCPayNetwork btcPayNetwork, CancellationToken cancellationToken)
        {
            var cloned = psbt.Clone();
            cloned = cloned.Finalize();
            await _broadcaster.Schedule(DateTimeOffset.UtcNow + TimeSpan.FromMinutes(2.0), cloned.ExtractTransaction(), btcPayNetwork);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            var minRelayFee = _dashboard.Get(btcPayNetwork.CryptoCode).Status.BitcoinStatus?.MinRelayTxFee;
            _payjoinClient.MinimumFeeRate = minRelayFee;
            return await _payjoinClient.RequestPayjoin(bip21, new PayjoinWallet(derivationSchemeSettings), psbt, cts.Token);
        }

        private async Task FetchTransactionDetails(WalletId walletId, DerivationSchemeSettings derivationSchemeSettings, WalletPSBTReadyViewModel vm, BTCPayNetwork network)
        {
            var psbtObject = PSBT.Parse(vm.SigningContext.PSBT, network.NBitcoinNetwork);
            if (!psbtObject.IsAllFinalized())
                psbtObject = await ExplorerClientProvider.UpdatePSBT(derivationSchemeSettings, psbtObject) ?? psbtObject;
            IHDKey signingKey = null;
            RootedKeyPath signingKeyPath = null;
            try
            {
                signingKey = new BitcoinExtPubKey(vm.SigningKey, network.NBitcoinNetwork);
            }
            catch { }
            try
            {
                signingKey = signingKey ?? new BitcoinExtKey(vm.SigningKey, network.NBitcoinNetwork);
            }
            catch { }

            try
            {
                signingKeyPath = RootedKeyPath.Parse(vm.SigningKeyPath);
            }
            catch { }

            if (signingKey == null || signingKeyPath == null)
            {
                var signingKeySettings = derivationSchemeSettings.GetSigningAccountKeySettings();
                if (signingKey == null)
                {
                    signingKey = signingKeySettings.AccountKey;
                    vm.SigningKey = signingKey.ToString();
                }
                if (vm.SigningKeyPath == null)
                {
                    signingKeyPath = signingKeySettings.GetRootedKeyPath();
                    vm.SigningKeyPath = signingKeyPath?.ToString();
                }
            }

            if (psbtObject.IsAllFinalized())
            {
                vm.CanCalculateBalance = false;
            }
            else
            {
                var balanceChange = psbtObject.GetBalance(derivationSchemeSettings.AccountDerivation, signingKey, signingKeyPath);
                vm.BalanceChange = ValueToString(balanceChange, network);
                vm.CanCalculateBalance = true;
                vm.Positive = balanceChange >= Money.Zero;
            }
            vm.Inputs = new List<WalletPSBTReadyViewModel.InputViewModel>();
            var inputToObjects = new Dictionary<uint, ObjectTypeId[]>();
            var outputToObjects = new Dictionary<string, ObjectTypeId>();
            foreach (var input in psbtObject.Inputs)
            {
                var inputVm = new WalletPSBTReadyViewModel.InputViewModel();
                vm.Inputs.Add(inputVm);
                var txOut = input.GetTxOut();
                var mine = input.HDKeysFor(derivationSchemeSettings.AccountDerivation, signingKey, signingKeyPath).Any();
                var balanceChange2 = txOut?.Value ?? Money.Zero;
                if (mine)
                    balanceChange2 = -balanceChange2;
                inputVm.BalanceChange = ValueToString(balanceChange2, network);
                inputVm.Positive = balanceChange2 >= Money.Zero;
                inputVm.Index = (int)input.Index;

                var walletObjectIds = new List<ObjectTypeId>();
                walletObjectIds.Add(new ObjectTypeId(WalletObjectData.Types.Utxo, input.PrevOut.ToString()));
                walletObjectIds.Add(new ObjectTypeId(WalletObjectData.Types.Tx, input.PrevOut.Hash.ToString()));
                var address = txOut?.ScriptPubKey.GetDestinationAddress(network.NBitcoinNetwork)?.ToString();
                if (address != null)
                    walletObjectIds.Add(new ObjectTypeId(WalletObjectData.Types.Address, address));
                inputToObjects.Add(input.Index, walletObjectIds.ToArray());

            }
            vm.Destinations = new List<WalletPSBTReadyViewModel.DestinationViewModel>();
            foreach (var output in psbtObject.Outputs)
            {
                var dest = new WalletPSBTReadyViewModel.DestinationViewModel();
                vm.Destinations.Add(dest);
                var mine = output.HDKeysFor(derivationSchemeSettings.AccountDerivation, signingKey, signingKeyPath).Any();
                var balanceChange2 = output.Value;
                if (!mine)
                    balanceChange2 = -balanceChange2;
                dest.Balance = ValueToString(balanceChange2, network);
                dest.Positive = balanceChange2 >= Money.Zero;
                dest.Destination = output.ScriptPubKey.GetDestinationAddress(network.NBitcoinNetwork)?.ToString() ?? output.ScriptPubKey.ToString();
                var address = output.ScriptPubKey.GetDestinationAddress(network.NBitcoinNetwork)?.ToString();
                if (address != null)
                    outputToObjects.Add(dest.Destination, new ObjectTypeId(WalletObjectData.Types.Address, address));

            }

            if (psbtObject.TryGetFee(out var fee))
            {
                vm.Destinations.Add(new WalletPSBTReadyViewModel.DestinationViewModel
                {
                    Positive = false,
                    Balance = ValueToString(-fee, network),
                    Destination = "Mining fees"
                });
            }
            if (psbtObject.TryGetEstimatedFeeRate(out var feeRate))
            {
                vm.FeeRate = feeRate.ToString();
            }

            var sanityErrors = psbtObject.CheckSanity();
            if (sanityErrors.Count != 0)
            {
                vm.SetErrors(sanityErrors);
            }
            else if (!psbtObject.IsAllFinalized() && !psbtObject.TryFinalize(out var errors))
            {
                vm.SetErrors(errors);
            }

            var combinedTypeIds = inputToObjects.Values.SelectMany(ids => ids).Concat(outputToObjects.Values)
                .DistinctBy(id => $"{id.Type}:{id.Id}").ToArray();

            var labelInfo = await WalletRepository.GetWalletTransactionsInfo(walletId, combinedTypeIds);
            foreach (KeyValuePair<uint, ObjectTypeId[]> inputToObject in inputToObjects)
            {
                var keys = inputToObject.Value.Select(id => id.Id).ToArray();
                WalletTransactionInfo ix = null;
                foreach (var key in keys)
                {
                    if (!labelInfo.TryGetValue(key, out var i))
                        continue;
                    if (ix is null)
                    {
                        ix = i;
                    }
                    else
                    {
                        ix.Merge(i);
                    }
                }
                if (ix is null)
                    continue;

                var labels = _labelService.CreateTransactionTagModels(ix, Request);
                var input = vm.Inputs.First(model => model.Index == inputToObject.Key);
                input.Labels = labels;
            }
            foreach (var outputToObject in outputToObjects)
            {
                if (!labelInfo.TryGetValue(outputToObject.Value.Id, out var ix))
                    continue;
                var labels = _labelService.CreateTransactionTagModels(ix, Request);
                var destination = vm.Destinations.First(model => model.Destination == outputToObject.Key);
                destination.Labels = labels;
            }

        }

        [HttpPost("{walletId}/psbt/ready")]
        public async Task<IActionResult> WalletPSBTReady(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, WalletPSBTViewModel vm, string command, CancellationToken cancellationToken = default)
        {
            var network = NetworkProvider.GetNetwork<BTCPayNetwork>(walletId.CryptoCode);
            PSBT psbt = await vm.GetPSBT(network.NBitcoinNetwork, ModelState);
            if (vm.InvalidPSBT || psbt is null)
            {
                if (vm.InvalidPSBT)
                    vm.Errors.Add("Invalid PSBT");
                return View(nameof(WalletPSBT), vm);
            }
            DerivationSchemeSettings derivationSchemeSettings = GetDerivationSchemeSettings(walletId);
            if (derivationSchemeSettings == null)
                return NotFound();

            await FetchTransactionDetails(walletId, derivationSchemeSettings, vm, network);

            switch (command)
            {
                case "payjoin":
                    string error;
                    try
                    {
                        var proposedPayjoin = await GetPayjoinProposedTX(new BitcoinUrlBuilder(vm.SigningContext.PayJoinBIP21, network.NBitcoinNetwork), psbt,
                            derivationSchemeSettings, network, cancellationToken);
                        try
                        {
                            proposedPayjoin.Settings.SigningOptions = new SigningOptions
                            {
                                EnforceLowR = !(vm.SigningContext?.EnforceLowR is false)
                            };
                            var extKey = ExtKey.Parse(vm.SigningKey, network.NBitcoinNetwork);
                            proposedPayjoin = proposedPayjoin.SignAll(derivationSchemeSettings.AccountDerivation,
                                extKey,
                                RootedKeyPath.Parse(vm.SigningKeyPath));
                            vm.SigningContext.PSBT = proposedPayjoin.ToBase64();
                            vm.SigningContext.OriginalPSBT = psbt.ToBase64();
                            proposedPayjoin.Finalize();
                            var hash = proposedPayjoin.ExtractTransaction().GetHash();
                            await WalletRepository.AddWalletTransactionAttachment(walletId, hash, Attachment.Payjoin());
                            TempData.SetStatusMessageModel(new StatusMessageModel
                            {
                                Severity = StatusMessageModel.StatusSeverity.Success,
                                AllowDismiss = false,
                                Html = $"The payjoin transaction has been successfully broadcasted ({proposedPayjoin.ExtractTransaction().GetHash()})"
                            });
                            return await WalletPSBTReady(walletId, vm, "broadcast");
                        }
                        catch (Exception)
                        {
                            TempData.SetStatusMessageModel(new StatusMessageModel()
                            {
                                Severity = StatusMessageModel.StatusSeverity.Warning,
                                AllowDismiss = false,
                                Html =
                                    "This transaction has been coordinated between the receiver and you to create a <a href='https://en.bitcoin.it/wiki/PayJoin' target='_blank'>payjoin transaction</a> by adding inputs from the receiver.<br/>" +
                                    "The amount being sent may appear higher but is in fact almost same.<br/><br/>" +
                                    "If you cancel or refuse to sign this transaction, the payment will proceed without payjoin"
                            });
                            vm.SigningContext.PSBT = proposedPayjoin.ToBase64();
                            vm.SigningContext.OriginalPSBT = psbt.ToBase64();
                            return ViewVault(walletId, vm);
                        }
                    }
                    catch (PayjoinReceiverException ex)
                    {
                        error = $"The payjoin receiver could not complete the payjoin: {ex.Message}";
                    }
                    catch (PayjoinSenderException ex)
                    {
                        error = $"We rejected the receiver's payjoin proposal: {ex.Message}";
                    }
                    catch (Exception ex)
                    {
                        error = $"Unexpected payjoin error: {ex.Message}";
                    }

                    //we possibly exposed the tx to the receiver, so we need to broadcast straight away
                    psbt.Finalize();
                    TempData.SetStatusMessageModel(new StatusMessageModel
                    {
                        Severity = StatusMessageModel.StatusSeverity.Warning,
                        AllowDismiss = false,
                        Html = $"The payjoin transaction could not be created.<br/>" +
                               $"The original transaction was broadcasted instead. ({psbt.ExtractTransaction().GetHash()})<br/><br/>" +
                               $"{error}"
                    });
                    return await WalletPSBTReady(walletId, vm, "broadcast");
                case "broadcast" when !psbt.IsAllFinalized() && !psbt.TryFinalize(out var errors):
                    vm.SetErrors(errors);
                    return View(nameof(WalletPSBT), vm);
                case "broadcast":
                    {
                        var transaction = psbt.ExtractTransaction();
                        try
                        {
                            var broadcastResult = await ExplorerClientProvider.GetExplorerClient(network).BroadcastAsync(transaction);
                            if (!broadcastResult.Success)
                            {
                                if (!string.IsNullOrEmpty(vm.SigningContext.OriginalPSBT))
                                {
                                    TempData.SetStatusMessageModel(new StatusMessageModel
                                    {
                                        Severity = StatusMessageModel.StatusSeverity.Warning,
                                        AllowDismiss = false,
                                        Html = $"The payjoin transaction could not be broadcasted.<br/>({broadcastResult.RPCCode} {broadcastResult.RPCCodeMessage} {broadcastResult.RPCMessage}).<br/>The transaction has been reverted back to its original format and has been broadcast."
                                    });
                                    vm.SigningContext.PSBT = vm.SigningContext.OriginalPSBT;
                                    vm.SigningContext.OriginalPSBT = null;
                                    return await WalletPSBTReady(walletId, vm, "broadcast");
                                }

                                vm.Errors.Add($"RPC Error while broadcasting: {broadcastResult.RPCCode} {broadcastResult.RPCCodeMessage} {broadcastResult.RPCMessage}");
                                return View(nameof(WalletPSBT), vm);
                            }
                            else
                            {
                                var wallet = _walletProvider.GetWallet(network);
                                var derivationSettings = GetDerivationSchemeSettings(walletId);
                                wallet.InvalidateCache(derivationSettings.AccountDerivation);
                            }
                        }
                        catch (Exception ex)
                        {
                            vm.Errors.Add("Error while broadcasting: " + ex.Message);
                            return View(nameof(WalletPSBT), vm);
                        }

                        if (!TempData.HasStatusMessage())
                        {
                            TempData[WellKnownTempData.SuccessMessage] = $"Transaction broadcasted successfully ({transaction.GetHash()})";
                        }
                        if (!string.IsNullOrEmpty(vm.ReturnUrl))
                        {
                            return LocalRedirect(vm.ReturnUrl);
                        }
                        return RedirectToAction(nameof(WalletTransactions), new { walletId = walletId.ToString() });
                    }
                case "analyze-psbt":
                    return RedirectToWalletPSBT(new WalletPSBTViewModel
                    {
                        PSBT = psbt.ToBase64(),
                        ReturnUrl = vm.ReturnUrl,
                        BackUrl = vm.BackUrl
                    });
                case "decode":
                    await FetchTransactionDetails(walletId, derivationSchemeSettings, vm, network);
                    return View("WalletPSBTDecoded", vm);
                default:
                    vm.Errors.Add("Unknown command");
                    return View(nameof(WalletPSBT), vm);
            }
        }

        private IActionResult FilePSBT(PSBT psbt, string fileName)
        {
            return File(psbt.ToBytes(), "application/octet-stream", fileName);
        }

        [HttpPost("{walletId}/psbt/combine")]
        public async Task<IActionResult> WalletPSBTCombine([ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, WalletPSBTCombineViewModel vm)
        {
            var network = NetworkProvider.GetNetwork<BTCPayNetwork>(walletId.CryptoCode);
            var psbt = await vm.GetPSBT(network.NBitcoinNetwork, ModelState);
            if (psbt == null)
            {
                return View(vm);
            }
            var sourcePSBT = vm.GetSourcePSBT(network.NBitcoinNetwork, ModelState);
            if (sourcePSBT is null)
            {
                return View(vm);
            }
            sourcePSBT = sourcePSBT.Combine(psbt);
            TempData[WellKnownTempData.SuccessMessage] = "PSBT Successfully combined!";
            return RedirectToWalletPSBT(new WalletPSBTViewModel
            {
                PSBT = sourcePSBT.ToBase64(),
                ReturnUrl = vm.ReturnUrl
            });
        }
    }
}
