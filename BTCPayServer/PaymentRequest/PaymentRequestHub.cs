using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Services.PaymentRequests;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Builder;

namespace BTCPayServer.PaymentRequest
{
    public class PaymentRequestHub : Hub
    {
        private readonly PaymentRequestController _PaymentRequestController;
        public const string InvoiceCreated = "InvoiceCreated";
        public const string PaymentReceived = "PaymentReceived";
        public const string InfoUpdated = "InfoUpdated";
        public const string InvoiceError = "InvoiceError";
        public const string CancelInvoiceError = "CancelInvoiceError";
        public const string InvoiceCancelled = "InvoiceCancelled";

        public PaymentRequestHub(PaymentRequestController paymentRequestController)
        {
            _PaymentRequestController = paymentRequestController;
        }

        public async Task ListenToPaymentRequest(string paymentRequestId)
        {
            if (Context.Items.ContainsKey("pr-id"))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, Context.Items["pr-id"].ToString());
                Context.Items.Remove("pr-id");
            }

            Context.Items.Add("pr-id", paymentRequestId);
            await Groups.AddToGroupAsync(Context.ConnectionId, paymentRequestId);
        }


        public async Task Pay(decimal? amount = null)
        {
            _PaymentRequestController.ControllerContext.HttpContext = Context.GetHttpContext();
            var result =
                await _PaymentRequestController.PayPaymentRequest(Context.Items["pr-id"].ToString(), false, amount);
            switch (result)
            {
                case OkObjectResult okObjectResult:
                    await Clients.Caller.SendCoreAsync(InvoiceCreated, new[] {okObjectResult.Value.ToString()});
                    break;
                case ObjectResult objectResult:
                    await Clients.Caller.SendCoreAsync(InvoiceError, new[] {objectResult.Value});
                    break;
                default:
                    await Clients.Caller.SendCoreAsync(InvoiceError, System.Array.Empty<object>());
                    break;
            }
        }

        public async Task CancelUnpaidPendingInvoice()
        {
            _PaymentRequestController.ControllerContext.HttpContext = Context.GetHttpContext();
            var result =
                await _PaymentRequestController.CancelUnpaidPendingInvoice(Context.Items["pr-id"].ToString(), false);
            switch (result)
            {
                case OkObjectResult okObjectResult:
                    await Clients.Group(Context.Items["pr-id"].ToString()).SendCoreAsync(InvoiceCancelled, System.Array.Empty<object>());
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
        private readonly PaymentRequestService _PaymentRequestService;


        public PaymentRequestStreamer(EventAggregator eventAggregator,
            IHubContext<PaymentRequestHub> hubContext,
            PaymentRequestRepository paymentRequestRepository,
            PaymentRequestService paymentRequestService) : base(eventAggregator)
        {
            _HubContext = hubContext;
            _PaymentRequestRepository = paymentRequestRepository;
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
            var (total, items) = await _PaymentRequestRepository.FindPaymentRequests(new PaymentRequestQuery()
            {
                Status = new[] {PaymentRequestData.PaymentRequestStatus.Pending}
            }, cancellationToken);

            Logs.PayServer.LogInformation($"{total} pending payment requests being checked since last run");
            await Task.WhenAll(items.Select(i => _PaymentRequestService.UpdatePaymentRequestStateIfNeeded(i))
                .ToArray());
        }

        Task _CheckingPendingPayments;

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await base.StopAsync(cancellationToken);
            await (_CheckingPendingPayments ?? Task.CompletedTask);
        }

        protected override void SubscibeToEvents()
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
                    if (invoiceEvent.Name == InvoiceEvent.ReceivedPayment)
                    {
                        await _PaymentRequestService.UpdatePaymentRequestStateIfNeeded(paymentId);
                        var data = invoiceEvent.Payment.GetCryptoPaymentData();
                        await _HubContext.Clients.Group(paymentId).SendCoreAsync(PaymentRequestHub.PaymentReceived,
                            new object[]
                            {
                            data.GetValue(),
                            invoiceEvent.Payment.GetCryptoCode(),
                            invoiceEvent.Payment.GetPaymentMethodId().PaymentType.ToString()
                            });
                    }

                    await InfoUpdated(paymentId);
                }
            }
            else if (evt is PaymentRequestUpdated updated)
            {
                await InfoUpdated(updated.PaymentRequestId);

                var expiry = updated.Data.GetBlob().ExpiryDate;
                if (updated.Data.Status == PaymentRequestData.PaymentRequestStatus.Pending &&
                    expiry.HasValue)
                {
                    QueueExpiryTask(
                        updated.PaymentRequestId,
                        expiry.Value,
                        cancellationToken);
                }
            }
        }

        private void QueueExpiryTask(string paymentRequestId, DateTime expiry, CancellationToken cancellationToken)
        {
            Task.Run(async () =>
            {
                var delay = expiry - DateTime.Now;
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
                    .SendCoreAsync(PaymentRequestHub.InfoUpdated, new object[] {req});
            }
        }
    }
}
