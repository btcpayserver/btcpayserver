using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.ModelBinders;
using BTCPayServer.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using NBXplorer.Models;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Controllers.Greenfield
{
    public partial class GreenfieldStoreOnChainPaymentMethodsController
    {
        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/payment-methods/{paymentMethodId}/generate")]
        [EnableCors(CorsPolicies.All)]
        public async Task<IActionResult> GenerateOnChainWallet(string storeId,
            [ModelBinder(typeof(PaymentMethodIdModelBinder))]
            PaymentMethodId paymentMethodId,
            GenerateWalletRequest request)
        {

            AssertCryptoCodeWallet(paymentMethodId, out var network, out _);

            if (!_walletProvider.IsAvailable(network))
            {
                return this.CreateAPIError(503, "not-available",
                    $"{paymentMethodId} services are not currently available");
            }

            if (IsConfigured(paymentMethodId, out _))
            {
                return this.CreateAPIError("already-configured",
                    $"{paymentMethodId} wallet is already configured for this store");
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
                        $"{paymentMethodId} services are not currently available");
                }
            }
            catch (Exception e)
            {
                return this.CreateAPIError(503, "not-available",
                    $"{paymentMethodId} error: {e.Message}");
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
            var handler = _handlers[paymentMethodId];
            store.SetPaymentMethodConfig(_handlers[paymentMethodId],
                derivationSchemeSettings);
            store.SetStoreBlob(storeBlob);
            await _storeRepository.UpdateStore(store);
            
            var result = new GenerateOnChainWalletResponse()
            {
                Enabled = !storeBlob.IsExcluded(paymentMethodId),
                PaymentMethodId = paymentMethodId.ToString(),
                Config = ((JObject)JToken.FromObject(derivationSchemeSettings, handler.Serializer.ForAPI())).ToObject<GenerateOnChainWalletResponse.ConfigData>(handler.Serializer.ForAPI())
            };
            result.Mnemonic = response.GetMnemonic();
            _eventAggregator.Publish(new WalletChangedEvent()
            {
                WalletId = new WalletId(storeId, network.CryptoCode)
            });
            return Ok(result);
        }

        private async Task<(bool HotWallet, bool RPCImport)> CanUseHotWallet()
        {
            return await _authorizationService.CanUseHotWallet(PoliciesSettings, User);
        }
    }
}
