using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers.GreenField
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public partial class StoreOnChainPaymentMethodsController : ControllerBase
    {
        private StoreData Store => HttpContext.GetStoreData();
        private readonly StoreRepository _storeRepository;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly BTCPayWalletProvider _walletProvider;
        private readonly IAuthorizationService _authorizationService;
        private readonly ISettingsRepository _settingsRepository;
        private readonly ExplorerClientProvider _explorerClientProvider;

        public StoreOnChainPaymentMethodsController(
            StoreRepository storeRepository,
            BTCPayNetworkProvider btcPayNetworkProvider,
            BTCPayWalletProvider walletProvider,
            IAuthorizationService authorizationService,
            ExplorerClientProvider explorerClientProvider, ISettingsRepository settingsRepository)
        {
            _storeRepository = storeRepository;
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _walletProvider = walletProvider;
            _authorizationService = authorizationService;
            _explorerClientProvider = explorerClientProvider;
            _settingsRepository = settingsRepository;
        }

        public static IEnumerable<OnChainPaymentMethodData> GetOnChainPaymentMethods(StoreData store,
            BTCPayNetworkProvider networkProvider, bool? enabled)
        {
            var blob = store.GetStoreBlob();
            var excludedPaymentMethods = blob.GetExcludedPaymentMethods();

            return store.GetSupportedPaymentMethods(networkProvider)
                .Where((method) => method.PaymentId.PaymentType == PaymentTypes.BTCLike)
                .OfType<DerivationSchemeSettings>()
                .Select(strategy =>
                    new OnChainPaymentMethodData(strategy.PaymentId.CryptoCode,
                        strategy.AccountDerivation.ToString(), !excludedPaymentMethods.Match(strategy.PaymentId), strategy.Label, strategy.GetSigningAccountKeySettings().GetRootedKeyPath()))
                .Where((result) => enabled is null || enabled == result.Enabled)
                .ToList();
        }

        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/onchain")]
        public ActionResult<IEnumerable<OnChainPaymentMethodData>> GetOnChainPaymentMethods(
            string storeId,
            [FromQuery] bool? enabled)
        {
            return Ok(GetOnChainPaymentMethods(Store, _btcPayNetworkProvider, enabled));
        }

        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}")]
        public ActionResult<OnChainPaymentMethodData> GetOnChainPaymentMethod(
            string storeId,
            string cryptoCode)
        {
            if (!GetCryptoCodeWallet(cryptoCode, out BTCPayNetwork _, out BTCPayWallet _))
            {
                return NotFound();
            }

            var method = GetExistingBtcLikePaymentMethod(cryptoCode);
            if (method is null)
            {
                return NotFound();
            }

            return Ok(method);
        }

        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}/preview")]
        public IActionResult GetOnChainPaymentMethodPreview(
            string storeId,
            string cryptoCode,
            int offset = 0, int amount = 10)
        {
            if (!GetCryptoCodeWallet(cryptoCode, out var network, out BTCPayWallet _))
            {
                return NotFound();
            }

            var paymentMethod = GetExistingBtcLikePaymentMethod(cryptoCode);
            if (string.IsNullOrEmpty(paymentMethod?.DerivationScheme))
            {
                return NotFound();
            }

            try
            {
                var strategy = DerivationSchemeSettings.Parse(paymentMethod.DerivationScheme, network);
                var deposit = new NBXplorer.KeyPathTemplates(null).GetKeyPathTemplate(DerivationFeature.Deposit);

                var line = strategy.AccountDerivation.GetLineFor(deposit);
                var result = new OnChainPaymentMethodPreviewResultData();
                for (var i = offset; i < amount; i++)
                {
                    var address = line.Derive((uint)i);
                    result.Addresses.Add(
                        new OnChainPaymentMethodPreviewResultData.OnChainPaymentMethodPreviewResultAddressItem()
                        {
                            KeyPath = deposit.GetKeyPath((uint)i).ToString(),
                            Address = address.ScriptPubKey.GetDestinationAddress(strategy.Network.NBitcoinNetwork)
                                .ToString()
                        });
                }

                return Ok(result);
            }
            catch
            {
                ModelState.AddModelError(nameof(OnChainPaymentMethodData.DerivationScheme),
                    "Invalid Derivation Scheme");
                return this.CreateValidationError(ModelState);
            }
        }


        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}/preview")]
        public IActionResult GetProposedOnChainPaymentMethodPreview(
            string storeId,
            string cryptoCode,
            [FromBody] OnChainPaymentMethodDataPreview paymentMethodData,
            int offset = 0, int amount = 10)
        {
            if (!GetCryptoCodeWallet(cryptoCode, out var network, out BTCPayWallet _))
            {
                return NotFound();
            }

            if (string.IsNullOrEmpty(paymentMethodData?.DerivationScheme))
            {
                ModelState.AddModelError(nameof(OnChainPaymentMethodData.DerivationScheme),
                    "Missing derivationScheme");
            }

            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);
            DerivationSchemeSettings strategy;
            try
            {
                strategy = DerivationSchemeSettings.Parse(paymentMethodData.DerivationScheme, network);
            }
            catch
            {
                ModelState.AddModelError(nameof(OnChainPaymentMethodData.DerivationScheme),
                    "Invalid Derivation Scheme");
                return this.CreateValidationError(ModelState);
            }

            var deposit = new NBXplorer.KeyPathTemplates(null).GetKeyPathTemplate(DerivationFeature.Deposit);
            var line = strategy.AccountDerivation.GetLineFor(deposit);
            var result = new OnChainPaymentMethodPreviewResultData();
            for (var i = offset; i < amount; i++)
            {
                var derivation = line.Derive((uint)i);
                result.Addresses.Add(
                    new
                        OnChainPaymentMethodPreviewResultData.
                        OnChainPaymentMethodPreviewResultAddressItem()
                        {
                            KeyPath = deposit.GetKeyPath((uint)i).ToString(),
                            Address = strategy.Network.NBXplorerNetwork.CreateAddress(strategy.AccountDerivation,
                                line.KeyPathTemplate.GetKeyPath((uint)i),
                                derivation.ScriptPubKey).ToString()
                        });
            }

            return Ok(result);
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpDelete("~/api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}")]
        public async Task<IActionResult> RemoveOnChainPaymentMethod(
            string storeId,
            string cryptoCode,
            int offset = 0, int amount = 10)
        {
            if (!GetCryptoCodeWallet(cryptoCode, out BTCPayNetwork _, out BTCPayWallet _))
            {
                return NotFound();
            }

            var id = new PaymentMethodId(cryptoCode, PaymentTypes.BTCLike);
            var store = Store;
            store.SetSupportedPaymentMethod(id, null);
            await _storeRepository.UpdateStore(store);
            return Ok();
        }

        [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPut("~/api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}")]
        public async Task<IActionResult> UpdateOnChainPaymentMethod(
            string storeId,
            string cryptoCode,
            [FromBody] UpdateOnChainPaymentMethodRequest request)
        {
            var id = new PaymentMethodId(cryptoCode, PaymentTypes.BTCLike);

            if (!GetCryptoCodeWallet(cryptoCode, out var network, out var wallet))
            {
                return NotFound();
            }

            if (string.IsNullOrEmpty(request?.DerivationScheme))
            {
                ModelState.AddModelError(nameof(OnChainPaymentMethodData.DerivationScheme),
                    "Missing derivationScheme");
            }

            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);

            try
            {
                var store = Store;
                var storeBlob = store.GetStoreBlob();
                var strategy = DerivationSchemeSettings.Parse(request.DerivationScheme, network);
                if (strategy != null)
                    await wallet.TrackAsync(strategy.AccountDerivation);
                strategy.Label = request.Label;
                var signing = strategy.GetSigningAccountKeySettings();
                if (request.AccountKeyPath is RootedKeyPath r)
                {
                    signing.AccountKeyPath = r.KeyPath;
                    signing.RootFingerprint = r.MasterFingerprint;
                }
                else
                {
                    signing.AccountKeyPath = null;
                    signing.RootFingerprint = null;
                }

                store.SetSupportedPaymentMethod(id, strategy);
                storeBlob.SetExcluded(id, !request.Enabled);
                store.SetStoreBlob(storeBlob);
                await _storeRepository.UpdateStore(store);
                return Ok(GetExistingBtcLikePaymentMethod(cryptoCode, store));
            }
            catch
            {
                ModelState.AddModelError(nameof(OnChainPaymentMethodData.DerivationScheme),
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

        private OnChainPaymentMethodData GetExistingBtcLikePaymentMethod(string cryptoCode, StoreData store = null)
        {
            store ??= Store;
            var storeBlob = store.GetStoreBlob();
            var id = new PaymentMethodId(cryptoCode, PaymentTypes.BTCLike);
            var paymentMethod = store
                .GetSupportedPaymentMethods(_btcPayNetworkProvider)
                .OfType<DerivationSchemeSettings>()
                .FirstOrDefault(method => method.PaymentId == id);

            var excluded = storeBlob.IsExcluded(id);
            return paymentMethod == null
                ? null
                : new OnChainPaymentMethodData(paymentMethod.PaymentId.CryptoCode,
                    paymentMethod.AccountDerivation.ToString(), !excluded, paymentMethod.Label,
                    paymentMethod.GetSigningAccountKeySettings().GetRootedKeyPath());
        }
    }
}
