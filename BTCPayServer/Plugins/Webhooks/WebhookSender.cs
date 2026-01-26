#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin.DataEncoders;
using Newtonsoft.Json;
using WebhookDeliveryData = BTCPayServer.Data.WebhookDeliveryData;

namespace BTCPayServer.Plugins.Webhooks;

/// <summary>
///     This class sends webhook notifications
///     It also makes sure the events sent to a webhook are sent in order to the webhook
/// </summary>
public class WebhookSender(
    StoreRepository storeRepository,
    IHttpClientFactory httpClientFactory,
    ApplicationDbContextFactory dbContextFactory,
    ILogger<WebhookSender> logger)
    : IHostedService
{
    public const string OnionNamedClient = "greenfield-webhook.onion";
    public const string ClearnetNamedClient = "greenfield-webhook.clearnet";
    public const string LoopbackNamedClient = "greenfield-webhook.loopback";
    public static string[] AllClients = new[] { OnionNamedClient, ClearnetNamedClient, LoopbackNamedClient };
    public static readonly JsonSerializerSettings DefaultSerializerSettings;


    private readonly MultiProcessingQueue _processingQueue = new();

    private readonly Encoding _utf8 = new UTF8Encoding(false);


    static WebhookSender()
    {
        DefaultSerializerSettings = WebhookEvent.DefaultSerializerSettings;
    }

    private StoreRepository StoreRepository { get; } = storeRepository;
    private IHttpClientFactory HttpClientFactory { get; } = httpClientFactory;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var stopping = _processingQueue.Abort(cancellationToken);
        await stopping;
    }


    private HttpClient GetClient(Uri uri)
    {
        return HttpClientFactory.CreateClient(uri.IsOnion() ? OnionNamedClient :
            uri.IsLoopback ? LoopbackNamedClient : ClearnetNamedClient);
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
        await using var ctx = dbContextFactory.CreateContext();
        var webhookDelivery = await ctx.WebhookDeliveries.AsNoTracking()
            .Where(o => o.Id == deliveryId)
            .Select(o => new { o.Webhook, Delivery = o })
            .FirstOrDefaultAsync();
        if (webhookDelivery is null)
            return null;
        var oldDeliveryBlob = webhookDelivery.Delivery.GetBlob();
        var newDelivery = WebhookDeliveryData.Create(webhookDelivery.Webhook.Id);
        WebhookDeliveryBlob newDeliveryBlob = new();
        newDeliveryBlob.Request = oldDeliveryBlob?.Request;
        if (newDeliveryBlob.IsPruned())
            return null;
        var webhookEvent = newDeliveryBlob.ReadRequestAs<WebhookEvent>();
        webhookEvent.DeliveryId = newDelivery.Id;
        webhookEvent.WebhookId = webhookDelivery.Webhook.Id;
        // if we redelivered a redelivery, we still want the initial delivery here
        webhookEvent.OriginalDeliveryId ??= deliveryId;
        webhookEvent.IsRedelivery = true;
        newDeliveryBlob.Request = ToBytes(webhookEvent);
        newDelivery.SetBlob(newDeliveryBlob);
        return new WebhookDeliveryRequest(webhookDelivery.Webhook.Id, webhookEvent, newDelivery,
            webhookDelivery.Webhook.GetBlob());
    }

    public void EnqueueDelivery(WebhookDeliveryRequest context)
    {
        _processingQueue.Enqueue(context.WebhookId, cancellationToken => Process(context, cancellationToken));
    }

    private async Task Process(WebhookDeliveryRequest ctx, CancellationToken cancellationToken)
    {
        try
        {
            var wh = (await StoreRepository.GetWebhook(ctx.WebhookId))?.GetBlob();
            if (wh is null || !wh.ShouldDeliver(ctx.WebhookEvent.Type))
                return;
            var result = await SendAndSaveDelivery(ctx, cancellationToken);
            if (ctx.WebhookBlob.AutomaticRedelivery &&
                result is { ShouldRetry: true, DeliveryId: not null })
            {
                var originalDeliveryId = result.DeliveryId;
                foreach (var wait in new[]
                         {
                             TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10),
                             TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10)
                         })
                {
                    await Task.Delay(wait, cancellationToken);
                    var localCtx = await CreateRedeliveryRequest(originalDeliveryId);
                    // This may have changed
                    if (localCtx is null || !localCtx.WebhookBlob.AutomaticRedelivery ||
                        !localCtx.WebhookBlob.ShouldDeliver(ctx.WebhookEvent.Type))
                        return;
                    ctx = localCtx;
                    result = await SendAndSaveDelivery(ctx, cancellationToken);
                    if (!result.ShouldRetry)
                        return;
                }
            }
        }
        catch when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error when processing a webhook");
        }
    }

    private async Task<DeliveryResult> SendDelivery(WebhookDeliveryRequest ctx, CancellationToken cancellationToken)
    {
        Uri uri = new(ctx.WebhookBlob.Url, UriKind.Absolute);
        var httpClient = GetClient(uri);
        using HttpRequestMessage request = new();
        request.RequestUri = uri;
        request.Method = HttpMethod.Post;
        var bytes = ToBytes(ctx.WebhookEvent);
        ByteArrayContent content = new(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using HMACSHA256 hmac = new(_utf8.GetBytes(ctx.WebhookBlob.Secret ?? string.Empty));
        var sig = Encoders.Hex.EncodeData(hmac.ComputeHash(bytes));
        content.Headers.Add("BTCPay-Sig", $"sha256={sig}");
        request.Content = content;
        var deliveryBlob = ctx.Delivery.GetBlob() ?? new WebhookDeliveryBlob();
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
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            deliveryBlob.Status = WebhookDeliveryStatus.Failed;
            deliveryBlob.ErrorMessage = ex.Message;
        }

        ctx.Delivery.SetBlob(deliveryBlob);

        return new DeliveryResult
        {
            ShouldRetry = deliveryBlob.HttpCode switch
            {
                // Retry on server errors (5xx)
                >= 500 and <= 599 => true,
                // Retry on rate limiting
                429 => true,
                // Retry on request timeout
                408 => true,
                // Retry when no HTTP code (network/connection errors)
                null => true,
                // Don't retry on client errors (4xx except 408, 429) or success (2xx, 3xx)
                _ => false
            },
            DeliveryId = ctx.Delivery.Id,
            ErrorMessage = deliveryBlob.ErrorMessage
        };
    }

    private async Task<DeliveryResult> SendAndSaveDelivery(WebhookDeliveryRequest ctx,
        CancellationToken cancellationToken)
    {
        var result = await SendDelivery(ctx, cancellationToken);
        await StoreRepository.AddWebhookDelivery(ctx.Delivery);

        return result;
    }

    private byte[] ToBytes(WebhookEvent webhookEvent)
    {
        var str = JsonConvert.SerializeObject(webhookEvent, Formatting.Indented, DefaultSerializerSettings);
        var bytes = _utf8.GetBytes(str);
        return bytes;
    }

    public async Task<WebhookData[]> GetWebhooks(string invoiceStoreId, string? webhookEventType)
    {
        return (await StoreRepository.GetWebhooks(invoiceStoreId)).Where(data => webhookEventType is null || data.GetBlob().ShouldDeliver(webhookEventType))
            .ToArray();
    }
    public class WebhookDeliveryRequest(
        string webhookId,
        WebhookEvent webhookEvent,
        WebhookDeliveryData delivery,
        WebhookBlob webhookBlob)
    {
        public WebhookEvent WebhookEvent { get; } = webhookEvent;
        public WebhookDeliveryData Delivery { get; } = delivery;
        public WebhookBlob WebhookBlob { get; } = webhookBlob;
        public string WebhookId { get; } = webhookId;
    }

    public class DeliveryResult
    {
        public string? DeliveryId { get; set; }
        public bool ShouldRetry { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
