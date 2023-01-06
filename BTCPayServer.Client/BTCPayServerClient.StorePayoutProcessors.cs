#nullable enable
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client
{
    public partial class BTCPayServerClient
    {
        public virtual async Task<IEnumerable<PayoutProcessorData>> GetPayoutProcessors(string storeId,
            CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/payout-processors"), token);
            return await HandleResponse<IEnumerable<PayoutProcessorData>>(response);
        }
        public virtual async Task RemovePayoutProcessor(string storeId, string processor, string paymentMethod, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/payout-processors/{processor}/{paymentMethod}", null, HttpMethod.Delete), token);
            await HandleResponse(response);
        }

        public virtual async Task<IEnumerable<LightningAutomatedPayoutSettings>> GetStoreLightningAutomatedPayoutProcessors(string storeId, string? paymentMethod = null,
            CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/payout-processors/LightningAutomatedPayoutSenderFactory{(paymentMethod is null ? string.Empty : $"/{paymentMethod}")}"), token);
            return await HandleResponse<IEnumerable<LightningAutomatedPayoutSettings>>(response);
        }
        public virtual async Task<LightningAutomatedPayoutSettings> UpdateStoreLightningAutomatedPayoutProcessors(string storeId, string paymentMethod, LightningAutomatedPayoutSettings request, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/payout-processors/LightningAutomatedPayoutSenderFactory/{paymentMethod}", null, request, HttpMethod.Put), token);
            return await HandleResponse<LightningAutomatedPayoutSettings>(response);
        }
        public virtual async Task<OnChainAutomatedPayoutSettings> UpdateStoreOnChainAutomatedPayoutProcessors(string storeId, string paymentMethod, OnChainAutomatedPayoutSettings request, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/payout-processors/OnChainAutomatedPayoutSenderFactory/{paymentMethod}", null, request, HttpMethod.Put), token);
            return await HandleResponse<OnChainAutomatedPayoutSettings>(response);
        }

        public virtual async Task<IEnumerable<OnChainAutomatedPayoutSettings>> GetStoreOnChainAutomatedPayoutProcessors(string storeId, string? paymentMethod = null,
            CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/payout-processors/OnChainAutomatedPayoutSenderFactory{(paymentMethod is null ? string.Empty : $"/{paymentMethod}")}"), token);
            return await HandleResponse<IEnumerable<OnChainAutomatedPayoutSettings>>(response);
        }
    }
}
