#nullable  enable
using BTCPayServer.Client.Models;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Plugins.Webhooks;

public record WebhookTriggerContext(StoreData Store, object Event, StoreWebhookEvent Webhook)
{
    public object? AdditionalData { get; set; }
}

public record WebhookTriggerContext<T> : WebhookTriggerContext where T : class
{
    public WebhookTriggerContext(StoreData store, T evt, StoreWebhookEvent webhook) : base(store, evt, webhook)
    {
    }

    public new T Event => (T)base.Event;
}
