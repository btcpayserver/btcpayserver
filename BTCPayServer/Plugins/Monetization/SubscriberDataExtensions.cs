#nullable enable

using BTCPayServer.Data.Subscriptions;

namespace BTCPayServer.Plugins.Monetization;

public static class SubscriberDataExtensions
{
    public const string IdentityType = "ApplicationUserId";
    public static string? GetApplicationUserId(this SubscriberData subscriber)
        => subscriber.Customer.GetContact(IdentityType);
}
