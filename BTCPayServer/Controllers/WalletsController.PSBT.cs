﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.ModelBinders;
using BTCPayServer.Models.WalletViewModels;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
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
            return psbt;
        }

        [HttpGet]
        [Route("{walletId}/psbt")]
        public async Task<IActionResult> WalletPSBT([ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, WalletPSBTViewModel vm)
        {
            var network = NetworkProvider.GetNetwork<BTCPayNetwork>(walletId.CryptoCode);
            if (await vm.GetPSBT(network.NBitcoinNetwork) is PSBT psbt)
            {
                vm.Decoded = psbt.ToString();
                vm.PSBT = psbt.ToBase64();
            }
            return View(vm ?? new WalletPSBTViewModel());
        }
        [HttpPost]
        [Route("{walletId}/psbt")]
        public async Task<IActionResult> WalletPSBT(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId,
            WalletPSBTViewModel vm, string command = null)
        {
            var network = NetworkProvider.GetNetwork<BTCPayNetwork>(walletId.CryptoCode);
            var psbt = await vm.GetPSBT(network.NBitcoinNetwork);
            if (psbt == null)
            {
                ModelState.AddModelError(nameof(vm.PSBT), "Invalid PSBT");
                return View(vm);
            }
            switch (command)
            {
                case null:
                    vm.Decoded = psbt.ToString();
                    ModelState.Remove(nameof(vm.PSBT));
                    ModelState.Remove(nameof(vm.FileName));
                    ModelState.Remove(nameof(vm.UploadedPSBTFile));
                    vm.PSBT = psbt.ToBase64();
                    vm.FileName = vm.UploadedPSBTFile?.FileName;
                    return View(vm);
                case "ledger":
                    return ViewWalletSendLedger(psbt);
                case "update":
                    var derivationSchemeSettings = await GetDerivationSchemeSettings(walletId);
                    psbt = await UpdatePSBT(derivationSchemeSettings, psbt, network);
                    if (psbt == null)
                    {
                        ModelState.AddModelError(nameof(vm.PSBT), "You need to update your version of NBXplorer");
                        return View(vm);
                    }
                    StatusMessage = "PSBT updated!";
                    return RedirectToAction(nameof(WalletPSBT), new { walletId = walletId, psbt = psbt.ToBase64(), FileName = vm.FileName });
                case "seed":
                    return SignWithSeed(walletId, psbt.ToBase64());
                case "broadcast":
                {
                    return await WalletPSBTReady(walletId, psbt.ToBase64());
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

        [HttpGet]
        [Route("{walletId}/psbt/ready")]
        public async Task<IActionResult> WalletPSBTReady(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, string psbt = null, 
            string signingKey = null,
            string signingKeyPath = null)
        {
            var network = NetworkProvider.GetNetwork<BTCPayNetwork>(walletId.CryptoCode);
            var vm = new WalletPSBTReadyViewModel() { PSBT = psbt };
            vm.SigningKey = signingKey;
            vm.SigningKeyPath = signingKeyPath;

            var derivationSchemeSettings = await GetDerivationSchemeSettings(walletId);
            if (derivationSchemeSettings == null)
                return NotFound();
            await FetchTransactionDetails(derivationSchemeSettings, vm, network);
            return View(nameof(WalletPSBTReady), vm);
        }

        private async Task FetchTransactionDetails(DerivationSchemeSettings derivationSchemeSettings, WalletPSBTReadyViewModel vm, BTCPayNetwork network)
        {
            var psbtObject = PSBT.Parse(vm.PSBT, network.NBitcoinNetwork);
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
            PSBT psbt = null;
            var network = NetworkProvider.GetNetwork<BTCPayNetwork>(walletId.CryptoCode);
            try
            {
                psbt = PSBT.Parse(vm.PSBT, network.NBitcoinNetwork);
                var derivationSchemeSettings = await GetDerivationSchemeSettings(walletId);
                if (derivationSchemeSettings == null)
                    return NotFound();
                await FetchTransactionDetails(derivationSchemeSettings, vm, network);
            }
            catch
            {
                vm.GlobalError = "Invalid PSBT";
                return View(vm);
            }
            if (command == "broadcast")
            {
                if (!psbt.IsAllFinalized() && !psbt.TryFinalize(out var errors))
                {
                    vm.SetErrors(errors);
                    return View(vm);
                }
                var transaction = psbt.ExtractTransaction();
                try
                {
                    var broadcastResult = await ExplorerClientProvider.GetExplorerClient(network).BroadcastAsync(transaction);
                    if (!broadcastResult.Success)
                    {
                        vm.GlobalError = $"RPC Error while broadcasting: {broadcastResult.RPCCode} {broadcastResult.RPCCodeMessage} {broadcastResult.RPCMessage}";
                        return View(vm);
                    }
                }
                catch (Exception ex)
                {
                    vm.GlobalError = "Error while broadcasting: " + ex.Message;
                    return View(vm);
                }
                return await RedirectToWalletTransaction(walletId, transaction);
            }
            else if (command == "analyze-psbt")
            {
                return RedirectToAction(nameof(WalletPSBT), new { walletId = walletId, psbt = psbt.ToBase64() });
            }
            else
            {
                vm.GlobalError = "Unknown command";
                return View(vm);
            }
        }

        private IActionResult ViewPSBT<T>(PSBT psbt, IEnumerable<T> errors = null)
        {
            return ViewPSBT(psbt, null, errors?.Select(e => e.ToString()).ToList());
        }
        private IActionResult ViewPSBT(PSBT psbt, IEnumerable<string> errors = null)
        {
            return ViewPSBT(psbt, null, errors);
        }
        private IActionResult ViewPSBT(PSBT psbt, string fileName, IEnumerable<string> errors = null)
        {
            ModelState.Remove(nameof(WalletPSBTViewModel.PSBT));
            ModelState.Remove(nameof(WalletPSBTViewModel.FileName));
            return View(nameof(WalletPSBT), new WalletPSBTViewModel()
            {
                Decoded = psbt.ToString(),
                FileName = fileName ?? string.Empty,
                PSBT = psbt.ToBase64(),
                Errors = errors?.ToList()
            });
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
            StatusMessage = "PSBT Successfully combined!";
            return ViewPSBT(sourcePSBT);
        }
    }
}
