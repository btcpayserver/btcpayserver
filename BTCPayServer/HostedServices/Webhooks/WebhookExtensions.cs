using System;
using System.Net.Http;
using BTCPayServer.Data;
using BTCPayServer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.HostedServices.Webhooks;

public static class WebhookExtensions
{
    public static Data.WebhookDeliveryData NewWebhookDelivery(string webhookId)
    {
        return new Data.WebhookDeliveryData
        {
            Id = Encoders.Base58.EncodeData(RandomUtils.GetBytes(16)),
            Timestamp = DateTimeOffset.UtcNow,
            WebhookId = webhookId
        };
    }

    public static bool ShouldDeliver(this WebhookBlob wh, string type)
    {
        return wh.Active && wh.AuthorizedEvents.Match(type);
    }

    public static IServiceCollection AddWebhooks(this IServiceCollection services)
    {
        services.AddSingleton<InvoiceWebhookProvider>();
        services.AddSingleton<IWebhookProvider>(o => o.GetRequiredService<InvoiceWebhookProvider>());
        services.AddHostedService(o => o.GetRequiredService<InvoiceWebhookProvider>());

        services.AddSingleton<PayoutWebhookProvider>();
        services.AddSingleton<IWebhookProvider>(o => o.GetRequiredService<PayoutWebhookProvider>());
        services.AddHostedService(o => o.GetRequiredService<PayoutWebhookProvider>());
        
        services.AddSingleton<PaymentRequestWebhookProvider>();
        services.AddSingleton<IWebhookProvider>(o => o.GetRequiredService<PaymentRequestWebhookProvider>());
        services.AddHostedService(o => o.GetRequiredService<PaymentRequestWebhookProvider>());
        
        services.AddSingleton<PendingTransactionWebhookProvider>();
        services.AddSingleton<IWebhookProvider>(o => o.GetRequiredService<PendingTransactionWebhookProvider>());
        services.AddHostedService(o => o.GetRequiredService<PendingTransactionWebhookProvider>());

        services.AddSingleton<WebhookSender>();
        services.AddSingleton<IHostedService, WebhookSender>(o => o.GetRequiredService<WebhookSender>());
        services.AddScheduledTask<CleanupWebhookDeliveriesTask>(TimeSpan.FromHours(6.0));
        services.AddHttpClient(WebhookSender.OnionNamedClient)
            .ConfigurePrimaryHttpMessageHandler<Socks5HttpClientHandler>();
        services.AddHttpClient(WebhookSender.LoopbackNamedClient)
            .ConfigurePrimaryHttpMessageHandler(_ => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });
        return services;
    }
}
