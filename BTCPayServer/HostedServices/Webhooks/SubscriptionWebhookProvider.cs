using System;
using System.Collections.Generic;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.HostedServices.Webhooks;

public class SubscriptionWebhookProvider : IWebhookProvider
{
    public bool SupportsCustomerEmail { get; } = true;

    public Dictionary<string, string> GetSupportedWebhookTypes()
    {
        return new Dictionary<string, string>
        {
            { WebhookEventType.SubscriptionMemberActivated, "Subscription - Member activated" },
            { WebhookEventType.SubscriptionMemberDisabled, "Subscription - Member disabled" }
        };
    }

    public WebhookEvent CreateTestEvent(string type, params object[] args)
    {
        if (type == WebhookEventType.SubscriptionMemberActivated)
        {
            return new WebhookSubscriptionEvent.MemberActivated()
            {
                Member = CreateTestMember(),
                StoreId = "def"
            };
        }
        else
        {
            return new WebhookSubscriptionEvent.MemberDisabled()
            {
                Member = CreateTestMember(),
                StoreId = "def"
            };
        }
    }

    private SubscriptionMemberModel CreateTestMember()
    {
        return new SubscriptionMemberModel()
        {
            Customer = new CustomerModel() { Id = "test-customer-id", StoreId = "def", ExternalId = "test-external-id" },
            Plan = new SubscriptionPlanModel() { Id = "test-plan-id", Items = new()
            {
                new () { Id = "test-item-id", Quantity = 1.0m }
            }},
            PeriodEnd = DateTimeOffset.UtcNow.AddDays(30),
            TrialEnd = DateTimeOffset.UtcNow.AddDays(7),
            GracePeriodEnd = DateTimeOffset.UtcNow.AddDays(37),
            CanceledAt = null,
            IsActive = true,
            ForceDisabled = false
        };
    }
}
