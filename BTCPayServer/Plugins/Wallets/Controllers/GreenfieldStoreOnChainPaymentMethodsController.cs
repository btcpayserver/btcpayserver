using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.ModelBinders;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Plugins.Wallets;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBXplorer.DerivationStrategy;
using Newtonsoft.Json.Linq;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Controllers.Greenfield
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [EnableCors(CorsPolicies.All)]
    public partial class GreenfieldStoreOnChainPaymentMethodsController(
        StoreRepository storeRepository,
        BTCPayWalletProvider walletProvider,
        IAuthorizationService authorizationService,
        ExplorerClientProvider explorerClientProvider,
        PoliciesSettings policiesSettings,
        PaymentMethodHandlerDictionary handlers,
        EventAggregator eventAggregator)
        : ControllerBase
    {
        private StoreData Store => HttpContext.GetStoreData();

        public PoliciesSettings PoliciesSettings { get; } = policiesSettings;

        protected JsonHttpException ErrorPaymentMethodNotConfigured()
        {
            return new JsonHttpException(this.CreateAPIError(404, "paymentmethod-not-configured", "The payment method is not configured"));
        }

        [Authorize(Policy = WalletPolicies.CanViewWallet, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/payment-methods/{paymentMethodId}/wallet/preview")]
        public IActionResult GetOnChainPaymentMethodPreview(
            string storeId,
            [ModelBinder(typeof(PaymentMethodIdModelBinder))]
            PaymentMethodId paymentMethodId,
            int offset = 0, int count = 10)
        {
            AssertCryptoCodeWallet(paymentMethodId, out var network, out _);
            if (!IsConfigured(paymentMethodId, out var settings))
            {
                throw ErrorPaymentMethodNotConfigured();
            }
            return Ok(GetPreviewResultData(offset, count, network, settings.AccountDerivation));
        }


        [Authorize(Policy = WalletPolicies.CanManageWalletSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/payment-methods/{paymentMethodId}/wallet/preview")]
        public async Task<IActionResult> GetProposedOnChainPaymentMethodPreview(
            string storeId,
            [ModelBinder(typeof(PaymentMethodIdModelBinder))]
            PaymentMethodId paymentMethodId,
            [FromBody] UpdatePaymentMethodRequest request = null,
            int offset = 0, int count = 10)
        {
            if (request is null)
            {
                ModelState.AddModelError(nameof(request), "Missing body");
                return this.CreateValidationError(ModelState);
            }
            if (request.Config is null)
            {
                ModelState.AddModelError(nameof(request.Config), "Missing config");
                return this.CreateValidationError(ModelState);
            }
            AssertCryptoCodeWallet(paymentMethodId, out var network, out _);

            var handler = handlers.GetBitcoinHandler(network);
            var ctx = new PaymentMethodConfigValidationContext(authorizationService, ModelState, request.Config, User, Store.GetPaymentMethodConfig(paymentMethodId));
            await handler.ValidatePaymentMethodConfig(ctx);
            if (ctx.MissingPermission is not null)
            {
                return this.CreateAPIPermissionError(ctx.MissingPermission.Permission, ctx.MissingPermission.Message);
            }
            if (!ModelState.IsValid)
                return this.CreateValidationError(ModelState);

            var settings = handler.ParsePaymentMethodConfig(ctx.Config);
            var result = GetPreviewResultData(offset, count, network, settings.AccountDerivation);
            return Ok(result);
        }

        internal static OnChainPaymentMethodPreviewResultData GetPreviewResultData(int offset, int count, BTCPayNetwork network, DerivationStrategyBase strategy)
        {
            var line = strategy.GetLineFor(DerivationFeature.Deposit);
            var result = new OnChainPaymentMethodPreviewResultData();
            for (var i = offset; i < count; i++)
            {
                var keyPath = new KeyPath(0, (uint)i);
                if (strategy is PolicyDerivationStrategy)
                    keyPath = null;
                var derivation = line.Derive((uint)i);
                result.Addresses.Add(
                    new()
                    {
                        KeyPath = keyPath?.ToString(),
                        Index = i,
                        Address =
#pragma warning disable CS0612 // Type or member is obsolete
                            // We should be able to derive the address from the scriptPubKey.
                            // However, Elements has blinded addresses, so we can't derive the address from the scriptPubKey.
                            // We should probably just use a special if/else just for elements here instead of relying on obsolete stuff.
                            network.NBXplorerNetwork.CreateAddress(strategy, keyPath ?? new(), derivation.ScriptPubKey)
#pragma warning restore CS0612 // Type or member is obsolete
                                .ToString()
                    });
            }
            return result;
        }

        private void AssertCryptoCodeWallet(PaymentMethodId paymentMethodId, out BTCPayNetwork network, out BTCPayWallet wallet)
        {
            if (!handlers.TryGetValue(paymentMethodId, out var h) || h is not BitcoinLikePaymentHandler handler)
                throw new JsonHttpException(this.CreateAPIError(404, "unknown-paymentMethodId", "This payment method id isn't set up in this BTCPay Server instance"));
            network = handler.Network;
            wallet = walletProvider.GetWallet(network);
            if (wallet is null)
                throw ErrorPaymentMethodNotConfigured();
        }

        bool IsConfigured(PaymentMethodId paymentMethodId, [MaybeNullWhen(false)] out DerivationSchemeSettings settings)
        {
            var store = Store;
            var conf = store.GetPaymentMethodConfig(paymentMethodId);
            settings = null;
            if (conf is (null or { Type: JTokenType.Null }))
                return false;
            settings = ((BitcoinLikePaymentHandler)handlers[paymentMethodId]).ParsePaymentMethodConfig(conf);
            return settings?.AccountDerivation is not null;
        }
    }
}
