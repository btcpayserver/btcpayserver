using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Security;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Controllers.Greenfield
{
    public class LightningUnavailableExceptionFilter : Attribute, IExceptionFilter
    {
        public void OnException(ExceptionContext context)
        {
            context.Result = new ObjectResult(new GreenfieldAPIError("lightning-node-unavailable", $"The lightning node is unavailable ({context.Exception.GetType().Name}: {context.Exception.Message})")) { StatusCode = 503 };
            // Do not mark handled, it is possible filters above have better errors
        }
    }

    public abstract class GreenfieldLightningNodeApiController : Controller
    {
        private readonly PoliciesSettings _policiesSettings;
        private readonly IAuthorizationService _authorizationService;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly LightningHistogramService _lnHistogramService;

        protected GreenfieldLightningNodeApiController(
            PoliciesSettings policiesSettings,
            IAuthorizationService authorizationService,
            PaymentMethodHandlerDictionary handlers,
            LightningHistogramService lnHistogramService)
        {
            _policiesSettings = policiesSettings;
            _authorizationService = authorizationService;
            _handlers = handlers;
            _lnHistogramService = lnHistogramService;
        }

        public virtual async Task<IActionResult> GetInfo(string cryptoCode, CancellationToken cancellationToken = default)
        {
            var lightningClient = await GetLightningClient(cryptoCode, true);
            var info = await lightningClient.GetInfo(cancellationToken);
            return Ok(new LightningNodeInformationData
            {
                BlockHeight = info.BlockHeight,
                NodeURIs = info.NodeInfoList.Select(nodeInfo => nodeInfo).ToArray(),
                Alias = info.Alias,
                Color = info.Color,
                Version = info.Version,
                PeersCount = info.PeersCount,
                ActiveChannelsCount = info.ActiveChannelsCount,
                InactiveChannelsCount = info.InactiveChannelsCount,
                PendingChannelsCount = info.PendingChannelsCount
            });
        }

        public virtual async Task<IActionResult> GetBalance(string cryptoCode, CancellationToken cancellationToken = default)
        {
            var lightningClient = await GetLightningClient(cryptoCode, true);
            var balance = await lightningClient.GetBalance(cancellationToken);
            return Ok(new LightningNodeBalanceData
            {
                OnchainBalance = balance.OnchainBalance != null
                    ? new OnchainBalanceData
                    {
                        Confirmed = balance.OnchainBalance.Confirmed,
                        Unconfirmed = balance.OnchainBalance.Unconfirmed,
                        Reserved = balance.OnchainBalance.Reserved
                    }
                    : null,
                OffchainBalance = balance.OffchainBalance != null
                    ? new OffchainBalanceData
                    {
                        Opening = balance.OffchainBalance.Opening,
                        Local = balance.OffchainBalance.Local,
                        Remote = balance.OffchainBalance.Remote,
                        Closing = balance.OffchainBalance.Closing,
                    }
                    : null
            });
        }
        
        public virtual async Task<IActionResult> GetHistogram(string cryptoCode, HistogramType? type = null, CancellationToken cancellationToken = default)
        {
            Enum.TryParse<HistogramType>(type.ToString(), true, out var histType);
            var lightningClient = await GetLightningClient(cryptoCode, true);
            var data = await _lnHistogramService.GetHistogram(lightningClient, histType, cancellationToken);
            if (data == null) return this.CreateAPIError(404, "histogram-not-found", "The lightning histogram was not found.");

            return Ok(new HistogramData
            {
                Type = data.Type,
                Balance = data.Balance,
                Series = data.Series,
                Labels = data.Labels
            });
        }

        public virtual async Task<IActionResult> ConnectToNode(string cryptoCode, ConnectToNodeRequest request, CancellationToken cancellationToken = default)
        {
            var lightningClient = await GetLightningClient(cryptoCode, true);
            if (request?.NodeURI is null)
            {
                ModelState.AddModelError(nameof(request.NodeURI), "A valid node info was not provided to connect to");
            }

            if (!ModelState.IsValid)
            {
                return this.CreateValidationError(ModelState);
            }

            var result = await lightningClient.ConnectTo(request.NodeURI, cancellationToken);
            switch (result)
            {
                case ConnectionResult.Ok:
                    return Ok();
                case ConnectionResult.CouldNotConnect:
                    return this.CreateAPIError("could-not-connect", "Could not connect to the remote node");
            }

            return Ok();
        }

        public virtual async Task<IActionResult> GetChannels(string cryptoCode, CancellationToken cancellationToken = default)
        {
            var lightningClient = await GetLightningClient(cryptoCode, true);

            var channels = await lightningClient.ListChannels(cancellationToken);
            return Ok(channels.Select(channel => new LightningChannelData
            {
                Capacity = channel.Capacity,
                ChannelPoint = channel.ChannelPoint.ToString(),
                IsActive = channel.IsActive,
                IsPublic = channel.IsPublic,
                LocalBalance = channel.LocalBalance,
                RemoteNode = channel.RemoteNode.ToString()
            }));
        }


        public virtual async Task<IActionResult> OpenChannel(string cryptoCode, OpenLightningChannelRequest request, CancellationToken cancellationToken = default)
        {
            var lightningClient = await GetLightningClient(cryptoCode, true);
            if (request?.NodeURI is null)
            {
                ModelState.AddModelError(nameof(request.NodeURI),
                    "A valid node info was not provided to open a channel with");
            }

            if (request?.ChannelAmount is null)
            {
                ModelState.AddModelError(nameof(request.ChannelAmount), "ChannelAmount is missing");
            }
            else if (request.ChannelAmount.Satoshi <= 0)
            {
                ModelState.AddModelError(nameof(request.ChannelAmount), "ChannelAmount must be more than 0");
            }

            if (request?.FeeRate is null)
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

            var response = await lightningClient.OpenChannel(new OpenChannelRequest
            {
                ChannelAmount = request.ChannelAmount,
                FeeRate = request.FeeRate,
                NodeInfo = request.NodeURI
            }, cancellationToken);

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

        public virtual async Task<IActionResult> GetDepositAddress(string cryptoCode, CancellationToken cancellationToken = default)
        {
            var lightningClient = await GetLightningClient(cryptoCode, true);
            var addr = await lightningClient.GetDepositAddress(cancellationToken);
            return Ok(new JValue(addr.ToString()));
        }

        public virtual async Task<IActionResult> GetPayment(string cryptoCode, string paymentHash, CancellationToken cancellationToken = default)
        {
            var lightningClient = await GetLightningClient(cryptoCode, false);
            var payment = await lightningClient.GetPayment(paymentHash, cancellationToken);
            return payment == null ? this.CreateAPIError(404, "payment-not-found", "Impossible to find a lightning payment with this payment hash") : Ok(ToModel(payment));
        }

        public virtual async Task<IActionResult> PayInvoice(string cryptoCode, PayLightningInvoiceRequest lightningInvoice, CancellationToken cancellationToken = default)
        {
            var lightningClient = await GetLightningClient(cryptoCode, true);
            var network =  GetNetwork(cryptoCode);
            BOLT11PaymentRequest bolt11 = null;

            if (string.IsNullOrEmpty(lightningInvoice.BOLT11) ||
                !BOLT11PaymentRequest.TryParse(lightningInvoice.BOLT11, out bolt11, network.NBitcoinNetwork))
            {
                ModelState.AddModelError(nameof(lightningInvoice.BOLT11), "The BOLT11 invoice was invalid.");
            }

            if (!ModelState.IsValid)
            {
                return this.CreateValidationError(ModelState);
            }

            var param = lightningInvoice.MaxFeeFlat != null || lightningInvoice.MaxFeePercent != null
                    || lightningInvoice.Amount != null || lightningInvoice.SendTimeout != null
                ? new PayInvoiceParams
                {
                    MaxFeePercent = lightningInvoice.MaxFeePercent,
                    MaxFeeFlat = lightningInvoice.MaxFeeFlat,
                    Amount = lightningInvoice.Amount,
                    SendTimeout = lightningInvoice.SendTimeout
                }
                : null;
            var result = await lightningClient.Pay(lightningInvoice.BOLT11, param, cancellationToken);

            if (result.Result is PayResult.Ok or PayResult.Unknown && bolt11?.PaymentHash is not null)
            {
                // get a new instance of the LN client, because the old one might have disposed its HTTPClient
                lightningClient = await GetLightningClient(cryptoCode, true);

                var paymentHash = bolt11.PaymentHash.ToString();
                var payment = await lightningClient.GetPayment(paymentHash, cancellationToken);
                var data = new LightningPaymentData
                {
                    Id = payment.Id,
                    PaymentHash = paymentHash,
                    Status = payment.Status,
                    BOLT11 = payment.BOLT11,
                    Preimage = payment.Preimage,
                    CreatedAt = payment.CreatedAt,
                    TotalAmount = payment.AmountSent,
                    FeeAmount = payment.Fee,
                };
                return result.Result is PayResult.Ok ? Ok(data) : Accepted(data);
            }

            return result.Result switch
            {
                PayResult.CouldNotFindRoute => this.CreateAPIError("could-not-find-route", result.ErrorDetail ?? "Impossible to find a route to the peer"),
                PayResult.Error => this.CreateAPIError("generic-error", result.ErrorDetail),
                PayResult.Unknown => Accepted(new LightningPaymentData
                {
                    Status = LightningPaymentStatus.Unknown
                }),
                PayResult.Ok => Ok(new LightningPaymentData
                {
                    BOLT11 = bolt11?.ToString(),
                    Status = LightningPaymentStatus.Complete,
                    TotalAmount = result.Details?.TotalAmount,
                    FeeAmount = result.Details?.FeeAmount,
                    PaymentHash = result.Details?.PaymentHash.ToString(),
                    Preimage = result.Details?.Preimage.ToString()
                }),
                _ => throw new NotSupportedException("Unsupported PayResult")
            };
        }

        public virtual async Task<IActionResult> GetInvoice(string cryptoCode, string id, CancellationToken cancellationToken = default)
        {
            var lightningClient = await GetLightningClient(cryptoCode, false);
            var inv = await lightningClient.GetInvoice(id, cancellationToken);
            return inv == null ? this.CreateAPIError(404, "invoice-not-found", "Impossible to find a lightning invoice with this id") : Ok(ToModel(inv));
        }

        public virtual async Task<IActionResult> GetInvoices(string cryptoCode, [FromQuery] bool? pendingOnly, [FromQuery] long? offsetIndex, CancellationToken cancellationToken = default)
        {
            var lightningClient = await GetLightningClient(cryptoCode, false);
            var param = new ListInvoicesParams { PendingOnly = pendingOnly, OffsetIndex = offsetIndex };
            var invoices = await lightningClient.ListInvoices(param, cancellationToken);
            return Ok(invoices.Select(ToModel).ToArray());
        }

        public virtual async Task<IActionResult> GetPayments(string cryptoCode, [FromQuery] bool? includePending, [FromQuery] long? offsetIndex, CancellationToken cancellationToken = default)
        {
            var lightningClient = await GetLightningClient(cryptoCode, false);
            var param = new ListPaymentsParams { IncludePending = includePending, OffsetIndex = offsetIndex };
            var payments = await lightningClient.ListPayments(param, cancellationToken);
            return Ok(payments.Select(ToModel).ToArray());
        }

        public virtual async Task<IActionResult> CreateInvoice(string cryptoCode, CreateLightningInvoiceRequest request, CancellationToken cancellationToken = default)
        {
            var lightningClient = await GetLightningClient(cryptoCode, false);
            if (request.Amount < LightMoney.Zero)
            {
                ModelState.AddModelError(nameof(request.Amount), "Amount should be more or equals to 0");
            }

            if (request.Description is null && request.DescriptionHashOnly)
            {
                ModelState.AddModelError(nameof(request.Description), "Description is required when `descriptionHashOnly` is true");
            }

            if (request.Expiry <= TimeSpan.Zero)
            {
                ModelState.AddModelError(nameof(request.Expiry), "Expiry should be more than 0");
            }
            if (!ModelState.IsValid)
            {
                return this.CreateValidationError(ModelState);
            }

            request.Description ??= "";
            try
            {
                var param = new CreateInvoiceParams(request.Amount, request.Description, request.Expiry)
                {
                    PrivateRouteHints = request.PrivateRouteHints,
                    DescriptionHashOnly = request.DescriptionHashOnly
                };
                var invoice = await lightningClient.CreateInvoice(param, cancellationToken);
                return Ok(ToModel(invoice));
            }
            catch (Exception ex)
            {
                return this.CreateAPIError("generic-error", ex.Message);
            }
        }
        protected BTCPayNetwork GetNetwork(string cryptoCode)
            => _handlers.TryGetValue(PaymentTypes.LN.GetPaymentMethodId(cryptoCode), out var h) 
                    && h is IHasNetwork { Network: var network }
                ? network
                : null;
        protected JsonHttpException ErrorLightningNodeNotConfiguredForStore()
        {
            return new JsonHttpException(this.CreateAPIError(404, "lightning-not-configured", "The lightning node is not set up"));
        }
        protected JsonHttpException ErrorInternalLightningNodeNotConfigured()
        {
            return new JsonHttpException(this.CreateAPIError(404, "lightning-not-configured", "The internal lightning node is not set up"));
        }
        protected JsonHttpException ErrorCryptoCodeNotFound()
        {
            return new JsonHttpException(this.CreateAPIError(404, "unknown-cryptocode", "This crypto code isn't set up in this BTCPay Server instance"));
        }
        protected JsonHttpException ErrorShouldBeAdminForInternalNode()
        {
            return new JsonHttpException(this.CreateAPIPermissionError("btcpay.server.canuseinternallightningnode", "The user should be admin to use the internal lightning node"));
        }

        private LightningInvoiceData ToModel(LightningInvoice invoice)
        {
            var data = new LightningInvoiceData
            {
                Amount = invoice.Amount,
                Id = invoice.Id,
                Status = invoice.Status,
                AmountReceived = invoice.AmountReceived,
                PaidAt = invoice.PaidAt,
                BOLT11 = invoice.BOLT11,
                ExpiresAt = invoice.ExpiresAt,
                PaymentHash = invoice.PaymentHash,
                Preimage = invoice.Preimage
            };

            if (invoice.CustomRecords != null)
            {
                data.CustomRecords = invoice.CustomRecords;
            }
            return data;
        }

        private LightningPaymentData ToModel(LightningPayment payment)
        {
            return new LightningPaymentData
            {
                TotalAmount = payment.AmountSent,
                FeeAmount = payment.Amount != null && payment.AmountSent != null ? payment.AmountSent - payment.Amount : null,
                Id = payment.Id,
                Status = payment.Status,
                CreatedAt = payment.CreatedAt,
                BOLT11 = payment.BOLT11,
                PaymentHash = payment.PaymentHash,
                Preimage = payment.Preimage
            };
        }

        protected async Task<bool> CanUseInternalLightning(bool doingAdminThings)
        {
            return (!doingAdminThings && this._policiesSettings.AllowLightningInternalNodeForAll) ||
                (await _authorizationService.AuthorizeAsync(User, null,
                    new PolicyRequirement(Policies.CanUseInternalLightningNode))).Succeeded;
        }

        protected abstract Task<ILightningClient> GetLightningClient(string cryptoCode, bool doingAdminThings);
    }
}
