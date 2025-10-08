using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Services;
using BTCPayServer.Services.PaymentRequests;
using Google.Apis.Storage.v1.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.PaymentRequest
{
    public class PaymentRequestHub : Hub
    {
        private readonly UIPaymentRequestController _PaymentRequestController;
        public const string InvoiceCreated = "InvoiceCreated";
        public const string InvoiceConfirmed = "InvoiceConfirmed";
        public const string PaymentReceived = "PaymentReceived";
        public const string InfoUpdated = "InfoUpdated";
        public const string InvoiceError = "InvoiceError";
        public const string CancelInvoiceError = "CancelInvoiceError";
        public const string InvoiceCancelled = "InvoiceCancelled";

        public PaymentRequestHub(UIPaymentRequestController paymentRequestController)
        {
            _PaymentRequestController = paymentRequestController;
        }

        public async Task ListenToPaymentRequest(string prId)
        {
            if (Context.Items.ContainsKey("pr-id"))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, Context.Items["pr-id"].ToString());
                Context.Items.Remove("pr-id");
            }
            if (prId != null)
            {
                Context.Items.Add("pr-id", prId);
                await Groups.AddToGroupAsync(Context.ConnectionId, prId);
            }
        }


        public async Task Pay(string prId, decimal? amount = null)
        {
            if (prId is null)
                return;
            _PaymentRequestController.ControllerContext.HttpContext = Context.GetHttpContext();
            var result =
                await _PaymentRequestController.PayPaymentRequest(prId, false, amount);
            switch (result)
            {
                case OkObjectResult okObjectResult:
                    await Clients.Caller.SendCoreAsync(InvoiceCreated, new[] { okObjectResult.Value.ToString() });
                    break;
                case ObjectResult objectResult:
                    await Clients.Caller.SendCoreAsync(InvoiceError, new[] { objectResult.Value });
                    break;
                default:
                    await Clients.Caller.SendCoreAsync(InvoiceError, System.Array.Empty<object>());
                    break;
            }
        }

        public async Task CancelUnpaidPendingInvoice(string prId)
        {
            if (prId is null)
                return;
            _PaymentRequestController.ControllerContext.HttpContext = Context.GetHttpContext();
            var result =
                await _PaymentRequestController.CancelUnpaidPendingInvoice(prId, false);
            switch (result)
            {
                case OkObjectResult:
                    await Clients.Group(prId).SendCoreAsync(InvoiceCancelled, System.Array.Empty<object>());
                    break;

                default:
                    await Clients.Caller.SendCoreAsync(CancelInvoiceError, System.Array.Empty<object>());
                    break;
            }
        }

        public static string GetHubPath(HttpRequest request)
        {
            return request.GetRelativePathOrAbsolute("/payment-requests/hub");
        }

        public static void Register(IEndpointRouteBuilder route)
        {
            route.MapHub<PaymentRequestHub>("/payment-requests/hub");
        }
    }

    public class PaymentRequestStreamer : EventHostedServiceBase
    {
        private readonly IHubContext<PaymentRequestHub> _HubContext;
        private readonly PaymentRequestRepository _PaymentRequestRepository;
        private readonly PrettyNameProvider _prettyNameProvider;
        private readonly PaymentRequestService _PaymentRequestService;
        private readonly DelayedTaskScheduler _delayedTaskScheduler;

        public PaymentRequestStreamer(EventAggregator eventAggregator,
            IHubContext<PaymentRequestHub> hubContext,
            PaymentRequestRepository paymentRequestRepository,
            PrettyNameProvider prettyNameProvider,
            PaymentRequestService paymentRequestService,
            DelayedTaskScheduler delayedTaskScheduler,
            Logs logs) : base(eventAggregator, logs)
        {
            _HubContext = hubContext;
            _PaymentRequestRepository = paymentRequestRepository;
            _prettyNameProvider = prettyNameProvider;
            _PaymentRequestService = paymentRequestService;
            _delayedTaskScheduler = delayedTaskScheduler;
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            this.PushEvent(new Starting());
            await base.StartAsync(cancellationToken);
        }

        internal void CheckExpirable()
        {
            this.PushEvent(new Starting());
        }

        record Starting;

        protected override void SubscribeToEvents()
        {
            Subscribe<InvoiceEvent>();
            Subscribe<PaymentRequestEvent>();
        }

        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            if (evt is Starting)
            {
                foreach (var req in await _PaymentRequestRepository.GetExpirablePaymentRequests(cancellationToken))
                {
                    UpdateOnExpire(req);
                }
            }
            else if (evt is InvoiceEvent invoiceEvent)
            {
                foreach (var paymentId in PaymentRequestRepository.GetPaymentIdsFromInternalTags(invoiceEvent.Invoice))
                {
                    if (invoiceEvent.Name is InvoiceEvent.ReceivedPayment or InvoiceEvent.MarkedCompleted or InvoiceEvent.MarkedInvalid)
                    {
                        await _PaymentRequestService.UpdatePaymentRequestStateIfNeeded(paymentId);
                        if (invoiceEvent.Payment != null)
                        {
                            await _HubContext.Clients.Group(paymentId).SendCoreAsync(PaymentRequestHub.PaymentReceived,
                                new object[]
                                {
                                    invoiceEvent.Payment.Value,
                                    invoiceEvent.Payment.Currency,
                                    _prettyNameProvider.PrettyName(invoiceEvent.Payment.PaymentMethodId),
                                    invoiceEvent.Payment.PaymentMethodId.ToString()
                                }, cancellationToken);
                        }
                    }
                    else if (invoiceEvent.Name is InvoiceEvent.Completed or InvoiceEvent.Confirmed)
                    {
                        await _PaymentRequestService.UpdatePaymentRequestStateIfNeeded(paymentId);
                        await _HubContext.Clients.Group(paymentId).SendCoreAsync(PaymentRequestHub.InvoiceConfirmed,
                            new object[]
                            {
                                invoiceEvent.InvoiceId
                            }, cancellationToken);
                    }

                    await InfoUpdated(paymentId);
                }
            }
            else if (evt is PaymentRequestEvent updated)
            {
                await _PaymentRequestService.UpdatePaymentRequestStateIfNeeded(updated.Data.Id);
                await InfoUpdated(updated.Data.Id);

                UpdateOnExpire(updated.Data);
            }
        }

        private void UpdateOnExpire(Data.PaymentRequestData data)
        {
            if (data is 
                {
                    Expirable: true,
                    Expiry: { } e
                })
            {
                _delayedTaskScheduler.Schedule($"PAYREQ_{data.Id}", e, async () =>
                {
                    await _PaymentRequestService.UpdatePaymentRequestStateIfNeeded(data.Id);
                });
            }
        }

        private async Task InfoUpdated(string paymentRequestId)
        {
            var req = await _PaymentRequestService.GetPaymentRequest(paymentRequestId);
            if (req != null)
            {
                await _HubContext.Clients.Group(paymentRequestId)
                    .SendCoreAsync(PaymentRequestHub.InfoUpdated, new object[] { req });
            }
        }
    }
}
