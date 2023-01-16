#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers.Greenfield;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.DataEncoders;
using Newtonsoft.Json;

namespace BTCPayServer.HostedServices
{
    /// <summary>
    /// This class send webhook notifications
    /// It also make sure the events sent to a webhook are sent in order to the webhook
    /// </summary>
    public class WebhookSender : EventHostedServiceBase
    {
        readonly Encoding UTF8 = new UTF8Encoding(false);
        public readonly static JsonSerializerSettings DefaultSerializerSettings;
        static WebhookSender()
        {
            DefaultSerializerSettings = WebhookEvent.DefaultSerializerSettings;
        }
        public const string OnionNamedClient = "greenfield-webhook.onion";
        public const string ClearnetNamedClient = "greenfield-webhook.clearnet";
        public const string LoopbackNamedClient = "greenfield-webhook.loopback";
        private HttpClient GetClient(Uri uri)
        {
            return HttpClientFactory.CreateClient(uri.IsOnion() ? OnionNamedClient : uri.IsLoopback ? LoopbackNamedClient : ClearnetNamedClient);
        }
        class WebhookDeliveryRequest
        {
            public WebhookEvent WebhookEvent;
            public Data.WebhookDeliveryData Delivery;
            public WebhookBlob WebhookBlob;
            public string WebhookId;
            public WebhookDeliveryRequest(string webhookId, WebhookEvent webhookEvent, Data.WebhookDeliveryData delivery, WebhookBlob webhookBlob)
            {
                WebhookId = webhookId;
                WebhookEvent = webhookEvent;
                Delivery = delivery;
                WebhookBlob = webhookBlob;
            }
        }

        MultiProcessingQueue _processingQueue = new MultiProcessingQueue();
        public StoreRepository StoreRepository { get; }
        public IHttpClientFactory HttpClientFactory { get; }

        public WebhookSender(EventAggregator eventAggregator,
            StoreRepository storeRepository,
            IHttpClientFactory httpClientFactory,
            Logs logs) : base(eventAggregator, logs)
        {
            StoreRepository = storeRepository;
            HttpClientFactory = httpClientFactory;
        }

        protected override void SubscribeToEvents()
        {
            Subscribe<InvoiceEvent>();
        }

        public async Task<string?> Redeliver(string deliveryId)
        {
            var deliveryRequest = await CreateRedeliveryRequest(deliveryId);
            if (deliveryRequest is null)
                return null;
            EnqueueDelivery(deliveryRequest);
            return deliveryRequest.Delivery.Id;
        }

        private async Task<WebhookDeliveryRequest?> CreateRedeliveryRequest(string deliveryId)
        {
            using var ctx = StoreRepository.CreateDbContext();
            var webhookDelivery = await ctx.WebhookDeliveries.AsNoTracking()
                     .Where(o => o.Id == deliveryId)
                     .Select(o => new
                     {
                         Webhook = o.Webhook,
                         Delivery = o
                     })
                     .FirstOrDefaultAsync();
            if (webhookDelivery is null)
                return null;
            var oldDeliveryBlob = webhookDelivery.Delivery.GetBlob();
            var newDelivery = NewDelivery(webhookDelivery.Webhook.Id);
            var newDeliveryBlob = new WebhookDeliveryBlob();
            newDeliveryBlob.Request = oldDeliveryBlob.Request;
            var webhookEvent = newDeliveryBlob.ReadRequestAs<WebhookEvent>();
            webhookEvent.DeliveryId = newDelivery.Id;
            webhookEvent.WebhookId = webhookDelivery.Webhook.Id;
            // if we redelivered a redelivery, we still want the initial delivery here
            webhookEvent.OriginalDeliveryId ??= deliveryId;
            webhookEvent.IsRedelivery = true;
            newDeliveryBlob.Request = ToBytes(webhookEvent);
            newDelivery.SetBlob(newDeliveryBlob);
            return new WebhookDeliveryRequest(webhookDelivery.Webhook.Id, webhookEvent, newDelivery, webhookDelivery.Webhook.GetBlob());
        }

        private WebhookEvent GetTestWebHook(string storeId, string webhookId, WebhookEventType webhookEventType, Data.WebhookDeliveryData delivery)
        {
            var webhookEvent = GetWebhookEvent(webhookEventType);
            webhookEvent.InvoiceId = "__test__" + Guid.NewGuid().ToString() + "__test__";
            webhookEvent.StoreId = storeId;
            webhookEvent.DeliveryId = delivery.Id;
            webhookEvent.WebhookId = webhookId;
            webhookEvent.OriginalDeliveryId = "__test__" + Guid.NewGuid().ToString() + "__test__";
            webhookEvent.IsRedelivery = false;
            webhookEvent.Timestamp = delivery.Timestamp;

            return webhookEvent;
        }

        public async Task<DeliveryResult> TestWebhook(string storeId, string webhookId, WebhookEventType webhookEventType, CancellationToken cancellationToken)
        {
            var delivery = NewDelivery(webhookId);
            var webhook = (await StoreRepository.GetWebhooks(storeId)).FirstOrDefault(w => w.Id == webhookId);
            var deliveryRequest = new WebhookDeliveryRequest(
                webhookId,
                GetTestWebHook(storeId, webhookId, webhookEventType, delivery),
                delivery,
                webhook.GetBlob()
            );
            return await SendDelivery(deliveryRequest, cancellationToken);
        }

        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            if (evt is InvoiceEvent invoiceEvent)
            {
                var webhooks = await StoreRepository.GetWebhooks(invoiceEvent.Invoice.StoreId);
                foreach (var webhook in webhooks)
                {
                    var webhookBlob = webhook.GetBlob();
                    if (!(GetWebhookEvent(invoiceEvent) is WebhookInvoiceEvent webhookEvent))
                        continue;
                    if (!ShouldDeliver(webhookEvent.Type, webhookBlob))
                        continue;
                    Data.WebhookDeliveryData delivery = NewDelivery(webhook.Id);
                    webhookEvent.InvoiceId = invoiceEvent.InvoiceId;
                    webhookEvent.StoreId = invoiceEvent.Invoice.StoreId;
                    webhookEvent.DeliveryId = delivery.Id;
                    webhookEvent.WebhookId = webhook.Id;
                    webhookEvent.OriginalDeliveryId = delivery.Id;
                    webhookEvent.Metadata = invoiceEvent.Invoice.Metadata.ToJObject();
                    webhookEvent.IsRedelivery = false;
                    webhookEvent.Timestamp = delivery.Timestamp;
                    var context = new WebhookDeliveryRequest(webhook.Id, webhookEvent, delivery, webhookBlob);
                    EnqueueDelivery(context);
                }
            }
        }

        private void EnqueueDelivery(WebhookDeliveryRequest context)
        {
            _processingQueue.Enqueue(context.WebhookId, (cancellationToken) => Process(context, cancellationToken));
        }

        public static WebhookInvoiceEvent GetWebhookEvent(WebhookEventType webhookEventType)
        {
            switch (webhookEventType)
            {
                case WebhookEventType.InvoiceCreated:
                    return new WebhookInvoiceEvent(WebhookEventType.InvoiceCreated);
                case WebhookEventType.InvoiceReceivedPayment:
                    return new WebhookInvoiceReceivedPaymentEvent(WebhookEventType.InvoiceReceivedPayment);
                case WebhookEventType.InvoicePaymentSettled:
                    return new WebhookInvoicePaymentSettledEvent(WebhookEventType.InvoicePaymentSettled);
                case WebhookEventType.InvoiceProcessing:
                    return new WebhookInvoiceProcessingEvent(WebhookEventType.InvoiceProcessing);
                case WebhookEventType.InvoiceExpired:
                    return new WebhookInvoiceExpiredEvent(WebhookEventType.InvoiceExpired);
                case WebhookEventType.InvoiceSettled:
                    return new WebhookInvoiceSettledEvent(WebhookEventType.InvoiceSettled);
                case WebhookEventType.InvoiceInvalid:
                    return new WebhookInvoiceInvalidEvent(WebhookEventType.InvoiceInvalid);
                default:
                    return new WebhookInvoiceEvent(WebhookEventType.InvoiceCreated);
            }
        }

        public static WebhookInvoiceEvent? GetWebhookEvent(InvoiceEvent invoiceEvent)
        {
            var eventCode = invoiceEvent.EventCode;
            switch (eventCode)
            {
                case InvoiceEventCode.Completed:
                case InvoiceEventCode.PaidAfterExpiration:
                    return null;
                case InvoiceEventCode.Confirmed:
                case InvoiceEventCode.MarkedCompleted:
                    return new WebhookInvoiceSettledEvent(WebhookEventType.InvoiceSettled)
                    {
                        ManuallyMarked = eventCode == InvoiceEventCode.MarkedCompleted
                    };
                case InvoiceEventCode.Created:
                    return new WebhookInvoiceEvent(WebhookEventType.InvoiceCreated);
                case InvoiceEventCode.Expired:
                    return new WebhookInvoiceExpiredEvent(WebhookEventType.InvoiceExpired)
                    {
                        PartiallyPaid = invoiceEvent.PaidPartial
                    };
                case InvoiceEventCode.FailedToConfirm:
                case InvoiceEventCode.MarkedInvalid:
                    return new WebhookInvoiceInvalidEvent(WebhookEventType.InvoiceInvalid)
                    {
                        ManuallyMarked = eventCode == InvoiceEventCode.MarkedInvalid
                    };
                case InvoiceEventCode.PaidInFull:
                    return new WebhookInvoiceProcessingEvent(WebhookEventType.InvoiceProcessing)
                    {
                        OverPaid = invoiceEvent.Invoice.ExceptionStatus == InvoiceExceptionStatus.PaidOver,
                    };
                case InvoiceEventCode.ReceivedPayment:
                    return new WebhookInvoiceReceivedPaymentEvent(WebhookEventType.InvoiceReceivedPayment)
                    {
                        AfterExpiration = invoiceEvent.Invoice.Status.ToModernStatus() == InvoiceStatus.Expired || invoiceEvent.Invoice.Status.ToModernStatus() == InvoiceStatus.Invalid,
                        PaymentMethod = invoiceEvent.Payment.GetPaymentMethodId().ToStringNormalized(),
                        Payment = GreenfieldInvoiceController.ToPaymentModel(invoiceEvent.Invoice, invoiceEvent.Payment)
                    };
                case InvoiceEventCode.PaymentSettled:
                    return new WebhookInvoiceReceivedPaymentEvent(WebhookEventType.InvoicePaymentSettled)
                    {
                        AfterExpiration = invoiceEvent.Invoice.Status.ToModernStatus() == InvoiceStatus.Expired || invoiceEvent.Invoice.Status.ToModernStatus() == InvoiceStatus.Invalid,
                        PaymentMethod = invoiceEvent.Payment.GetPaymentMethodId().ToStringNormalized(),
                        Payment = GreenfieldInvoiceController.ToPaymentModel(invoiceEvent.Invoice, invoiceEvent.Payment),
                        OverPaid = invoiceEvent.Invoice.ExceptionStatus == InvoiceExceptionStatus.PaidOver,
                    };
                default:
                    return null;
            }
        }

        private async Task Process(WebhookDeliveryRequest ctx, CancellationToken cancellationToken)
        {
            try
            {
                var wh = (await StoreRepository.GetWebhook(ctx.WebhookId))?.GetBlob();
                if (wh is null || !ShouldDeliver(ctx.WebhookEvent.Type, wh))
                    return;
                var result = await SendAndSaveDelivery(ctx, cancellationToken);
                if (ctx.WebhookBlob.AutomaticRedelivery &&
                    !result.Success &&
                    result.DeliveryId is not null)
                {
                    var originalDeliveryId = result.DeliveryId;
                    foreach (var wait in new[]
                    {
                            TimeSpan.FromSeconds(10),
                            TimeSpan.FromMinutes(1),
                            TimeSpan.FromMinutes(10),
                            TimeSpan.FromMinutes(10),
                            TimeSpan.FromMinutes(10),
                            TimeSpan.FromMinutes(10),
                            TimeSpan.FromMinutes(10),
                            TimeSpan.FromMinutes(10),
                        })
                    {
                        await Task.Delay(wait, cancellationToken);
                        ctx = (await CreateRedeliveryRequest(originalDeliveryId))!;
                        // This may have changed
                        if (ctx is null || !ctx.WebhookBlob.AutomaticRedelivery ||
                            !ShouldDeliver(ctx.WebhookEvent.Type, ctx.WebhookBlob))
                            return;
                        result = await SendAndSaveDelivery(ctx, cancellationToken);
                        if (result.Success)
                            return;
                    }
                }
            }
            catch when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Logs.PayServer.LogError(ex, "Unexpected error when processing a webhook");
            }
        }

        private static bool ShouldDeliver(WebhookEventType type, WebhookBlob wh)
        {
            return wh.Active && wh.AuthorizedEvents.Match(type);
        }

        public class DeliveryResult
        {
            public string? DeliveryId { get; set; }
            public bool Success { get; set; }
            public string? ErrorMessage { get; set; }
        }

        private async Task<DeliveryResult> SendDelivery(WebhookDeliveryRequest ctx, CancellationToken cancellationToken)
        {
            var uri = new Uri(ctx.WebhookBlob.Url, UriKind.Absolute);
            var httpClient = GetClient(uri);
            using var request = new HttpRequestMessage();
            request.RequestUri = uri;
            request.Method = HttpMethod.Post;
            byte[] bytes = ToBytes(ctx.WebhookEvent);
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var hmac = new System.Security.Cryptography.HMACSHA256(UTF8.GetBytes(ctx.WebhookBlob.Secret ?? string.Empty));
            var sig = Encoders.Hex.EncodeData(hmac.ComputeHash(bytes));
            content.Headers.Add("BTCPay-Sig", $"sha256={sig}");
            request.Content = content;
            var deliveryBlob = ctx.Delivery.Blob is null ? new WebhookDeliveryBlob() : ctx.Delivery.GetBlob();
            deliveryBlob.Request = bytes;
            try
            {
                using var response = await httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    deliveryBlob.Status = WebhookDeliveryStatus.HttpError;
                    deliveryBlob.ErrorMessage = $"HTTP Error Code {(int)response.StatusCode}";
                }
                else
                {
                    deliveryBlob.Status = WebhookDeliveryStatus.HttpSuccess;
                }
                deliveryBlob.HttpCode = (int)response.StatusCode;
            }
            catch (Exception ex) when (!CancellationToken.IsCancellationRequested)
            {
                deliveryBlob.Status = WebhookDeliveryStatus.Failed;
                deliveryBlob.ErrorMessage = ex.Message;
            }
            ctx.Delivery.SetBlob(deliveryBlob);

            return new DeliveryResult()
            {
                Success = deliveryBlob.ErrorMessage is null,
                DeliveryId = ctx.Delivery.Id,
                ErrorMessage = deliveryBlob.ErrorMessage
            };
        }


        private async Task<DeliveryResult> SendAndSaveDelivery(WebhookDeliveryRequest ctx, CancellationToken cancellationToken)
        {
            var result = await SendDelivery(ctx, cancellationToken);
            await StoreRepository.AddWebhookDelivery(ctx.Delivery);

            return result;
        }

        private byte[] ToBytes(WebhookEvent webhookEvent)
        {
            var str = JsonConvert.SerializeObject(webhookEvent, Formatting.Indented, DefaultSerializerSettings);
            var bytes = UTF8.GetBytes(str);
            return bytes;
        }

        private static Data.WebhookDeliveryData NewDelivery(string webhookId)
        {
            return new Data.WebhookDeliveryData
            {
                Id = Encoders.Base58.EncodeData(RandomUtils.GetBytes(16)),
                Timestamp = DateTimeOffset.UtcNow,
                WebhookId = webhookId
            };
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            var stopping = _processingQueue.Abort(cancellationToken);
            await base.StopAsync(cancellationToken);
            await stopping;
        }
    }
}
