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
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using NBXplorer.DerivationStrategy;
using Newtonsoft.Json.Linq;
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
        private readonly BTCPayWalletProvider _walletProvider;
        private readonly IAuthorizationService _authorizationService;
        private readonly ExplorerClientProvider _explorerClientProvider;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly EventAggregator _eventAggregator;

        public GreenfieldStoreOnChainPaymentMethodsController(
            StoreRepository storeRepository,
            BTCPayWalletProvider walletProvider,
            IAuthorizationService authorizationService,
            ExplorerClientProvider explorerClientProvider,
            PoliciesSettings policiesSettings,
            PaymentMethodHandlerDictionary handlers,
            EventAggregator eventAggregator)
        {
            _storeRepository = storeRepository;
            _walletProvider = walletProvider;
            _authorizationService = authorizationService;
            _explorerClientProvider = explorerClientProvider;
            _eventAggregator = eventAggregator;
            PoliciesSettings = policiesSettings;
            _handlers = handlers;
        }

        protected JsonHttpException ErrorPaymentMethodNotConfigured()
        {
            return new JsonHttpException(this.CreateAPIError(404, "paymentmethod-not-configured", "The payment method is not configured"));
        }

        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
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


        [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
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

            var handler = _handlers.GetBitcoinHandler(network);
            var ctx = new PaymentMethodConfigValidationContext(_authorizationService, ModelState, request.Config, User, Store.GetPaymentMethodConfig(paymentMethodId));
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

        private static OnChainPaymentMethodPreviewResultData GetPreviewResultData(int offset, int count, BTCPayNetwork network, DerivationStrategyBase strategy)
        {
            var deposit = new NBXplorer.KeyPathTemplates(null).GetKeyPathTemplate(DerivationFeature.Deposit);
            var line = strategy.GetLineFor(deposit);
            var result = new OnChainPaymentMethodPreviewResultData();
            for (var i = offset; i < count; i++)
            {
                var derivation = line.Derive((uint)i);
                result.Addresses.Add(
                    new
                        OnChainPaymentMethodPreviewResultData.
                        OnChainPaymentMethodPreviewResultAddressItem()
                    {
                        KeyPath = deposit.GetKeyPath((uint)i).ToString(),
                        Address =
                            network.NBXplorerNetwork.CreateAddress(strategy, deposit.GetKeyPath((uint)i), derivation.ScriptPubKey)
                                .ToString()
                    });
            }
            return result;
        }

        private void AssertCryptoCodeWallet(PaymentMethodId paymentMethodId, out BTCPayNetwork network, out BTCPayWallet wallet)
        {
            if (!_handlers.TryGetValue(paymentMethodId, out var h) || h is not BitcoinLikePaymentHandler handler)
                throw new JsonHttpException(this.CreateAPIError(404, "unknown-paymentMethodId", "This payment method id isn't set up in this BTCPay Server instance"));
            network = handler.Network;
            wallet = _walletProvider.GetWallet(network);
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
            settings = ((BitcoinLikePaymentHandler)_handlers[paymentMethodId]).ParsePaymentMethodConfig(conf);
            return settings?.AccountDerivation is not null;
        }
    }
}
