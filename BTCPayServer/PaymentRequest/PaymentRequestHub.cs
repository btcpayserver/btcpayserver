using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Services;
using BTCPayServer.Services.PaymentRequests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using PaymentRequestData = BTCPayServer.Client.Models.PaymentRequestData;

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


        public PaymentRequestStreamer(EventAggregator eventAggregator,
            IHubContext<PaymentRequestHub> hubContext,
            PaymentRequestRepository paymentRequestRepository,
            PrettyNameProvider prettyNameProvider,
            PaymentRequestService paymentRequestService,
            Logs logs) : base(eventAggregator, logs)
        {
            _HubContext = hubContext;
            _PaymentRequestRepository = paymentRequestRepository;
            _prettyNameProvider = prettyNameProvider;
            _PaymentRequestService = paymentRequestService;
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            await base.StartAsync(cancellationToken);
            _CheckingPendingPayments = CheckingPendingPayments(cancellationToken)
                .ContinueWith(_ => _CheckingPendingPayments = null, TaskScheduler.Default);
        }

        private async Task CheckingPendingPayments(CancellationToken cancellationToken)
        {
            Logs.PayServer.LogInformation("Starting payment request expiration watcher");
            var items = await _PaymentRequestRepository.FindPaymentRequests(new PaymentRequestQuery
            {
                Status = new[]
                {
                    PaymentRequestData.PaymentRequestStatus.Pending,
                    PaymentRequestData.PaymentRequestStatus.Processing
                }
            }, cancellationToken);
            Logs.PayServer.LogInformation($"{items.Length} pending payment requests being checked since last run");
            await Task.WhenAll(items.Select(i => _PaymentRequestService.UpdatePaymentRequestStateIfNeeded(i))
                .ToArray());
        }

        Task _CheckingPendingPayments;

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await base.StopAsync(cancellationToken);
            await (_CheckingPendingPayments ?? Task.CompletedTask);
        }

        protected override void SubscribeToEvents()
        {
            Subscribe<InvoiceEvent>();
            Subscribe<PaymentRequestUpdated>();
        }

        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            if (evt is InvoiceEvent invoiceEvent)
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
            else if (evt is PaymentRequestUpdated updated)
            {
                await _PaymentRequestService.UpdatePaymentRequestStateIfNeeded(updated.PaymentRequestId);
                await InfoUpdated(updated.PaymentRequestId);

                var isPending = updated.Data.Status is
                    PaymentRequestData.PaymentRequestStatus.Pending or
                    PaymentRequestData.PaymentRequestStatus.Processing;
                var expiry = updated.Data.GetBlob().ExpiryDate;
                if (isPending && expiry.HasValue)
                {
                    QueueExpiryTask(
                        updated.PaymentRequestId,
                        expiry.Value.UtcDateTime,
                        cancellationToken);
                }
            }
        }

        private void QueueExpiryTask(string paymentRequestId, DateTime expiry, CancellationToken cancellationToken)
        {
            Task.Run(async () =>
            {
                var delay = expiry - DateTime.UtcNow;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken);
                await _PaymentRequestService.UpdatePaymentRequestStateIfNeeded(paymentRequestId);
            }, cancellationToken);
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
