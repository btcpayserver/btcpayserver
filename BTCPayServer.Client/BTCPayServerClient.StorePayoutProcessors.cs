#nullable enable
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client;

public partial class BTCPayServerClient
{
    public virtual async Task<IEnumerable<PayoutProcessorData>> GetPayoutProcessors(string storeId, CancellationToken token = default)
    {
        return await SendHttpRequest<IEnumerable<PayoutProcessorData>>($"api/v1/stores/{storeId}/payout-processors", null, HttpMethod.Get, token);
    }

    public virtual async Task RemovePayoutProcessor(string storeId, string processor, string paymentMethod, CancellationToken token = default)
    {
        await SendHttpRequest($"api/v1/stores/{storeId}/payout-processors/{processor}/{paymentMethod}", null, HttpMethod.Delete, token);
    }

    public virtual async Task<IEnumerable<LightningAutomatedPayoutSettings>> GetStoreLightningAutomatedPayoutProcessors(string storeId, string? payoutMethodId = null, CancellationToken token = default)
    {
        return await SendHttpRequest<IEnumerable<LightningAutomatedPayoutSettings>>($"api/v1/stores/{storeId}/payout-processors/LightningAutomatedPayoutSenderFactory{(payoutMethodId is null ? string.Empty : $"/{payoutMethodId}")}", null, HttpMethod.Get, token);
    }

    public virtual async Task<LightningAutomatedPayoutSettings> UpdateStoreLightningAutomatedPayoutProcessors(string storeId, string payoutMethodId, LightningAutomatedPayoutSettings request, CancellationToken token = default)
    {
        return await SendHttpRequest<LightningAutomatedPayoutSettings>($"api/v1/stores/{storeId}/payout-processors/LightningAutomatedPayoutSenderFactory/{payoutMethodId}", request, HttpMethod.Put, token);
    }

    public virtual async Task<OnChainAutomatedPayoutSettings> UpdateStoreOnChainAutomatedPayoutProcessors(string storeId, string paymentMethod, OnChainAutomatedPayoutSettings request, CancellationToken token = default)
    {
        return await SendHttpRequest<OnChainAutomatedPayoutSettings>($"api/v1/stores/{storeId}/payout-processors/OnChainAutomatedPayoutSenderFactory/{paymentMethod}", request, HttpMethod.Put, token);
    }

    public virtual async Task<IEnumerable<OnChainAutomatedPayoutSettings>> GetStoreOnChainAutomatedPayoutProcessors(string storeId, string? paymentMethod = null, CancellationToken token = default)
    {
        return await SendHttpRequest<IEnumerable<OnChainAutomatedPayoutSettings>>($"api/v1/stores/{storeId}/payout-processors/OnChainAutomatedPayoutSenderFactory{(paymentMethod is null ? string.Empty : $"/{paymentMethod}")}", null, HttpMethod.Get, token);
    }
}
