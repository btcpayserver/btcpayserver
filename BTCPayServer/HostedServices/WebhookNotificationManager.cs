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
    public class WebhookNotificationManager : EventHostedServiceBase
    {
        readonly Encoding UTF8 = new UTF8Encoding(false);
        public readonly static JsonSerializerSettings DefaultSerializerSettings;
        static WebhookNotificationManager()
        {
            DefaultSerializerSettings = WebhookEvent.DefaultSerializerSettings;
        }
        public const string OnionNamedClient = "greenfield-webhook.onion";
        public const string ClearnetNamedClient = "greenfield-webhook.clearnet";
        private HttpClient GetClient(Uri uri)
        {
            return HttpClientFactory.CreateClient(uri.IsOnion() ? OnionNamedClient : ClearnetNamedClient);
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
        Dictionary<string, Channel<WebhookDeliveryRequest>> _InvoiceEventsByWebhookId = new Dictionary<string, Channel<WebhookDeliveryRequest>>();
        public StoreRepository StoreRepository { get; }
        public IHttpClientFactory HttpClientFactory { get; }

        public WebhookNotificationManager(EventAggregator eventAggregator,
            StoreRepository storeRepository,
            IHttpClientFactory httpClientFactory) : base(eventAggregator)
        {
            StoreRepository = storeRepository;
            HttpClientFactory = httpClientFactory;
        }

        protected override void SubscribeToEvents()
        {
            Subscribe<InvoiceEvent>();
        }

        public async Task<string> Redeliver(string deliveryId)
        {
            var deliveryRequest = await CreateRedeliveryRequest(deliveryId);
            if (deliveryRequest is null)
                return null;
            EnqueueDelivery(deliveryRequest);
            return deliveryRequest.Delivery.Id;
        }

        private async Task<WebhookDeliveryRequest> CreateRedeliveryRequest(string deliveryId)
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
            var newDelivery = NewDelivery();
            newDelivery.WebhookId = webhookDelivery.Webhook.Id;
            var newDeliveryBlob = new WebhookDeliveryBlob();
            newDeliveryBlob.Request = oldDeliveryBlob.Request;
            var webhookEvent = newDeliveryBlob.ReadRequestAs<WebhookEvent>();
            webhookEvent.DeliveryId = newDelivery.Id;
            webhookEvent.WebhookId = webhookDelivery.Webhook.Id;
            // if we redelivered a redelivery, we still want the initial delivery here
            webhookEvent.OrignalDeliveryId ??= deliveryId;
            webhookEvent.IsRedelivery = true;
            newDeliveryBlob.Request = ToBytes(webhookEvent);
            newDelivery.SetBlob(newDeliveryBlob);
            return new WebhookDeliveryRequest(webhookDelivery.Webhook.Id, webhookEvent, newDelivery, webhookDelivery.Webhook.GetBlob());
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
                    Data.WebhookDeliveryData delivery = NewDelivery();
                    delivery.WebhookId = webhook.Id;
                    webhookEvent.InvoiceId = invoiceEvent.InvoiceId;
                    webhookEvent.StoreId = invoiceEvent.Invoice.StoreId;
                    webhookEvent.DeliveryId = delivery.Id;
                    webhookEvent.WebhookId = webhook.Id;
                    webhookEvent.OrignalDeliveryId = delivery.Id;
                    webhookEvent.IsRedelivery = false;
                    webhookEvent.Timestamp = delivery.Timestamp;
                    var context = new WebhookDeliveryRequest(webhook.Id, webhookEvent, delivery, webhookBlob);
                    EnqueueDelivery(context);
                }
            }
        }

        private void EnqueueDelivery(WebhookDeliveryRequest context)
        {
            if (_InvoiceEventsByWebhookId.TryGetValue(context.WebhookId, out var channel))
            {
                if (channel.Writer.TryWrite(context))
                    return;
            }
            channel = Channel.CreateUnbounded<WebhookDeliveryRequest>();
            _InvoiceEventsByWebhookId.Add(context.WebhookId, channel);
            channel.Writer.TryWrite(context);
            _ = Process(context.WebhookId, channel);
        }

        private WebhookInvoiceEvent GetWebhookEvent(InvoiceEvent invoiceEvent)
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
                case InvoiceEventCode.ExpiredPaidPartial:
                    return new WebhookInvoiceExpiredEvent(WebhookEventType.InvoiceExpired)
                    {
                        PartiallyPaid = eventCode == InvoiceEventCode.ExpiredPaidPartial
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
                        AfterExpiration = invoiceEvent.Invoice.Status.ToModernStatus() == InvoiceStatus.Expired || invoiceEvent.Invoice.Status.ToModernStatus() == InvoiceStatus.Invalid
                    };
                default:
                    return null;
            }
        }

        private async Task Process(string id, Channel<WebhookDeliveryRequest> channel)
        {
            await foreach (var originalCtx in channel.Reader.ReadAllAsync())
            {
                try
                {
                    var ctx = originalCtx;
                    var wh = (await StoreRepository.GetWebhook(ctx.WebhookId))?.GetBlob();
                    if (wh is null || !ShouldDeliver(ctx.WebhookEvent.Type, wh))
                        continue;
                    var result = await SendDelivery(ctx);
                    if (ctx.WebhookBlob.AutomaticRedelivery &&
                        !result.Success &&
                        result.DeliveryId is string)
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
                            await Task.Delay(wait, CancellationToken);
                            ctx = await CreateRedeliveryRequest(originalDeliveryId);
                            // This may have changed
                            if (!ctx.WebhookBlob.AutomaticRedelivery ||
                                !ShouldDeliver(ctx.WebhookEvent.Type, ctx.WebhookBlob))
                                break;
                            result = await SendDelivery(ctx);
                            if (result.Success)
                                break;
                        }
                    }
                }
                catch when (CancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logs.PayServer.LogError(ex, "Unexpected error when processing a webhook");
                }
            }
        }

        private static bool ShouldDeliver(WebhookEventType type, WebhookBlob wh)
        {
            return wh.Active && wh.AuthorizedEvents.Match(type);
        }

        class DeliveryResult
        {
            public string DeliveryId { get; set; }
            public bool Success { get; set; }
        }
        private async Task<DeliveryResult> SendDelivery(WebhookDeliveryRequest ctx)
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
                using var response = await httpClient.SendAsync(request, CancellationToken);
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
            await StoreRepository.AddWebhookDelivery(ctx.Delivery);
            return new DeliveryResult() { Success = deliveryBlob.ErrorMessage is null, DeliveryId = ctx.Delivery.Id };
        }

        private byte[] ToBytes(WebhookEvent webhookEvent)
        {
            var str = JsonConvert.SerializeObject(webhookEvent, Formatting.Indented, DefaultSerializerSettings);
            var bytes = UTF8.GetBytes(str);
            return bytes;
        }

        private static Data.WebhookDeliveryData NewDelivery()
        {
            var delivery = new Data.WebhookDeliveryData();
            delivery.Id = Encoders.Base58.EncodeData(RandomUtils.GetBytes(16));
            delivery.Timestamp = DateTimeOffset.UtcNow;
            return delivery;
        }
    }
}
