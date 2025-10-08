#nullable enable
using System.Collections.Generic;
using BTCPayServer.Client.Models;

namespace BTCPayServer.HostedServices.Webhooks;

public interface IWebhookProvider
{
    public bool SupportsCustomerEmail { get; }

    public Dictionary<string, string> GetSupportedWebhookTypes();

    public WebhookEvent CreateTestEvent(string type, params object[] args);
}
