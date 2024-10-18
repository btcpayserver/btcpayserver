using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Lightning;
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
    public class GreenfieldInternalLightningNodeApiController : GreenfieldLightningNodeApiController
    {
        private readonly LightningClientFactoryService _lightningClientFactory;
        private readonly IOptions<LightningNetworkOptions> _lightningNetworkOptions;
        private readonly PaymentMethodHandlerDictionary _handlers;

        public GreenfieldInternalLightningNodeApiController(
            PoliciesSettings policiesSettings, LightningClientFactoryService lightningClientFactory,
            IOptions<LightningNetworkOptions> lightningNetworkOptions,
            IAuthorizationService authorizationService,
            PaymentMethodHandlerDictionary handlers,
            LightningHistogramService lnHistogramService
            ) : base(policiesSettings, authorizationService, handlers, lnHistogramService)
        {
            _lightningClientFactory = lightningClientFactory;
            _lightningNetworkOptions = lightningNetworkOptions;
            _handlers = handlers;
        }

        [Authorize(Policy = Policies.CanUseInternalLightningNode,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/server/lightning/{cryptoCode}/info")]
        public override Task<IActionResult> GetInfo(string cryptoCode, CancellationToken cancellationToken = default)
        {
            return base.GetInfo(cryptoCode, cancellationToken);
        }

        [Authorize(Policy = Policies.CanUseInternalLightningNode,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/server/lightning/{cryptoCode}/balance")]
        public override Task<IActionResult> GetBalance(string cryptoCode, CancellationToken cancellationToken = default)
        {
            return base.GetBalance(cryptoCode, cancellationToken);
        }

        [Authorize(Policy = Policies.CanUseInternalLightningNode,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/server/lightning/{cryptoCode}/histogram")]
        public override Task<IActionResult> GetHistogram(string cryptoCode, [FromQuery] HistogramType? type = null, CancellationToken cancellationToken = default)
        {
            return base.GetHistogram(cryptoCode, type, cancellationToken);
        }

        [Authorize(Policy = Policies.CanUseInternalLightningNode,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/server/lightning/{cryptoCode}/connect")]
        public override Task<IActionResult> ConnectToNode(string cryptoCode, ConnectToNodeRequest request, CancellationToken cancellationToken = default)
        {
            return base.ConnectToNode(cryptoCode, request, cancellationToken);
        }

        [Authorize(Policy = Policies.CanUseInternalLightningNode,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/server/lightning/{cryptoCode}/channels")]
        public override Task<IActionResult> GetChannels(string cryptoCode, CancellationToken cancellationToken = default)
        {
            return base.GetChannels(cryptoCode, cancellationToken);
        }

        [Authorize(Policy = Policies.CanUseInternalLightningNode,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/server/lightning/{cryptoCode}/channels")]
        public override Task<IActionResult> OpenChannel(string cryptoCode, OpenLightningChannelRequest request, CancellationToken cancellationToken = default)
        {
            return base.OpenChannel(cryptoCode, request, cancellationToken);
        }

        [Authorize(Policy = Policies.CanUseInternalLightningNode,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/server/lightning/{cryptoCode}/address")]
        public override Task<IActionResult> GetDepositAddress(string cryptoCode, CancellationToken cancellationToken = default)
        {
            return base.GetDepositAddress(cryptoCode, cancellationToken);
        }

        [Authorize(Policy = Policies.CanUseInternalLightningNode,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/server/lightning/{cryptoCode}/payments/{paymentHash}")]
        public override Task<IActionResult> GetPayment(string cryptoCode, string paymentHash, CancellationToken cancellationToken = default)
        {
            return base.GetPayment(cryptoCode, paymentHash, cancellationToken);
        }

        [Authorize(Policy = Policies.CanViewLightningInvoiceInternalNode,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/server/lightning/{cryptoCode}/invoices/{id}")]
        public override Task<IActionResult> GetInvoice(string cryptoCode, string id, CancellationToken cancellationToken = default)
        {
            return base.GetInvoice(cryptoCode, id, cancellationToken);
        }

        [Authorize(Policy = Policies.CanViewLightningInvoiceInternalNode,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/server/lightning/{cryptoCode}/invoices")]
        public override Task<IActionResult> GetInvoices(string cryptoCode, [FromQuery] bool? pendingOnly, [FromQuery] long? offsetIndex, CancellationToken cancellationToken = default)
        {
            return base.GetInvoices(cryptoCode, pendingOnly, offsetIndex, cancellationToken);
        }

        [Authorize(Policy = Policies.CanUseInternalLightningNode,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/server/lightning/{cryptoCode}/invoices/pay")]
        public override Task<IActionResult> PayInvoice(string cryptoCode, PayLightningInvoiceRequest lightningInvoice, CancellationToken cancellationToken = default)
        {
            return base.PayInvoice(cryptoCode, lightningInvoice, cancellationToken);
        }

        [Authorize(Policy = Policies.CanCreateLightningInvoiceInternalNode,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/server/lightning/{cryptoCode}/invoices")]
        public override Task<IActionResult> CreateInvoice(string cryptoCode, CreateLightningInvoiceRequest request, CancellationToken cancellationToken = default)
        {
            return base.CreateInvoice(cryptoCode, request, cancellationToken);
        }

        [Authorize(Policy = Policies.CanUseInternalLightningNode,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/server/lightning/{cryptoCode}/payments")]
        public override Task<IActionResult> GetPayments(string cryptoCode, [FromQuery] bool? includePending, [FromQuery] long? offsetIndex, CancellationToken cancellationToken = default)
        {
            return base.GetPayments(cryptoCode, includePending, offsetIndex, cancellationToken);
        }

        protected override async Task<ILightningClient> GetLightningClient(string cryptoCode, bool doingAdminThings)
        {
            var network = GetNetwork(cryptoCode);
            if (network is null)
                throw ErrorCryptoCodeNotFound();
            if (!_lightningNetworkOptions.Value.InternalLightningByCryptoCode.TryGetValue(network.CryptoCode,
                out var internalLightningNode))
            {
                throw ErrorInternalLightningNodeNotConfigured();
            }
            if (!await CanUseInternalLightning(doingAdminThings))
            {
                throw ErrorShouldBeAdminForInternalNode();
            }

            return internalLightningNode;
        }
    }
}
