using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.ModelBinders;
using BTCPayServer.Models;
using BTCPayServer.Models.WalletViewModels;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
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
                psbtRequest.IncludeOnlyOutpoints = sendModel.SelectedInputs?.Select(OutPoint.Parse)?.ToList()?? new List<OutPoint>();
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
                psbtRequest.RBF = !sendModel.DisableRBF;
            }
           
            psbtRequest.FeePreference = new FeePreference();
            psbtRequest.FeePreference.ExplicitFeeRate = new FeeRate(Money.Satoshis(sendModel.FeeSatoshiPerByte), 1);
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
            var psbt = await vm.GetPSBT(network.NBitcoinNetwork);
            if (psbt == null)
            {
                ModelState.AddModelError(nameof(vm.PSBT), "Invalid PSBT");
                return View(vm);
            }
            switch (command)
            {
                case "decode":
                    vm.Decoded = psbt.ToString();
                    ModelState.Remove(nameof(vm.PSBT));
                    ModelState.Remove(nameof(vm.FileName));
                    ModelState.Remove(nameof(vm.UploadedPSBTFile));
                    vm.PSBT = psbt.ToBase64();
                    vm.FileName = vm.UploadedPSBTFile?.FileName;
                    return View(vm);
                case "vault":
                    return ViewVault(walletId, psbt, vm.PayJoinEndpointUrl);
                case "ledger":
                    return ViewWalletSendLedger(walletId, psbt);
                case "update":
                    var derivationSchemeSettings = GetDerivationSchemeSettings(walletId);
                    psbt = await UpdatePSBT(derivationSchemeSettings, psbt, network);
                    if (psbt == null)
                    {
                        ModelState.AddModelError(nameof(vm.PSBT), "You need to update your version of NBXplorer");
                        return View(vm);
                    }
                    TempData[WellKnownTempData.SuccessMessage] = "PSBT updated!";
                    return RedirectToWalletPSBT(psbt, vm.FileName);
                case "seed":
                    return SignWithSeed(walletId, psbt.ToBase64(), vm.PayJoinEndpointUrl);
                case "nbx-seed":
                    if (await CanUseHotWallet())
                    {
                        var derivationScheme = GetDerivationSchemeSettings(walletId);
                        var extKey = await ExplorerClientProvider.GetExplorerClient(network)
                            .GetMetadataAsync<string>(derivationScheme.AccountDerivation,
                                WellknownMetadataKeys.MasterHDKey);

                        return await SignWithSeed(walletId,
                            new SignWithSeedViewModel() {SeedOrKey = extKey, PSBT = psbt.ToBase64(), PayJoinEndpointUrl = vm.PayJoinEndpointUrl});
                    }

                    return View(vm);
                case "broadcast":
                {
                    return RedirectToWalletPSBTReady(psbt.ToBase64());
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

        private async Task<PSBT> UpdatePSBT(DerivationSchemeSettings derivationSchemeSettings, PSBT psbt, BTCPayNetwork network)
        {
            var result = await ExplorerClientProvider.GetExplorerClient(network).UpdatePSBTAsync(new UpdatePSBTRequest()
            {
                PSBT = psbt,
                DerivationScheme = derivationSchemeSettings.AccountDerivation,
            });
            if (result == null)
                return null;
            derivationSchemeSettings.RebaseKeyPaths(result.PSBT);
            return result.PSBT;
        }

       

        private async Task<PSBT> TryGetPayjoinProposedTX(string bpu, PSBT psbt, DerivationSchemeSettings derivationSchemeSettings, BTCPayNetwork btcPayNetwork)
        {
            if (!string.IsNullOrEmpty(bpu) && Uri.TryCreate(bpu, UriKind.Absolute, out var endpoint))
            {
                var httpClient = _httpClientFactory.CreateClient("payjoin");

                var cloned = psbt.Clone();
                    
                if (!cloned.IsAllFinalized() && !cloned.TryFinalize(out var errors))
                {
                    return null;
                }
                
                var bpuresponse = await httpClient.PostAsync(endpoint, new StringContent(cloned.ToHex(), Encoding.UTF8, "text/plain"));
                if (bpuresponse.IsSuccessStatusCode)
                {
                    var hex = await bpuresponse.Content.ReadAsStringAsync();
                    if (PSBT.TryParse(hex, btcPayNetwork.NBitcoinNetwork, out var newPSBT))
                    {
                        //check that all the inputs we provided are still there and that there is at least one new(signed) input.
                        bool valid = false;
                        var existingInputs = psbt.Inputs.Select(input => input.PrevOut).ToHashSet();
                        foreach (var input in newPSBT.Inputs)
                        {
                            var existingInput = existingInputs.SingleOrDefault(point => point == input.PrevOut);
                            if (existingInput != null)
                            {
                                existingInputs.Remove(existingInput);
                                continue;
                            }

                            if (!input.TryFinalizeInput(out _))
                            {
                                //a new input was provided but was invalid.
                                valid = false;
                                break;
                            }
                            // a new signed input was provided
                            valid = true;
                        }

                        if (!valid || existingInputs.Any())
                        {
                            return null;
                        }

                        //check if output sum to self is the same.
                        var signingAccountKeySettings = derivationSchemeSettings.GetSigningAccountKeySettings();
                        var newOutputSumToSelfSum = newPSBT.Outputs.CoinsFor(derivationSchemeSettings.AccountDerivation, signingAccountKeySettings.AccountKey,
                            signingAccountKeySettings.GetRootedKeyPath()).Sum(output => output.Value);

                        var originalOutputSumToSelf = psbt.Outputs.Sum(output => output.Value);
                        if (originalOutputSumToSelf < newOutputSumToSelfSum)
                        {
                            return null;
                        }
                        
                        newPSBT = await UpdatePSBT(derivationSchemeSettings, newPSBT, btcPayNetwork);
                        return newPSBT;
                    }
                }
            }

            return null;
        }
        
        [HttpGet]
        [Route("{walletId}/psbt/ready")]
        public async Task<IActionResult> WalletPSBTReady(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, string psbt = null, 
            string signingKey = null,
            string signingKeyPath = null,
            string originalPsbt = null,
            string payJoinEndpointUrl = null)
        {
            var network = NetworkProvider.GetNetwork<BTCPayNetwork>(walletId.CryptoCode);
            var vm = new WalletPSBTReadyViewModel() { PSBT = psbt };
            vm.SigningKey = signingKey;
            vm.SigningKeyPath = signingKeyPath;
            vm.OriginalPSBT = originalPsbt;
            vm.PayJoinEndpointUrl = payJoinEndpointUrl;
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
            var psbtObject = PSBT.Parse(vm.PSBT, network.NBitcoinNetwork);
            if (!psbtObject.IsAllFinalized())
                psbtObject = await UpdatePSBT(derivationSchemeSettings, psbtObject, network) ?? psbtObject;
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
                    Balance = ValueToString(- fee, network),
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
            WalletId walletId, WalletPSBTReadyViewModel vm, string command = null)
        {
            if (command == null)
                return await WalletPSBTReady(walletId, vm.PSBT, vm.SigningKey, vm.SigningKeyPath, vm.OriginalPSBT, vm.PayJoinEndpointUrl);
            PSBT psbt = null;
            var network = NetworkProvider.GetNetwork<BTCPayNetwork>(walletId.CryptoCode);
            DerivationSchemeSettings derivationSchemeSettings = null;
            try
            {
                psbt = PSBT.Parse(vm.PSBT, network.NBitcoinNetwork);
                derivationSchemeSettings = GetDerivationSchemeSettings(walletId);
                if (derivationSchemeSettings == null)
                    return NotFound();
                await FetchTransactionDetails(derivationSchemeSettings, vm, network);
            }
            catch
            {
                vm.GlobalError = "Invalid PSBT";
                return View(nameof(WalletPSBTReady),vm);
            }
        
            switch (command)
            {
                case "payjoin":
                    var proposedPayjoin =await 
                        TryGetPayjoinProposedTX(vm.PayJoinEndpointUrl, psbt, derivationSchemeSettings, network);
                    if (proposedPayjoin == null)
                    {
                        //we possibly exposed the tx to the receiver, so we need to broadcast straight away
                        TempData.SetStatusMessageModel(new StatusMessageModel()
                        {
                            Severity = StatusMessageModel.StatusSeverity.Warning,
                            AllowDismiss = false,
                            Html = $"The payjoin transaction could not be created. The original transaction was broadcast instead."
                        });
                        return await WalletPSBTReady(walletId, vm, "broadcast");
                    }
                    else
                    {
                        
                        try
                        {
                            var extKey = ExtKey.Parse(vm.SigningKey, network.NBitcoinNetwork);
                            var payjoinSigned = PSBTChanged(proposedPayjoin,
                                () => proposedPayjoin.SignAll(derivationSchemeSettings.AccountDerivation, 
                                    extKey,
                                    RootedKeyPath.Parse(vm.SigningKeyPath)));
                            if (!payjoinSigned)
                            {
                                TempData.SetStatusMessageModel(new StatusMessageModel()
                                {
                                    Severity = StatusMessageModel.StatusSeverity.Warning,
                                    AllowDismiss = false,
                                    Html = $"The payjoin transaction could not be signed. The original transaction was broadcast instead."
                                });
                                return await WalletPSBTReady(walletId, vm, "broadcast");
                            }
                            vm.PSBT = proposedPayjoin.ToBase64();
                            vm.OriginalPSBT = psbt.ToBase64();
                            return await WalletPSBTReady(walletId, vm, "broadcast");
                        }
                        catch (Exception e)
                        {
                            TempData.SetStatusMessageModel(new StatusMessageModel()
                            {
                                Severity = StatusMessageModel.StatusSeverity.Info,
                                AllowDismiss = false,
                                Html =
                                    $"This transaction has been coordinated between the receiver and you to create a <a href='https://en.bitcoin.it/wiki/PayJoin' target='_blank'>payjoin transaction</a> by adding inputs from the receiver. The amount being sent may appear higher but is in fact the same"
                            });
                            return ViewVault(walletId, proposedPayjoin, vm.PayJoinEndpointUrl, psbt);
                        }
                    }
                case "broadcast" when !psbt.IsAllFinalized() && !psbt.TryFinalize(out var errors):
                    vm.SetErrors(errors);
                    return View(nameof(WalletPSBTReady),vm);
                case "broadcast":
                {
                    var transaction = psbt.ExtractTransaction();
                    try
                    {
                        var broadcastResult = await ExplorerClientProvider.GetExplorerClient(network).BroadcastAsync(transaction);
                        if (!broadcastResult.Success)
                        {
                            if (!string.IsNullOrEmpty(vm.OriginalPSBT))
                            {
                                TempData.SetStatusMessageModel(new StatusMessageModel()
                                {
                                    Severity = StatusMessageModel.StatusSeverity.Warning,
                                    AllowDismiss = false,
                                    Html = $"The payjoin transaction could not be broadcast.<br/>({broadcastResult.RPCCode} {broadcastResult.RPCCodeMessage} {broadcastResult.RPCMessage}).<br/>The transaction has been reverted back to its original format and has been broadcast."
                                });
                                vm.PSBT = vm.OriginalPSBT;
                                vm.OriginalPSBT = null;
                                return await WalletPSBTReady(walletId, vm, "broadcast");
                            }
                        
                            vm.GlobalError = $"RPC Error while broadcasting: {broadcastResult.RPCCode} {broadcastResult.RPCCodeMessage} {broadcastResult.RPCMessage}";
                            return View(nameof(WalletPSBTReady),vm);
                        }
                    }
                    catch (Exception ex)
                    {
                        vm.GlobalError = "Error while broadcasting: " + ex.Message;
                        return View(nameof(WalletPSBTReady),vm);
                    }
                    return RedirectToWalletTransaction(walletId, transaction);
                }
                case "analyze-psbt":
                    return RedirectToWalletPSBT(psbt);
                default:
                    vm.GlobalError = "Unknown command";
                    return View(nameof(WalletPSBTReady),vm);
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
            return RedirectToWalletPSBT(sourcePSBT);
        }
    }
}
