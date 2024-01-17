using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using NBXplorer.Models;

namespace BTCPayServer.Controllers.Greenfield
{
    public partial class GreenfieldStoreOnChainPaymentMethodsController
    {
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}/generate")]
        [EnableCors(CorsPolicies.All)]
        public async Task<IActionResult> GenerateOnChainWallet(string storeId, string cryptoCode,
            GenerateWalletRequest request)
        {

            AssertCryptoCodeWallet(cryptoCode, out var network, out var wallet);

            if (!_walletProvider.IsAvailable(network))
            {
                return this.CreateAPIError(503, "not-available",
                    $"{cryptoCode} services are not currently available");
            }

            var method = GetExistingBtcLikePaymentMethod(cryptoCode);
            if (method != null)
            {
                return this.CreateAPIError("already-configured",
                    $"{cryptoCode} wallet is already configured for this store");
            }

            var canUseHotWallet = await CanUseHotWallet();
            if (request.SavePrivateKeys && !canUseHotWallet.HotWallet)
            {
                ModelState.AddModelError(nameof(request.SavePrivateKeys),
                    "This instance forbids non-admins from having a hot wallet for your store.");
            }

            if (request.ImportKeysToRPC && !canUseHotWallet.RPCImport)
            {
                ModelState.AddModelError(nameof(request.ImportKeysToRPC),
                    "This instance forbids non-admins from having importing the wallet addresses/keys to the underlying node.");
            }

            if (!ModelState.IsValid)
            {
                return this.CreateValidationError(ModelState);
            }

            var client = _explorerClientProvider.GetExplorerClient(network);
            GenerateWalletResponse response;
            try
            {
                response = await client.GenerateWalletAsync(request);
                if (response == null)
                {
                    return this.CreateAPIError(503, "not-available",
                        $"{cryptoCode} services are not currently available");
                }
            }
            catch (Exception e)
            {
                return this.CreateAPIError(503, "not-available",
                    $"{cryptoCode} error: {e.Message}");
            }

            var derivationSchemeSettings = new DerivationSchemeSettings(response.DerivationScheme, network);

            derivationSchemeSettings.Source =
                string.IsNullOrEmpty(request.ExistingMnemonic) ? "NBXplorerGenerated" : "ImportedSeed";
            derivationSchemeSettings.IsHotWallet = request.SavePrivateKeys;

            var accountSettings = derivationSchemeSettings.GetSigningAccountKeySettings();
            accountSettings.AccountKeyPath = response.AccountKeyPath.KeyPath;
            accountSettings.RootFingerprint = response.AccountKeyPath.MasterFingerprint;
            derivationSchemeSettings.AccountOriginal = response.DerivationScheme.ToString();

            var store = Store;
            var storeBlob = store.GetStoreBlob();
            store.SetSupportedPaymentMethod(new PaymentMethodId(cryptoCode, PaymentTypes.BTCLike),
                derivationSchemeSettings);
            store.SetStoreBlob(storeBlob);
            await _storeRepository.UpdateStore(store);
            var rawResult = GetExistingBtcLikePaymentMethod(cryptoCode, store);
            var result = new OnChainPaymentMethodDataWithSensitiveData(rawResult.CryptoCode, rawResult.DerivationScheme,
                rawResult.Enabled, rawResult.Label, rawResult.AccountKeyPath, response.GetMnemonic(), derivationSchemeSettings.PaymentId.ToStringNormalized());
            _eventAggregator.Publish(new WalletChangedEvent()
            {
                WalletId = new WalletId(storeId, cryptoCode)
            });
            return Ok(result);
        }

        private async Task<(bool HotWallet, bool RPCImport)> CanUseHotWallet()
        {
            return await _authorizationService.CanUseHotWallet(PoliciesSettings, User);
        }
    }
}
