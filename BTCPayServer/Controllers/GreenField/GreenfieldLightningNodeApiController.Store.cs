using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Controllers.Greenfield
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [LightningUnavailableExceptionFilter]
    [EnableCors(CorsPolicies.All)]
    public class GreenfieldStoreLightningNodeApiController : GreenfieldLightningNodeApiController
    {
        private readonly IOptions<LightningNetworkOptions> _lightningNetworkOptions;
        private readonly LightningClientFactoryService _lightningClientFactory;
        private readonly PaymentMethodHandlerDictionary _handlers;

        public GreenfieldStoreLightningNodeApiController(
            IOptions<LightningNetworkOptions> lightningNetworkOptions,
            LightningClientFactoryService lightningClientFactory, PaymentMethodHandlerDictionary handlers,
            PoliciesSettings policiesSettings,
            IAuthorizationService authorizationService) : base(policiesSettings, authorizationService, handlers)
        {
            _lightningNetworkOptions = lightningNetworkOptions;
            _lightningClientFactory = lightningClientFactory;
            _handlers = handlers;
        }

        [Authorize(Policy = Policies.CanUseLightningNodeInStore,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/lightning/{cryptoCode}/info")]
        public override Task<IActionResult> GetInfo(string cryptoCode, CancellationToken cancellationToken = default)
        {
            return base.GetInfo(cryptoCode, cancellationToken);
        }

        [Authorize(Policy = Policies.CanUseLightningNodeInStore,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/lightning/{cryptoCode}/balance")]
        public override Task<IActionResult> GetBalance(string cryptoCode, CancellationToken cancellationToken = default)
        {
            return base.GetBalance(cryptoCode, cancellationToken);
        }

        [Authorize(Policy = Policies.CanUseLightningNodeInStore,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/lightning/{cryptoCode}/connect")]
        public override Task<IActionResult> ConnectToNode(string cryptoCode, ConnectToNodeRequest request, CancellationToken cancellationToken = default)
        {
            return base.ConnectToNode(cryptoCode, request, cancellationToken);
        }
        [Authorize(Policy = Policies.CanUseLightningNodeInStore,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/lightning/{cryptoCode}/channels")]
        public override Task<IActionResult> GetChannels(string cryptoCode, CancellationToken cancellationToken = default)
        {
            return base.GetChannels(cryptoCode, cancellationToken);
        }
        [Authorize(Policy = Policies.CanUseLightningNodeInStore,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/lightning/{cryptoCode}/channels")]
        public override Task<IActionResult> OpenChannel(string cryptoCode, OpenLightningChannelRequest request, CancellationToken cancellationToken = default)
        {
            return base.OpenChannel(cryptoCode, request, cancellationToken);
        }

        [Authorize(Policy = Policies.CanUseLightningNodeInStore,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/lightning/{cryptoCode}/address")]
        public override Task<IActionResult> GetDepositAddress(string cryptoCode, CancellationToken cancellationToken = default)
        {
            return base.GetDepositAddress(cryptoCode, cancellationToken);
        }

        [Authorize(Policy = Policies.CanUseLightningNodeInStore,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/lightning/{cryptoCode}/payments/{paymentHash}")]
        public override Task<IActionResult> GetPayment(string cryptoCode, string paymentHash, CancellationToken cancellationToken = default)
        {
            return base.GetPayment(cryptoCode, paymentHash, cancellationToken);
        }

        [Authorize(Policy = Policies.CanUseLightningNodeInStore,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/lightning/{cryptoCode}/invoices/pay")]
        public override Task<IActionResult> PayInvoice(string cryptoCode, PayLightningInvoiceRequest lightningInvoice, CancellationToken cancellationToken = default)
        {
            return base.PayInvoice(cryptoCode, lightningInvoice, cancellationToken);
        }

        [Authorize(Policy = Policies.CanViewLightningInvoiceInStore,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/lightning/{cryptoCode}/invoices/{id}")]
        public override Task<IActionResult> GetInvoice(string cryptoCode, string id, CancellationToken cancellationToken = default)
        {
            return base.GetInvoice(cryptoCode, id, cancellationToken);
        }

        [Authorize(Policy = Policies.CanViewLightningInvoiceInStore,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/lightning/{cryptoCode}/invoices")]
        public override Task<IActionResult> GetInvoices(string cryptoCode, [FromQuery] bool? pendingOnly, [FromQuery] long? offsetIndex, CancellationToken cancellationToken = default)
        {
            return base.GetInvoices(cryptoCode, pendingOnly, offsetIndex, cancellationToken);
        }

        [Authorize(Policy = Policies.CanCreateLightningInvoiceInStore,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/stores/{storeId}/lightning/{cryptoCode}/invoices")]
        public override Task<IActionResult> CreateInvoice(string cryptoCode, CreateLightningInvoiceRequest request, CancellationToken cancellationToken = default)
        {
            return base.CreateInvoice(cryptoCode, request, cancellationToken);
        }

        [Authorize(Policy = Policies.CanUseLightningNodeInStore,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/stores/{storeId}/lightning/{cryptoCode}/payments")]
        public override Task<IActionResult> GetPayments(string cryptoCode, [FromQuery] bool? includePending, [FromQuery] long? offsetIndex, CancellationToken cancellationToken = default)
        {
            return base.GetPayments(cryptoCode, includePending, offsetIndex, cancellationToken);
        }

        protected override Task<ILightningClient> GetLightningClient(string cryptoCode,
            bool doingAdminThings)
        {
            if (!_handlers.TryGetValue(PaymentTypes.LN.GetPaymentMethodId(cryptoCode), out var o) ||
                o is not LightningLikePaymentHandler handler)
            {
                throw ErrorCryptoCodeNotFound();
            }
            var network = handler.Network;
            var store = HttpContext.GetStoreData();
            if (store == null)
            {
                throw new JsonHttpException(StoreNotFound());
            }

            var id = PaymentTypes.LN.GetPaymentMethodId(cryptoCode);
            var existing = store.GetPaymentMethodConfig<LightningPaymentMethodConfig>(id, _handlers);
            if (existing == null)
                throw ErrorLightningNodeNotConfiguredForStore();
            if (existing.GetExternalLightningUrl() is {} connectionString)
            {
                return Task.FromResult(_lightningClientFactory.Create(connectionString, network));
            }
            else if (existing.IsInternalNode &&
            _lightningNetworkOptions.Value.InternalLightningByCryptoCode.TryGetValue(network.CryptoCode,
            out var internalLightningNode))
            {
                if (!User.IsInRole(Roles.ServerAdmin) && doingAdminThings)
                {
                    throw ErrorShouldBeAdminForInternalNode();
                }
                return Task.FromResult(internalLightningNode);
            }
            throw ErrorLightningNodeNotConfiguredForStore();
        }

        private IActionResult StoreNotFound()
        {
            return this.CreateAPIError(404, "store-not-found", "The store was not found");
        }
    }
}
