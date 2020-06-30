using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.HostedServices;
using BTCPayServer.Lightning;
using BTCPayServer.Security;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers.GreenField
{
    [ApiController]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [LightningUnavailableExceptionFilter]
    [EnableCors(CorsPolicies.All)]
    public class InternalLightningNodeApiController : LightningNodeApiController
    {
        private readonly BTCPayServerOptions _btcPayServerOptions;
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly LightningClientFactoryService _lightningClientFactory;


        public InternalLightningNodeApiController(BTCPayServerOptions btcPayServerOptions,
            BTCPayNetworkProvider btcPayNetworkProvider, BTCPayServerEnvironment btcPayServerEnvironment,
            CssThemeManager cssThemeManager, LightningClientFactoryService lightningClientFactory) : base(
            btcPayNetworkProvider, btcPayServerEnvironment, cssThemeManager)
        {
            _btcPayServerOptions = btcPayServerOptions;
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _lightningClientFactory = lightningClientFactory;
        }

        [Authorize(Policy = Policies.CanUseInternalLightningNode,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/server/lightning/{cryptoCode}/info")]
        public override Task<IActionResult> GetInfo(string cryptoCode)
        {
            return base.GetInfo(cryptoCode);
        }

        [Authorize(Policy = Policies.CanUseInternalLightningNode,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/server/lightning/{cryptoCode}/connect")]
        public override Task<IActionResult> ConnectToNode(string cryptoCode, ConnectToNodeRequest request)
        {
            return base.ConnectToNode(cryptoCode, request);
        }

        [Authorize(Policy = Policies.CanUseInternalLightningNode,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/server/lightning/{cryptoCode}/channels")]
        public override Task<IActionResult> GetChannels(string cryptoCode)
        {
            return base.GetChannels(cryptoCode);
        }

        [Authorize(Policy = Policies.CanUseInternalLightningNode,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/server/lightning/{cryptoCode}/channels")]
        public override Task<IActionResult> OpenChannel(string cryptoCode, OpenLightningChannelRequest request)
        {
            return base.OpenChannel(cryptoCode, request);
        }

        [Authorize(Policy = Policies.CanUseInternalLightningNode,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/server/lightning/{cryptoCode}/address")]
        public override Task<IActionResult> GetDepositAddress(string cryptoCode)
        {
            return base.GetDepositAddress(cryptoCode);
        }

        [Authorize(Policy = Policies.CanUseInternalLightningNode,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpGet("~/api/v1/server/lightning/{cryptoCode}/invoices/{id}")]
        public override Task<IActionResult> GetInvoice(string cryptoCode, string id)
        {
            return base.GetInvoice(cryptoCode, id);
        }

        [Authorize(Policy = Policies.CanUseInternalLightningNode,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/server/lightning/{cryptoCode}/invoices/pay")]
        public override Task<IActionResult> PayInvoice(string cryptoCode, PayLightningInvoiceRequest lightningInvoice)
        {
            return base.PayInvoice(cryptoCode, lightningInvoice);
        }

        [Authorize(Policy = Policies.CanCreateLightningInvoiceInternalNode,
            AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
        [HttpPost("~/api/v1/server/lightning/{cryptoCode}/invoices")]
        public override Task<IActionResult> CreateInvoice(string cryptoCode, CreateLightningInvoiceRequest request)
        {
            return base.CreateInvoice(cryptoCode, request);
        }

        protected override Task<ILightningClient> GetLightningClient(string cryptoCode, bool doingAdminThings)
        {
            _btcPayServerOptions.InternalLightningByCryptoCode.TryGetValue(cryptoCode,
                out var internalLightningNode);
            var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
            if (network == null || !CanUseInternalLightning(doingAdminThings) || internalLightningNode == null)
            {
                return null;
            }

            return Task.FromResult(_lightningClientFactory.Create(internalLightningNode, network));
        }
    }
}
