using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.HostedServices;
using BTCPayServer.Lightning;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Controllers.GreenField
{
    public class LightningUnavailableExceptionFilter : Attribute, IExceptionFilter
    {
        public void OnException(ExceptionContext context)
        {
            if (context.Exception is NBitcoin.JsonConverters.JsonObjectException jsonObject)
            {
                context.Result = new ObjectResult(new GreenfieldValidationError(jsonObject.Path, jsonObject.Message));
            }
            else
            {
                context.Result = new StatusCodeResult(503);
            }
            context.ExceptionHandled = true;
        }
    }
    public abstract class LightningNodeApiController : Controller
    {
        private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
        private readonly BTCPayServerEnvironment _btcPayServerEnvironment;
        private readonly CssThemeManager _cssThemeManager;

        protected LightningNodeApiController(BTCPayNetworkProvider btcPayNetworkProvider,
            BTCPayServerEnvironment btcPayServerEnvironment, CssThemeManager cssThemeManager)
        {
            _btcPayNetworkProvider = btcPayNetworkProvider;
            _btcPayServerEnvironment = btcPayServerEnvironment;
            _cssThemeManager = cssThemeManager;
        }

        public virtual async Task<IActionResult> GetInfo(string cryptoCode)
        {
            var lightningClient = await GetLightningClient(cryptoCode, true);
            if (lightningClient == null)
            {
                return NotFound();
            }
            var info = await lightningClient.GetInfo();
            return Ok(new LightningNodeInformationData()
            {
                BlockHeight = info.BlockHeight,
                NodeURIs = info.NodeInfoList.Select(nodeInfo => nodeInfo).ToArray()
            });
        }

        public virtual async Task<IActionResult> ConnectToNode(string cryptoCode, ConnectToNodeRequest request)
        {
            var lightningClient = await GetLightningClient(cryptoCode, true);
            if (lightningClient == null)
            {
                return NotFound();
            }

            if (request?.NodeURI is null)
            {
                ModelState.AddModelError(nameof(request.NodeURI), "A valid node info was not provided to connect to");
            }

            if (!ModelState.IsValid)
            {
                return this.CreateValidationError(ModelState);
            }

            var result = await lightningClient.ConnectTo(request.NodeURI);
            switch (result)
            {
                case ConnectionResult.Ok:
                    return Ok();
                case ConnectionResult.CouldNotConnect:
                    return this.CreateAPIError("could-not-connect", "Could not connect to the remote node");
            }

            return Ok();
        }

        public virtual async Task<IActionResult> GetChannels(string cryptoCode)
        {
            var lightningClient = await GetLightningClient(cryptoCode, true);
            if (lightningClient == null)
            {
                return NotFound();
            }

            var channels = await lightningClient.ListChannels();
            return Ok(channels.Select(channel => new LightningChannelData()
            {
                Capacity = channel.Capacity,
                ChannelPoint = channel.ChannelPoint.ToString(),
                IsActive = channel.IsActive,
                IsPublic = channel.IsPublic,
                LocalBalance = channel.LocalBalance,
                RemoteNode = channel.RemoteNode.ToString()
            }));
        }


        public virtual async Task<IActionResult> OpenChannel(string cryptoCode, OpenLightningChannelRequest request)
        {
            var lightningClient = await GetLightningClient(cryptoCode, true);
            if (lightningClient == null)
            {
                return NotFound();
            }

            if (request?.NodeURI is null)
            {
                ModelState.AddModelError(nameof(request.NodeURI),
                    "A valid node info was not provided to open a channel with");
            }

            if (request.ChannelAmount == null)
            {
                ModelState.AddModelError(nameof(request.ChannelAmount), "ChannelAmount is missing");
            }
            else if (request.ChannelAmount.Satoshi <= 0)
            {
                ModelState.AddModelError(nameof(request.ChannelAmount), "ChannelAmount must be more than 0");
            }

            if (request.FeeRate == null)
            {
                ModelState.AddModelError(nameof(request.FeeRate), "FeeRate is missing");
            }
            else if (request.FeeRate.SatoshiPerByte <= 0)
            {
                ModelState.AddModelError(nameof(request.FeeRate), "FeeRate must be more than 0");
            }

            if (!ModelState.IsValid)
            {
                return this.CreateValidationError(ModelState);
            }

            var response = await lightningClient.OpenChannel(new Lightning.OpenChannelRequest()
            {
                ChannelAmount = request.ChannelAmount,
                FeeRate = request.FeeRate,
                NodeInfo = request.NodeURI
            });

            string errorCode, errorMessage;
            switch (response.Result)
            {
                case OpenChannelResult.Ok:
                    return Ok();
                case OpenChannelResult.AlreadyExists:
                    errorCode = "channel-already-exists";
                    errorMessage = "The channel already exists";
                    break;
                case OpenChannelResult.CannotAffordFunding:
                    errorCode = "cannot-afford-funding";
                    errorMessage = "Not enough money to open a channel";
                    break;
                case OpenChannelResult.NeedMoreConf:
                    errorCode = "need-more-confirmations";
                    errorMessage = "Need to wait for more confirmations";
                    break;
                case OpenChannelResult.PeerNotConnected:
                    errorCode = "peer-not-connected";
                    errorMessage = "Not connected to peer";
                    break;
                default:
                    throw new NotSupportedException("Unknown OpenChannelResult");
            }
            return this.CreateAPIError(errorCode, errorMessage);
        }

        public virtual async Task<IActionResult> GetDepositAddress(string cryptoCode)
        {
            var lightningClient = await GetLightningClient(cryptoCode, true);
            if (lightningClient == null)
            {
                return NotFound();
            }

            return Ok(new JValue((await lightningClient.GetDepositAddress()).ToString()));
        }

        public virtual async Task<IActionResult> PayInvoice(string cryptoCode, PayLightningInvoiceRequest lightningInvoice)
        {
            var lightningClient = await GetLightningClient(cryptoCode, true);
            var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
            if (lightningClient == null || network == null)
            {
                return NotFound();
            }

            if (lightningInvoice?.BOLT11 is null ||
                !BOLT11PaymentRequest.TryParse(lightningInvoice.BOLT11, out _, network.NBitcoinNetwork))
            {
                ModelState.AddModelError(nameof(lightningInvoice.BOLT11), "The BOLT11 invoice was invalid.");
            }

            if (!ModelState.IsValid)
            {
                return this.CreateValidationError(ModelState);
            }

            var result = await lightningClient.Pay(lightningInvoice.BOLT11);
            switch (result.Result)
            {
                case PayResult.CouldNotFindRoute:
                    return this.CreateAPIError("could-not-find-route", "Impossible to find a route to the peer");
                case PayResult.Error:
                    return this.CreateAPIError("generic-error", result.ErrorDetail);
                case PayResult.Ok:
                    return Ok();
                default:
                    throw new NotSupportedException("Unsupported Payresult");
            }
        }

        public virtual async Task<IActionResult> GetInvoice(string cryptoCode, string id)
        {
            var lightningClient = await GetLightningClient(cryptoCode, false);

            if (lightningClient == null)
            {
                return NotFound();
            }

            var inv = await lightningClient.GetInvoice(id);
            if (inv == null)
            {
                return NotFound();
            }
            return Ok(ToModel(inv));
        }

        public virtual async Task<IActionResult> CreateInvoice(string cryptoCode, CreateLightningInvoiceRequest request)
        {
            var lightningClient = await GetLightningClient(cryptoCode, false);

            if (lightningClient == null)
            {
                return NotFound();
            }

            if (request.Amount < LightMoney.Zero)
            {
                ModelState.AddModelError(nameof(request.Amount), "Amount should be more or equals to 0");
            }

            if (request.Expiry <= TimeSpan.Zero)
            {
                ModelState.AddModelError(nameof(request.Expiry), "Expiry should be more than 0");
            }

            if (!ModelState.IsValid)
            {
                return this.CreateValidationError(ModelState);
            }
            var invoice = await lightningClient.CreateInvoice(
                new CreateInvoiceParams(request.Amount, request.Description, request.Expiry)
                {
                    PrivateRouteHints = request.PrivateRouteHints
                },
                CancellationToken.None);
            return Ok(ToModel(invoice));
        }

        private LightningInvoiceData ToModel(LightningInvoice invoice)
        {
            return new LightningInvoiceData()
            {
                Amount = invoice.Amount,
                Id = invoice.Id,
                Status = invoice.Status,
                AmountReceived = invoice.AmountReceived,
                PaidAt = invoice.PaidAt,
                BOLT11 = invoice.BOLT11,
                ExpiresAt = invoice.ExpiresAt
            };
        }

        protected bool CanUseInternalLightning(bool doingAdminThings)
        {
            return (_btcPayServerEnvironment.IsDeveloping || User.IsInRole(Roles.ServerAdmin) ||
                    (_cssThemeManager.AllowLightningInternalNodeForAll && !doingAdminThings));
        }

        protected abstract Task<ILightningClient> GetLightningClient(string cryptoCode, bool doingAdminThings);
    }
}
