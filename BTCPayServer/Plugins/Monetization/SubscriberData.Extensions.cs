#nullable enable
using BTCPayServer.Data.Subscriptions;

namespace BTCPayServer.Plugins.Monetization;

public static class SubscriberDataExtensions
{
    public static SubscriberMonetizationAdditionalData? GetMonetizationData(this SubscriberData sub)
        => sub.GetAdditionalData<SubscriberMonetizationAdditionalData>(SubscriberMonetizationAdditionalData.Key);
    public static void SetMonetizationData(this SubscriberData sub, SubscriberMonetizationAdditionalData data)
        => sub.SetAdditionalData(SubscriberMonetizationAdditionalData.Key, data);

}
