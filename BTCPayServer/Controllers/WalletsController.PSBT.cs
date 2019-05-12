using System;
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


        [HttpPost]
        [Route("{walletId}/psbt/sign")]
        public async Task<IActionResult> WalletPSBTSign(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId,
            WalletPSBTViewModel vm,
            string command = null
            )
        {
            var network = NetworkProvider.GetNetwork(walletId.CryptoCode);
            var psbt = PSBT.Parse(vm.PSBT, network.NBitcoinNetwork);
            if (command == "ledger")
            {
                return ViewWalletSendLedger(psbt);
            }
            else if (command == "broadcast")
            {
                if (!psbt.IsAllFinalized() && !psbt.TryFinalize(out var errors))
                {
                    return ViewPSBT(psbt, errors);
                }
                var transaction = psbt.ExtractTransaction();
                try
                {
                    var broadcastResult = await ExplorerClientProvider.GetExplorerClient(network).BroadcastAsync(transaction);
                    if (!broadcastResult.Success)
                    {
                        return ViewPSBT(psbt, new[] { $"RPC Error while broadcasting: {broadcastResult.RPCCode} {broadcastResult.RPCCodeMessage} {broadcastResult.RPCMessage}" });
                    }
                }
                catch (Exception ex)
                {
                    return ViewPSBT(psbt, "Error while broadcasting: " + ex.Message);
                }
                return await RedirectToWalletTransaction(walletId, transaction);
            }
            else
            {
                (await GetDerivationSchemeSettings(walletId)).RebaseKeyPaths(psbt);
                return FilePSBT(psbt, "psbt-export.psbt");
            }
        }

        [NonAction]
        public async Task<CreatePSBTResponse> CreatePSBT(BTCPayNetwork network, DerivationSchemeSettings derivationSettings, WalletSendModel sendModel, CancellationToken cancellationToken)
        {
            var nbx = ExplorerClientProvider.GetExplorerClient(network);
            CreatePSBTRequest psbtRequest = new CreatePSBTRequest();
            CreatePSBTDestination psbtDestination = new CreatePSBTDestination();
            psbtRequest.Destinations.Add(psbtDestination);
            if (network.SupportRBF)
            {
                psbtRequest.RBF = !sendModel.DisableRBF;
            }
            psbtDestination.Destination = BitcoinAddress.Create(sendModel.Destination, network.NBitcoinNetwork);
            psbtDestination.Amount = Money.Coins(sendModel.Amount.Value);
            psbtRequest.FeePreference = new FeePreference();
            psbtRequest.FeePreference.ExplicitFeeRate = new FeeRate(Money.Satoshis(sendModel.FeeSatoshiPerByte), 1);
            if (sendModel.NoChange)
            {
                psbtRequest.ExplicitChangeAddress = psbtDestination.Destination;
            }
            psbtDestination.SubstractFees = sendModel.SubstractFees;
            psbtRequest.RebaseKeyPaths = derivationSettings.GetPSBTRebaseKeyRules().ToList();
            var psbt = (await nbx.CreatePSBTAsync(derivationSettings.AccountDerivation, psbtRequest, cancellationToken));
            if (psbt == null)
                throw new NotSupportedException("You need to update your version of NBXplorer");
            return psbt;
        }

        [HttpGet]
        [Route("{walletId}/psbt")]
        public IActionResult WalletPSBT()
        {
            return View(new WalletPSBTViewModel());
        }
        [HttpPost]
        [Route("{walletId}/psbt")]
        public IActionResult WalletPSBT(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId,
            WalletPSBTViewModel vm)
        {
            try
            {
                if (!string.IsNullOrEmpty(vm.PSBT))
                    vm.Decoded = PSBT.Parse(vm.PSBT, NetworkProvider.GetNetwork(walletId.CryptoCode).NBitcoinNetwork).ToString();
            }
            catch (FormatException ex)
            {
                ModelState.AddModelError(nameof(vm.PSBT), ex.Message);
            }
            return View(vm);
        }

        [HttpGet]
        [Route("{walletId}/psbt/ready")]
        public IActionResult WalletPSBTReady(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, string psbt = null)
        {
            return View(new WalletPSBTReadyViewModel() { PSBT = psbt });
        }

        [HttpPost]
        [Route("{walletId}/psbt/ready")]
        public async Task<IActionResult> WalletPSBTReady(
            [ModelBinder(typeof(WalletIdModelBinder))]
            WalletId walletId, WalletPSBTReadyViewModel vm, string command = null)
        {
            PSBT psbt = null;
            var network = NetworkProvider.GetNetwork(walletId.CryptoCode);
            try
            {
                psbt = PSBT.Parse(vm.PSBT, network.NBitcoinNetwork);
            }
            catch
            {
                vm.Errors = new List<string>();
                vm.Errors.Add("Invalid PSBT");
                return View(vm);
            }
            if (command == "broadcast")
            {
                if (!psbt.IsAllFinalized() && !psbt.TryFinalize(out var errors))
                {
                    vm.Errors = new List<string>();
                    vm.Errors.AddRange(errors.Select(e => e.ToString()));
                    return View(vm);
                }
                var transaction = psbt.ExtractTransaction();
                try
                {
                    var broadcastResult = await ExplorerClientProvider.GetExplorerClient(network).BroadcastAsync(transaction);
                    if (!broadcastResult.Success)
                    {
                        vm.Errors = new List<string>();
                        vm.Errors.Add($"RPC Error while broadcasting: {broadcastResult.RPCCode} {broadcastResult.RPCCodeMessage} {broadcastResult.RPCMessage}");
                        return View(vm);
                    }
                }
                catch (Exception ex)
                {
                    vm.Errors = new List<string>();
                    vm.Errors.Add("Error while broadcasting: " + ex.Message);
                    return View(vm);
                }
                return await RedirectToWalletTransaction(walletId, transaction);
            }
            else if (command == "analyze-psbt")
            {
                return ViewPSBT(psbt);
            }
            else
            {
                vm.Errors = new List<string>();
                vm.Errors.Add("Unknown command");
                return View(vm);
            }
        }

        private IActionResult ViewPSBT<T>(PSBT psbt, IEnumerable<T> errors = null)
        {
            return ViewPSBT(psbt, errors?.Select(e => e.ToString()).ToList());
        }
        private IActionResult ViewPSBT(PSBT psbt, IEnumerable<string> errors = null)
        {
            return View(nameof(WalletPSBT), new WalletPSBTViewModel()
            {
                Decoded = psbt.ToString(),
                PSBT = psbt.ToBase64(),
                Errors = errors?.ToList()
            });
        }

        private IActionResult FilePSBT(PSBT psbt, string fileName)
        {
            return File(psbt.ToBytes(), "application/octet-stream", fileName);
        }
    }
}
