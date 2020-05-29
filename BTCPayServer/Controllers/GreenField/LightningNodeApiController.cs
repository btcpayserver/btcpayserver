using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.HostedServices;
using BTCPayServer.Lightning;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;

namespace BTCPayServer.Controllers.GreenField
{
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

            try
            {
                var info = await lightningClient.GetInfo();
                return Ok(new LightningNodeInformationData()
                {
                    BlockHeight = info.BlockHeight,
                    NodeInfoList = info.NodeInfoList.Select(nodeInfo => nodeInfo.ToString())
                });
            }
            catch (Exception e)
            {
                ModelState.AddModelError(string.Empty, e.Message);
                return BadRequest(new ValidationProblemDetails(ModelState));
            }
        }

        public virtual async Task<IActionResult> ConnectToNode(string cryptoCode, ConnectToNodeRequest request)
        {
            var lightningClient = await GetLightningClient(cryptoCode, true);
            if (lightningClient == null)
            {
                return NotFound();
            }

            if (TryGetNodeInfo(request, out var nodeInfo))
            {
                ModelState.AddModelError(nameof(request.NodeId), "A valid node info was not provided to connect to");
            }

            if (CheckValidation(out var errorActionResult))
            {
                return errorActionResult;
            }

            try
            {
                await lightningClient.ConnectTo(nodeInfo);
            }
            catch (Exception e)
            {
                ModelState.AddModelError(string.Empty, e.Message);
                return BadRequest(new ValidationProblemDetails(ModelState));
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

            try
            {
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
            catch (Exception e)
            {
                ModelState.AddModelError(string.Empty, e.Message);
                return BadRequest(new ValidationProblemDetails(ModelState));
            }
        }

        
        public virtual async Task<IActionResult> OpenChannel(string cryptoCode, OpenLightningChannelRequest request)
        {
            var lightningClient = await GetLightningClient(cryptoCode, true);
            if (lightningClient == null)
            {
                return NotFound();
            }

            if (TryGetNodeInfo(request.Node, out var nodeInfo))
            {
                ModelState.AddModelError(nameof(request.Node),
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

            if (CheckValidation(out var errorActionResult))
            {
                return errorActionResult;
            }

            try
            {
                var response = await lightningClient.OpenChannel(new Lightning.OpenChannelRequest()
                {
                    ChannelAmount = request.ChannelAmount, FeeRate = request.FeeRate, NodeInfo = nodeInfo
                });
                if (response.Result == OpenChannelResult.Ok)
                {
                    return Ok();
                }

                ModelState.AddModelError(string.Empty, response.Result.ToString());
                return BadRequest(new ValidationProblemDetails(ModelState));
            }
            catch (Exception e)
            {
                ModelState.AddModelError(string.Empty, e.Message);
                return BadRequest(new ValidationProblemDetails(ModelState));
            }
        }

        public virtual async Task<IActionResult> GetDepositAddress(string cryptoCode)
        {
            var lightningClient = await GetLightningClient(cryptoCode, true);
            if (lightningClient == null)
            {
                return NotFound();
            }

            return Ok((await lightningClient.GetDepositAddress()).ToString());
        }

        public virtual async Task<IActionResult> PayInvoice(string cryptoCode, PayLightningInvoiceRequest lightningInvoice)
        {
            var lightningClient = await GetLightningClient(cryptoCode, true);
            var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
            if (lightningClient == null || network == null)
            {
                return NotFound();
            }

            try
            {
                BOLT11PaymentRequest.TryParse(lightningInvoice.Invoice, out var bolt11PaymentRequest, network.NBitcoinNetwork);
            }
            catch (Exception)
            {
                ModelState.AddModelError(nameof(lightningInvoice), "The BOLT11 invoice was invalid.");
            }

            if (CheckValidation(out var errorActionResult))
            {
                return errorActionResult;
            }

            var result = await lightningClient.Pay(lightningInvoice.Invoice);
            switch (result.Result)
            {
                case PayResult.Ok:
                    return Ok();
                case PayResult.CouldNotFindRoute:
                    ModelState.AddModelError(nameof(lightningInvoice.Invoice), "Could not find route");
                    break;
                case PayResult.Error:
                    ModelState.AddModelError(nameof(lightningInvoice.Invoice), result.ErrorDetail);
                    break;
            }

            return BadRequest(new ValidationProblemDetails(ModelState));
        }

        public virtual async Task<IActionResult> GetInvoice(string cryptoCode, string id)
        {
            var lightningClient = await GetLightningClient(cryptoCode, false);
            
            if (lightningClient == null)
            {
                return NotFound();
            }

            try
            {
                var inv = await lightningClient.GetInvoice(id);
                if (inv == null)
                {
                    return NotFound();
                }
                return Ok(ToModel(inv));
            }
            catch (Exception e)
            {
                ModelState.AddModelError(string.Empty, e.Message);
                return BadRequest(new ValidationProblemDetails(ModelState));
            }
        }

        public virtual async Task<IActionResult> CreateInvoice(string cryptoCode, CreateLightningInvoiceRequest request)
        {
            var lightningClient = await GetLightningClient(cryptoCode, false);
            
            if (lightningClient == null)
            {
                return NotFound();
            }

            if (CheckValidation(out var errorActionResult))
            {
                return errorActionResult;
            }

            try
            {
                var invoice = await lightningClient.CreateInvoice(
                    new CreateInvoiceParams(request.Amount, request.Description, request.Expiry)
                    {
                        PrivateRouteHints = request.PrivateRouteHints
                    },
                    CancellationToken.None);

                return Ok(ToModel(invoice));
            }
            catch (Exception e)
            {
                ModelState.AddModelError(string.Empty, e.Message);
                return BadRequest(new ValidationProblemDetails(ModelState));
            }
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

        private bool CheckValidation(out IActionResult result)
        {
            if (!ModelState.IsValid)
            {
                result = BadRequest(new ValidationProblemDetails(ModelState));
                return true;
            }

            result = null;
            return false;
        }

        protected bool CanUseInternalLightning(bool doingAdminThings)
        {
            return (_btcPayServerEnvironment.IsDevelopping || User.IsInRole(Roles.ServerAdmin) ||
                    (_cssThemeManager.AllowLightningInternalNodeForAll && !doingAdminThings));
        }


        private bool TryGetNodeInfo(ConnectToNodeRequest request, out NodeInfo nodeInfo)
        {
            nodeInfo = null;
            if (!string.IsNullOrEmpty(request.NodeInfo)) return NodeInfo.TryParse(request.NodeInfo, out nodeInfo);
            try
            {
                nodeInfo = new NodeInfo(new PubKey(request.NodeId), request.NodeHost, request.NodePort);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        protected abstract Task<ILightningClient> GetLightningClient(string cryptoCode, bool doingAdminThings);
    }
}
