using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Security;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NBXplorer.DerivationStrategy;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers.GreenField
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public class StoreBtcLikePaymentMethodController : ControllerBase
    {
        private StoreData Store => HttpContext.GetStoreData();
        private readonly StoreRepository _storeRepository;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly BTCPayWalletProvider _walletProvider;

        public StoreBtcLikePaymentMethodController(
            StoreRepository storeRepository,
            BTCPayNetworkProvider btcPayNetworkProvider,
            BTCPayWalletProvider walletProvider)
        {
            _storeRepository = storeRepository;
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _walletProvider = walletProvider;
        }

        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/" + nameof(PaymentTypes.BTCLike.Id))]
        public ActionResult<IEnumerable<StoreBtcLikePaymentMethodData>> GetBtcLikePaymentMethods(
            [FromQuery] bool enabledOnly = false)
        {
            var blob = Store.GetStoreBlob();
            var excludedPaymentMethods = blob.GetExcludedPaymentMethods();
            var defaultPaymentId = Store.GetDefaultPaymentId(_btcPayNetworkProvider);
            return Ok(Store.GetSupportedPaymentMethods(_btcPayNetworkProvider)
                .Where((method) => method.PaymentId.PaymentType == PaymentTypes.BTCLike)
                .OfType<DerivationSchemeSettings>()
                .Select(strategy =>
                    new StoreBtcLikePaymentMethodData(strategy.PaymentId.CryptoCode,
                        strategy.AccountDerivation.ToString(), !excludedPaymentMethods.Match(strategy.PaymentId),
                        defaultPaymentId == strategy.PaymentId))
                .Where((result) => !enabledOnly || result.Enabled)
                .ToList()
            );
        }

        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/" + nameof(PaymentTypes.BTCLike.Id) + "/{cryptoCode}")]
        public ActionResult<StoreBtcLikePaymentMethodData> GetBtcLikePaymentMethod(string cryptoCode)
        {
            if (!GetCryptoCodeWallet(cryptoCode, out var network, out var wallet))
            {
                return NotFound();
            }

            return Ok(GetExistingBtcLikePaymentMethod(cryptoCode));
        }

        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/" + nameof(PaymentTypes.BTCLike.Id) +
                 "/{cryptoCode}/preview")]
        public ActionResult<StoreBtcLikePaymentMethodPreviewResultData> GetBtcLikePaymentAddressPreview(
            string cryptoCode,
            int offset = 0, int amount = 10)
        {
            if (!GetCryptoCodeWallet(cryptoCode, out var network, out var wallet))
            {
                return NotFound();
            }

            var paymentMethod = GetExistingBtcLikePaymentMethod(cryptoCode);
            if (string.IsNullOrEmpty(paymentMethod.DerivationScheme))
            {
                return BadRequest();
            }

            var strategy = DerivationSchemeSettings.Parse(paymentMethod.DerivationScheme, network);
            var deposit = new NBXplorer.KeyPathTemplates(null).GetKeyPathTemplate(DerivationFeature.Deposit);

            var line = strategy.AccountDerivation.GetLineFor(deposit);
            var result = new StoreBtcLikePaymentMethodPreviewResultData();
            for (var i = offset; i < amount; i++)
            {
                var address = line.Derive((uint)i);
                result.Addresses.Add(
                    new StoreBtcLikePaymentMethodPreviewResultData.StoreBtcLikePaymentMethodPreviewResultAddressItem()
                    {
                        KeyPath = deposit.GetKeyPath((uint)i).ToString(),
                        Address = address.ScriptPubKey.GetDestinationAddress(strategy.Network.NBitcoinNetwork)
                            .ToString()
                    });
            }

            return Ok(result);
        }

        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/payment-methods/" + nameof(PaymentTypes.BTCLike.Id) +
                  "/{cryptoCode}/preview")]
        public IActionResult GetBtcLikePaymentAddressPreview(string cryptoCode,
            [FromBody] StoreBtcLikePaymentMethodData paymentMethodData,
            int offset = 0, int amount = 10)
        {
            if (!GetCryptoCodeWallet(cryptoCode, out var network, out var wallet))
            {
                return NotFound();
            }

            try
            {
                var strategy = DerivationSchemeSettings.Parse(paymentMethodData.DerivationScheme, network);
                var deposit = new NBXplorer.KeyPathTemplates(null).GetKeyPathTemplate(DerivationFeature.Deposit);
                var line = strategy.AccountDerivation.GetLineFor(deposit);
                var result = new StoreBtcLikePaymentMethodPreviewResultData();
                for (var i = offset; i < amount; i++)
                {
                    var derivation = line.Derive((uint)i);
                    result.Addresses.Add(
                        new
                            StoreBtcLikePaymentMethodPreviewResultData.
                            StoreBtcLikePaymentMethodPreviewResultAddressItem()
                            {
                                KeyPath = deposit.GetKeyPath((uint)i).ToString(),
                                Address = strategy.Network.NBXplorerNetwork.CreateAddress(strategy.AccountDerivation,
                                    line.KeyPathTemplate.GetKeyPath((uint)i),
                                    derivation.ScriptPubKey).ToString()
                            });
                }

                return Ok(result);
            }

            catch
            {
                ModelState.AddModelError(nameof(StoreBtcLikePaymentMethodData.DerivationScheme),
                    "Invalid Derivation Scheme");
                return this.CreateValidationError(ModelState);
            }
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPut("~/api/v1/stores/{storeId}/payment-methods/" + nameof(PaymentTypes.BTCLike.Id) + "/{cryptoCode}")]
        public async Task<IActionResult> UpdateBtcLikePaymentMethod(string cryptoCode,
            [FromBody] StoreBtcLikePaymentMethodData paymentMethodData)
        {
            var id = new PaymentMethodId(cryptoCode, PaymentTypes.BTCLike);

            if (!GetCryptoCodeWallet(cryptoCode, out var network, out var wallet))
            {
                return NotFound();
            }

            try
            {
                var store = Store;
                var storeBlob = store.GetStoreBlob();
                var strategy = DerivationSchemeSettings.Parse(paymentMethodData.DerivationScheme, network);
                if (strategy != null)
                    await wallet.TrackAsync(strategy.AccountDerivation);
                store.SetSupportedPaymentMethod(id, strategy);
                storeBlob.SetExcluded(id, !paymentMethodData.Enabled);
                store.SetStoreBlob(storeBlob);
                await _storeRepository.UpdateStore(store);
                return Ok(GetExistingBtcLikePaymentMethod(cryptoCode, store));
            }
            catch
            {
                ModelState.AddModelError(nameof(StoreBtcLikePaymentMethodData.DerivationScheme),
                    "Invalid Derivation Scheme");
                return this.CreateValidationError(ModelState);
            }
        }

        private bool GetCryptoCodeWallet(string cryptoCode, out BTCPayNetwork network, out BTCPayWallet wallet)
        {
            network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
            wallet = network != null ? _walletProvider.GetWallet(network) : null;
            return wallet != null;
        }

        private StoreBtcLikePaymentMethodData GetExistingBtcLikePaymentMethod(string cryptoCode, StoreData store = null)
        {
            store ??= Store;
            var storeBlob = store.GetStoreBlob();
            var defaultPaymentMethod = store.GetDefaultPaymentId(_btcPayNetworkProvider);
            var id = new PaymentMethodId(cryptoCode, PaymentTypes.BTCLike);
            var paymentMethod = store
                .GetSupportedPaymentMethods(_btcPayNetworkProvider)
                .OfType<DerivationSchemeSettings>()
                .FirstOrDefault(method => method.PaymentId == id);

            var excluded = storeBlob.IsExcluded(id);
            return paymentMethod == null
                ? new StoreBtcLikePaymentMethodData(cryptoCode, string.Empty, !excluded,
                    defaultPaymentMethod == new PaymentMethodId(cryptoCode, PaymentTypes.BTCLike))
                : new StoreBtcLikePaymentMethodData(paymentMethod.PaymentId.CryptoCode,
                    paymentMethod.AccountDerivation.ToString(), !excluded,
                    defaultPaymentMethod == paymentMethod.PaymentId);
        }
    }
}
