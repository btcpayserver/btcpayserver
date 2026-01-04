#nullable  enable
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.HostedServices;
using BTCPayServer.Plugins.Emails;
using BTCPayServer.Plugins.Emails.HostedServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Plugins.Webhooks;

public abstract class WebhookTriggerProvider
{
    public abstract Task<StoreWebhookEvent?> GetWebhookEventAsync(object evt);

    public virtual Task<JObject> GetEmailModel(WebhookTriggerContext webhookTriggerContext)
    {
        var webhookData = JObject.FromObject(webhookTriggerContext.Webhook, JsonSerializer.Create(WebhookSender.DefaultSerializerSettings));
        var model = new JObject();
        model["Webhook"] = webhookData;
        model["Store"] = new JObject()
        {
            ["Id"] = webhookTriggerContext.Store.Id,
            ["Name"] = webhookTriggerContext.Store.StoreName
        };
        return Task.FromResult(model);
    }

    public virtual Task BeforeSending(EmailRuleMatchContext context, WebhookTriggerContext webhookTriggerContext)
        => Task.CompletedTask;

    public virtual WebhookTriggerContext CreateWebhookTriggerContext(StoreData store, object evt, StoreWebhookEvent webhookEvent)
    => new (store, evt, webhookEvent);
}

public abstract class WebhookTriggerProvider<T> : WebhookTriggerProvider where T : class
{
    public sealed override async Task<StoreWebhookEvent?> GetWebhookEventAsync(object evt)
        => evt is T t ? await GetWebhookEventAsync(t) : null;

    protected virtual Task<StoreWebhookEvent?> GetWebhookEventAsync(T evt)
    => Task.FromResult<StoreWebhookEvent?>(GetWebhookEvent(evt));

    protected virtual StoreWebhookEvent? GetWebhookEvent(T evt) => null;

    public sealed override Task<JObject> GetEmailModel(WebhookTriggerContext webhookTriggerContext)
    => GetEmailModel((WebhookTriggerContext<T>)webhookTriggerContext);
    protected virtual Task<JObject> GetEmailModel(WebhookTriggerContext<T> webhookTriggerContext)
        => base.GetEmailModel(webhookTriggerContext);

    public sealed override Task BeforeSending(EmailRuleMatchContext context, WebhookTriggerContext webhookTriggerContext)
        => BeforeSending(context,(WebhookTriggerContext<T>)webhookTriggerContext);

    protected virtual Task BeforeSending(EmailRuleMatchContext context, WebhookTriggerContext<T> webhookTriggerContext)
        => Task.CompletedTask;

    public override WebhookTriggerContext CreateWebhookTriggerContext(StoreData store, object evt, StoreWebhookEvent webhookEvent)
        => new WebhookTriggerContext<T>(store, (T)evt, webhookEvent);
}
