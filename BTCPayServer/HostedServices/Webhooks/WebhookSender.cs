#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Services.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin.DataEncoders;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WebhookDeliveryData = BTCPayServer.Data.WebhookDeliveryData;

namespace BTCPayServer.HostedServices.Webhooks;

/// <summary>
///     This class sends webhook notifications
///     It also makes sure the events sent to a webhook are sent in order to the webhook
/// </summary>
public class WebhookSender(
    StoreRepository storeRepository,
    IHttpClientFactory httpClientFactory,
    ApplicationDbContextFactory dbContextFactory,
    ILogger<WebhookSender> logger,
    IServiceProvider serviceProvider)
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
        var newDelivery = WebhookExtensions.NewWebhookDelivery(webhookDelivery.Webhook.Id);
        WebhookDeliveryBlob newDeliveryBlob = new();
        newDeliveryBlob.Request = oldDeliveryBlob.Request;
        var webhookEvent = newDeliveryBlob.ReadRequestAs<WebhookEvent>();
        if (webhookEvent.IsPruned())
            return null;
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

    private WebhookEvent GetTestWebHook(string storeId, string webhookId, string webhookEventType,
        WebhookDeliveryData delivery)
    {
        var webhookProvider = serviceProvider.GetServices<IWebhookProvider>()
            .FirstOrDefault(provider => provider.GetSupportedWebhookTypes().ContainsKey(webhookEventType));

        if (webhookProvider is null)
            throw new ArgumentException($"Unknown webhook event type {webhookEventType}", webhookEventType);

        var webhookEvent = webhookProvider.CreateTestEvent(webhookEventType, storeId);
        if (webhookEvent is null)
            throw new ArgumentException("Webhook provider does not support tests");

        webhookEvent.DeliveryId = delivery.Id;
        webhookEvent.WebhookId = webhookId;
        webhookEvent.OriginalDeliveryId = "__test__" + Guid.NewGuid() + "__test__";
        webhookEvent.IsRedelivery = false;
        webhookEvent.Timestamp = delivery.Timestamp;

        return webhookEvent;
    }

    public async Task<DeliveryResult> TestWebhook(string storeId, string webhookId, string webhookEventType,
        CancellationToken cancellationToken)
    {
        var delivery = WebhookExtensions.NewWebhookDelivery(webhookId);
        var webhook = (await StoreRepository.GetWebhooks(storeId)).FirstOrDefault(w => w.Id == webhookId);
        WebhookDeliveryRequest deliveryRequest = new(
            webhookId,
            GetTestWebHook(storeId, webhookId, webhookEventType, delivery),
            delivery,
            webhook.GetBlob()
        );
        return await SendDelivery(deliveryRequest, cancellationToken);
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
                !result.Success &&
                result.DeliveryId is not null)
            {
                var originalDeliveryId = result.DeliveryId;
                foreach (var wait in new[]
                         {
                             TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10),
                             TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10)
                         })
                {
                    await Task.Delay(wait, cancellationToken);
                    ctx = (await CreateRedeliveryRequest(originalDeliveryId))!;
                    // This may have changed
                    if (ctx is null || !ctx.WebhookBlob.AutomaticRedelivery ||
                        !ctx.WebhookBlob.ShouldDeliver(ctx.WebhookEvent.Type))
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

        return new DeliveryResult { Success = deliveryBlob.ErrorMessage is null, DeliveryId = ctx.Delivery.Id, ErrorMessage = deliveryBlob.ErrorMessage };
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

    public async Task<UIStoresController.StoreEmailRule[]> GetEmailRules(string storeId,
        string type)
    {
        return (await StoreRepository.FindStore(storeId))?.GetStoreBlob().EmailRules?.Where(rule => rule.Trigger == type).ToArray() ??
               Array.Empty<UIStoresController.StoreEmailRule>();
    }

    public Dictionary<string, string> GetSupportedWebhookTypes()
    {
        return serviceProvider.GetServices<IWebhookProvider>()
            .SelectMany(provider => provider.GetSupportedWebhookTypes()).ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    public Dictionary<string, bool> GetWebhookTypesSupportedByCustomerEmail()
    {
        return serviceProvider.GetServices<IWebhookProvider>()
            .SelectMany(provider => provider.GetSupportedWebhookTypes()
                .Select(pair => new { pair.Key, Value = provider.SupportsCustomerEmail }))
            .ToDictionary(x => x.Key, x => x.Value);
    }

    public class WebhookDeliveryRequest(
        string webhookId,
        WebhookEvent webhookEvent,
        WebhookDeliveryData delivery,
        WebhookBlob webhookBlob)
    {
        // Regex pattern to validate JSONPath: alphanumeric, underscore, dot, hyphen, square brackets, asterisk, single/double quotes
        private static readonly Regex _jsonPathRegex = new(@"^[a-zA-Z0-9_\.\-\[\]\*'""]*$", RegexOptions.Compiled);
        public WebhookEvent WebhookEvent { get; } = webhookEvent;
        public WebhookDeliveryData Delivery { get; } = delivery;
        public WebhookBlob WebhookBlob { get; } = webhookBlob;
        public string WebhookId { get; } = webhookId;

        public virtual Task<SendEmailRequest?> Interpolate(SendEmailRequest req,
            UIStoresController.StoreEmailRule storeEmailRule)
        {
            return Task.FromResult(req)!;
        }

        protected static string InterpolateJsonField(string str, string fieldName, JObject obj)
        {
            if (string.IsNullOrEmpty(str) || string.IsNullOrEmpty(fieldName) || obj == null)
                return str;

            fieldName += ".";

            //find all instance of {fieldName*} in str, then run obj.SelectToken(*) on it
            while (true)
            {
                var start = str.IndexOf($"{{{fieldName}", StringComparison.InvariantCultureIgnoreCase);
                if (start == -1)
                    break;

                start += fieldName.Length + 1; // Move past the {
                var end = str.IndexOf("}", start, StringComparison.InvariantCultureIgnoreCase);
                if (end == -1)
                    break;

                var jsonpath = str.Substring(start, end - start);
                var result = string.Empty;

                try
                {
                    if (string.IsNullOrEmpty(jsonpath))
                        result = obj.ToString();
                    else if (_jsonPathRegex.IsMatch(jsonpath))
                        // Only process if JSONPath is valid
                        result = obj.SelectToken(jsonpath)?.ToString() ?? string.Empty;
                    // If jsonpath doesn't match the pattern, result remains empty string
                }
                catch (JsonException)
                {
                    // Handle JSON parsing errors (e.g., invalid JSONPath syntax)
                    result = string.Empty;
                }

                str = str.Replace($"{{{fieldName}{jsonpath}}}", result);
            }

            return str;
        }
    }

    public class DeliveryResult
    {
        public string? DeliveryId { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
