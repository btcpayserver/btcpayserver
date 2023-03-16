using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers.Greenfield
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public partial class GreenfieldStoreOnChainPaymentMethodsController : ControllerBase
    {
        private StoreData Store => HttpContext.GetStoreData();

        public PoliciesSettings PoliciesSettings { get; }

        private readonly StoreRepository _storeRepository;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly BTCPayWalletProvider _walletProvider;
        private readonly IAuthorizationService _authorizationService;
        private readonly ExplorerClientProvider _explorerClientProvider;
        private readonly EventAggregator _eventAggregator;

        public GreenfieldStoreOnChainPaymentMethodsController(
            StoreRepository storeRepository,
            BTCPayNetworkProvider btcPayNetworkProvider,
            BTCPayWalletProvider walletProvider,
            IAuthorizationService authorizationService,
            ExplorerClientProvider explorerClientProvider,
            PoliciesSettings policiesSettings,
            EventAggregator eventAggregator)
        {
            _storeRepository = storeRepository;
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _walletProvider = walletProvider;
            _authorizationService = authorizationService;
            _explorerClientProvider = explorerClientProvider;
            _eventAggregator = eventAggregator;
            PoliciesSettings = policiesSettings;
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
                        strategy.AccountDerivation.ToString(), !excludedPaymentMethods.Match(strategy.PaymentId),
                        strategy.Label, strategy.GetSigningAccountKeySettings().GetRootedKeyPath(),
                        strategy.PaymentId.ToStringNormalized()))
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
            AssertCryptoCodeWallet(cryptoCode, out BTCPayNetwork _, out BTCPayWallet _);
            var method = GetExistingBtcLikePaymentMethod(cryptoCode);
            if (method is null)
            {
                throw ErrorPaymentMethodNotConfigured();
            }

            return Ok(method);
        }

        protected JsonHttpException ErrorPaymentMethodNotConfigured()
        {
            return new JsonHttpException(this.CreateAPIError(404, "paymentmethod-not-configured", "The lightning node is not set up"));
        }

        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}/preview")]
        public IActionResult GetOnChainPaymentMethodPreview(
            string storeId,
            string cryptoCode,
            int offset = 0, int amount = 10)
        {
            AssertCryptoCodeWallet(cryptoCode, out var network, out _);

            var paymentMethod = GetExistingBtcLikePaymentMethod(cryptoCode);
            if (string.IsNullOrEmpty(paymentMethod?.DerivationScheme))
            {
                throw ErrorPaymentMethodNotConfigured();
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
            [FromBody] UpdateOnChainPaymentMethodRequest paymentMethodData,
            int offset = 0, int amount = 10)
        {
            AssertCryptoCodeWallet(cryptoCode, out var network, out _);

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
            AssertCryptoCodeWallet(cryptoCode, out _, out _);

            var id = new PaymentMethodId(cryptoCode, PaymentTypes.BTCLike);
            var store = Store;
            store.SetSupportedPaymentMethod(id, null);
            await _storeRepository.UpdateStore(store);
            _eventAggregator.Publish(new WalletChangedEvent()
            {
                WalletId = new WalletId(storeId, cryptoCode)
            });
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
            AssertCryptoCodeWallet(cryptoCode, out var network, out var wallet);

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
                _eventAggregator.Publish(new WalletChangedEvent()
                {
                    WalletId = new WalletId(storeId, cryptoCode)
                });
                return Ok(GetExistingBtcLikePaymentMethod(cryptoCode, store));
            }
            catch
            {
                ModelState.AddModelError(nameof(OnChainPaymentMethodData.DerivationScheme),
                    "Invalid Derivation Scheme");
                return this.CreateValidationError(ModelState);
            }
        }

        private void AssertCryptoCodeWallet(string cryptoCode, out BTCPayNetwork network, out BTCPayWallet wallet)
        {
            network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
            if (network is null)
                throw new JsonHttpException(this.CreateAPIError(404, "unknown-cryptocode", "This crypto code isn't set up in this BTCPay Server instance"));

            wallet = _walletProvider.GetWallet(network);
            if (wallet is null)
                throw ErrorPaymentMethodNotConfigured();
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
                    paymentMethod.GetSigningAccountKeySettings().GetRootedKeyPath(),
                    paymentMethod.PaymentId.ToStringNormalized());
        }
    }
}
