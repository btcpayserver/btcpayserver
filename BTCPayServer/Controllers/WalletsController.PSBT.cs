using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.HostedServices;
using BTCPayServer.ModelBinders;
using BTCPayServer.Models;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.Payment;
using NBXplorer;
using NBXplorer.Models;

namespace BTCPayServer.Controllers
{
    public partial class WalletsController
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

            if (network.SupportRBF)
            {
                if (sendModel.AllowFeeBump is WalletSendModel.ThreeStateBool.Yes)
                    psbtRequest.RBF = true;
                if (sendModel.AllowFeeBump is WalletSendModel.ThreeStateBool.No)
                    psbtRequest.RBF = false;
            }

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

        [HttpGet]
        [Route("{walletId}/psbt")]
        public async Task<IActionResult> WalletPSBT([ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, WalletPSBTViewModel vm)
        {
            var network = NetworkProvider.GetNetwork<BTCPayNetwork>(walletId.CryptoCode);
            vm.CryptoCode = network.CryptoCode;
            vm.NBXSeedAvailable = await CanUseHotWallet() && !string.IsNullOrEmpty(await ExplorerClientProvider.GetExplorerClient(network)
                .GetMetadataAsync<string>(GetDerivationSchemeSettings(walletId).AccountDerivation,
                    WellknownMetadataKeys.Mnemonic));
            if (await vm.GetPSBT(network.NBitcoinNetwork) is PSBT psbt)
            {
                vm.Decoded = psbt.ToString();
                vm.PSBT = psbt.ToBase64();
                vm.PSBTHex = psbt.ToHex();
            }

            return View(nameof(WalletPSBT), vm ?? new WalletPSBTViewModel() { CryptoCode = walletId.CryptoCode });
        }
        [HttpPost]
        [Route("{walletId}/psbt")]
        public async Task<IActionResult> WalletPSBT(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId,
            WalletPSBTViewModel vm, string command = null)
        {
            if (command == null)
                return await WalletPSBT(walletId, vm);
            var network = NetworkProvider.GetNetwork<BTCPayNetwork>(walletId.CryptoCode);
            vm.CryptoCode = network.CryptoCode;
            vm.NBXSeedAvailable = await CanUseHotWallet() && !string.IsNullOrEmpty(await ExplorerClientProvider.GetExplorerClient(network)
                .GetMetadataAsync<string>(GetDerivationSchemeSettings(walletId).AccountDerivation,
                    WellknownMetadataKeys.Mnemonic));
            var psbt = await vm.GetPSBT(network.NBitcoinNetwork);
            if (psbt == null)
            {
                ModelState.AddModelError(nameof(vm.PSBT), "Invalid PSBT");
                return View(vm);
            }

            vm.PSBTHex = psbt.ToHex();
            var res = await TryHandleSigningCommands(walletId, psbt, command, new SigningContextModel(psbt));
            if (res != null)
            {
                return res;
            }
            switch (command)
            {
                case "decode":
                    vm.Decoded = psbt.ToString();
                    ModelState.Remove(nameof(vm.PSBT));
                    ModelState.Remove(nameof(vm.FileName));
                    ModelState.Remove(nameof(vm.UploadedPSBTFile));
                    vm.PSBT = psbt.ToBase64();
                    vm.PSBTHex = psbt.ToHex();
                    vm.FileName = vm.UploadedPSBTFile?.FileName;
                    return View(vm);

                case "update":
                    var derivationSchemeSettings = GetDerivationSchemeSettings(walletId);
                    psbt = await ExplorerClientProvider.UpdatePSBT(derivationSchemeSettings, psbt);
                    if (psbt == null)
                    {
                        ModelState.AddModelError(nameof(vm.PSBT), "You need to update your version of NBXplorer");
                        return View(vm);
                    }
                    TempData[WellKnownTempData.SuccessMessage] = "PSBT updated!";
                    return RedirectToWalletPSBT(new WalletPSBTViewModel()
                    {
                        PSBT = psbt.ToBase64(),
                        FileName = vm.FileName
                    });

                case "broadcast":
                    {
                        return RedirectToWalletPSBTReady(new WalletPSBTReadyViewModel()
                        {
                            SigningContext = new SigningContextModel(psbt)
                        });
                    }
                case "combine":
                    ModelState.Remove(nameof(vm.PSBT));
                    return View(nameof(WalletPSBTCombine), new WalletPSBTCombineViewModel() { OtherPSBT = psbt.ToBase64() });
                case "save-psbt":
                    return FilePSBT(psbt, vm.FileName);
                default:
                    return View(vm);
            }
        }

        private async Task<PSBT> GetPayjoinProposedTX(BitcoinUrlBuilder bip21, PSBT psbt, DerivationSchemeSettings derivationSchemeSettings, BTCPayNetwork btcPayNetwork, CancellationToken cancellationToken)
        {
            var cloned = psbt.Clone();
            cloned = cloned.Finalize();
            await _broadcaster.Schedule(DateTimeOffset.UtcNow + TimeSpan.FromMinutes(2.0), cloned.ExtractTransaction(), btcPayNetwork);
            return await _payjoinClient.RequestPayjoin(bip21, derivationSchemeSettings, psbt, cancellationToken);
        }

        [HttpGet]
        [Route("{walletId}/psbt/ready")]
        public async Task<IActionResult> WalletPSBTReady(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId,
            WalletPSBTReadyViewModel vm)
        {
            if (vm is null)
                return NotFound();
            var network = NetworkProvider.GetNetwork<BTCPayNetwork>(walletId.CryptoCode);
            var derivationSchemeSettings = GetDerivationSchemeSettings(walletId);
            if (derivationSchemeSettings == null)
                return NotFound();
            try
            {
                await FetchTransactionDetails(derivationSchemeSettings, vm, network);
            }
            catch { return BadRequest(); }
            return View(nameof(WalletPSBTReady), vm);
        }

        private async Task FetchTransactionDetails(DerivationSchemeSettings derivationSchemeSettings, WalletPSBTReadyViewModel vm, BTCPayNetwork network)
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
            foreach (var input in psbtObject.Inputs)
            {
                var inputVm = new WalletPSBTReadyViewModel.InputViewModel();
                vm.Inputs.Add(inputVm);
                var mine = input.HDKeysFor(derivationSchemeSettings.AccountDerivation, signingKey, signingKeyPath).Any();
                var balanceChange2 = input.GetTxOut()?.Value ?? Money.Zero;
                if (mine)
                    balanceChange2 = -balanceChange2;
                inputVm.BalanceChange = ValueToString(balanceChange2, network);
                inputVm.Positive = balanceChange2 >= Money.Zero;
                inputVm.Index = (int)input.Index;
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
            }

            if (psbtObject.TryGetFee(out var fee))
            {
                vm.Destinations.Add(new WalletPSBTReadyViewModel.DestinationViewModel()
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
        }

        [HttpPost]
        [Route("{walletId}/psbt/ready")]
        public async Task<IActionResult> WalletPSBTReady(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, WalletPSBTReadyViewModel vm, string command = null, CancellationToken cancellationToken = default)
        {
            if (command == null)
                return await WalletPSBTReady(walletId, vm);
            PSBT psbt = null;
            var network = NetworkProvider.GetNetwork<BTCPayNetwork>(walletId.CryptoCode);
            DerivationSchemeSettings derivationSchemeSettings = null;
            try
            {
                psbt = PSBT.Parse(vm.SigningContext.PSBT, network.NBitcoinNetwork);
                derivationSchemeSettings = GetDerivationSchemeSettings(walletId);
                if (derivationSchemeSettings == null)
                    return NotFound();
                await FetchTransactionDetails(derivationSchemeSettings, vm, network);
            }
            catch
            {
                vm.GlobalError = "Invalid PSBT";
                return View(nameof(WalletPSBTReady), vm);
            }

            switch (command)
            {
                case "payjoin":
                    string error = null;
                    try
                    {
                        var proposedPayjoin = await GetPayjoinProposedTX(new BitcoinUrlBuilder(vm.SigningContext.PayJoinBIP21, network.NBitcoinNetwork), psbt,
                            derivationSchemeSettings, network, cancellationToken);
                        try
                        {
                            var extKey = ExtKey.Parse(vm.SigningKey, network.NBitcoinNetwork);
                            proposedPayjoin = proposedPayjoin.SignAll(derivationSchemeSettings.AccountDerivation,
                                extKey,
                                RootedKeyPath.Parse(vm.SigningKeyPath), new SigningOptions()
                                {
                                    EnforceLowR = !(vm.SigningContext?.EnforceLowR is false)
                                });
                            vm.SigningContext.PSBT = proposedPayjoin.ToBase64();
                            vm.SigningContext.OriginalPSBT = psbt.ToBase64();
                            proposedPayjoin.Finalize();
                            var hash = proposedPayjoin.ExtractTransaction().GetHash();
                            _EventAggregator.Publish(new UpdateTransactionLabel(walletId, hash, UpdateTransactionLabel.PayjoinLabelTemplate()));
                            TempData.SetStatusMessageModel(new StatusMessageModel()
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
                                    $"This transaction has been coordinated between the receiver and you to create a <a href='https://en.bitcoin.it/wiki/PayJoin' target='_blank'>payjoin transaction</a> by adding inputs from the receiver.<br/>" +
                                    $"The amount being sent may appear higher but is in fact almost same.<br/><br/>" +
                                    $"If you cancel or refuse to sign this transaction, the payment will proceed without payjoin"
                            });
                            vm.SigningContext.PSBT = proposedPayjoin.ToBase64();
                            vm.SigningContext.OriginalPSBT = psbt.ToBase64();
                            return ViewVault(walletId, vm.SigningContext);
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
                    TempData.SetStatusMessageModel(new StatusMessageModel()
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
                    return View(nameof(WalletPSBTReady), vm);
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
                                    TempData.SetStatusMessageModel(new StatusMessageModel()
                                    {
                                        Severity = StatusMessageModel.StatusSeverity.Warning,
                                        AllowDismiss = false,
                                        Html = $"The payjoin transaction could not be broadcasted.<br/>({broadcastResult.RPCCode} {broadcastResult.RPCCodeMessage} {broadcastResult.RPCMessage}).<br/>The transaction has been reverted back to its original format and has been broadcast."
                                    });
                                    vm.SigningContext.PSBT = vm.SigningContext.OriginalPSBT;
                                    vm.SigningContext.OriginalPSBT = null;
                                    return await WalletPSBTReady(walletId, vm, "broadcast");
                                }

                                vm.GlobalError = $"RPC Error while broadcasting: {broadcastResult.RPCCode} {broadcastResult.RPCCodeMessage} {broadcastResult.RPCMessage}";
                                return View(nameof(WalletPSBTReady), vm);
                            }
                        }
                        catch (Exception ex)
                        {
                            vm.GlobalError = "Error while broadcasting: " + ex.Message;
                            return View(nameof(WalletPSBTReady), vm);
                        }

                        if (!TempData.HasStatusMessage())
                        {
                            TempData[WellKnownTempData.SuccessMessage] = $"Transaction broadcasted successfully ({transaction.GetHash()})";
                        }
                        return RedirectToWalletTransaction(walletId, transaction);
                    }
                case "analyze-psbt":
                    return RedirectToWalletPSBT(new WalletPSBTViewModel()
                    {
                        PSBT = psbt.ToBase64()
                    });
                default:
                    vm.GlobalError = "Unknown command";
                    return View(nameof(WalletPSBTReady), vm);
            }
        }

        private IActionResult FilePSBT(PSBT psbt, string fileName)
        {
            return File(psbt.ToBytes(), "application/octet-stream", fileName);
        }

        [HttpPost]
        [Route("{walletId}/psbt/combine")]
        public async Task<IActionResult> WalletPSBTCombine([ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, WalletPSBTCombineViewModel vm)
        {
            var network = NetworkProvider.GetNetwork<BTCPayNetwork>(walletId.CryptoCode);
            var psbt = await vm.GetPSBT(network.NBitcoinNetwork);
            if (psbt == null)
            {
                ModelState.AddModelError(nameof(vm.PSBT), "Invalid PSBT");
                return View(vm);
            }
            var sourcePSBT = vm.GetSourcePSBT(network.NBitcoinNetwork);
            if (sourcePSBT == null)
            {
                ModelState.AddModelError(nameof(vm.OtherPSBT), "Invalid PSBT");
                return View(vm);
            }
            sourcePSBT = sourcePSBT.Combine(psbt);
            TempData[WellKnownTempData.SuccessMessage] = "PSBT Successfully combined!";
            return RedirectToWalletPSBT(new WalletPSBTViewModel()
            {
                PSBT = sourcePSBT.ToBase64()
            });
        }

        private async Task<IActionResult> TryHandleSigningCommands(WalletId walletId, PSBT psbt, string command,
            SigningContextModel signingContext)
        {
            signingContext.PSBT = psbt.ToBase64();
            switch (command)
            {
                case "vault":
                    return ViewVault(walletId, signingContext);
                case "seed":
                    return SignWithSeed(walletId, signingContext);
                case "nbx-seed":
                    if (await CanUseHotWallet())
                    {
                        var derivationScheme = GetDerivationSchemeSettings(walletId);
                        if (derivationScheme.IsHotWallet)
                        {
                            var extKey = await ExplorerClientProvider.GetExplorerClient(walletId.CryptoCode)
                                .GetMetadataAsync<string>(derivationScheme.AccountDerivation,
                                    WellknownMetadataKeys.MasterHDKey);
                            return SignWithSeed(walletId,
                                new SignWithSeedViewModel() { SeedOrKey = extKey, SigningContext = signingContext });
                        }
                    }
                    TempData.SetStatusMessageModel(new StatusMessageModel()
                    {
                        Severity = StatusMessageModel.StatusSeverity.Error,
                        Message = "NBX seed functionality is not available"
                    });
                    break;
            }

            return null;
        }
    }
}
