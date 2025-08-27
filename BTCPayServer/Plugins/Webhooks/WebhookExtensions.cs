using System;
using System.Collections.Generic;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Emails.Views;
using BTCPayServer.Plugins.Webhooks;
using BTCPayServer.Plugins.Webhooks.Views;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer;

public static class WebhookExtensions
{
    public static IServiceCollection AddWebhookTriggerProvider<T>(this IServiceCollection services) where T : WebhookTriggerProvider
    {
        services.AddSingleton<T>();
        services.AddSingleton<WebhookTriggerProvider>(o => o.GetRequiredService<T>());
        return services;
    }
    public static IServiceCollection AddWebhookTriggerViewModels(this IServiceCollection services, IEnumerable<EmailTriggerViewModel> viewModels)
    {
        foreach(var trigger in viewModels)
        {
            var webhookType = trigger.Trigger;
            if (trigger.Trigger.StartsWith("WH-"))
                throw new ArgumentException("Webhook type cannot start with WH-");
            trigger.Trigger = EmailRuleData.GetWebhookTriggerName(trigger.Trigger);
            services.AddSingleton(new AvailableWebhookViewModel(webhookType, trigger.Description));
            services.AddSingleton(trigger);
        }
        return services;
    }
}
