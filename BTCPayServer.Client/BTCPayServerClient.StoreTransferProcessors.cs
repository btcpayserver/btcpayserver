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
        public virtual async Task<IEnumerable<TransferProcessorData>> GetTransferProcessors(string storeId, 
            CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/transfer-processors"), token);
            return await HandleResponse<IEnumerable<TransferProcessorData>>(response);
        }
        public virtual async Task RemoveTransferProcessor(string storeId, string processor, string paymentMethod, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/transfer-processors/{processor}/{paymentMethod}", null, HttpMethod.Delete), token);
            await HandleResponse(response);
        }
        
        
        public virtual async Task<IEnumerable<LightningAutomatedTransferSettings>> GetStoreLightningAutomatedTransferProcessors(string storeId, string? paymentMethod = null,
            CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/transfer-processors/LightningAutomatedTransferSenderFactory{(paymentMethod is null? string.Empty: $"/{paymentMethod}")}"), token);
            return await HandleResponse<IEnumerable<LightningAutomatedTransferSettings>>(response);
        }
        public virtual async Task<LightningAutomatedTransferSettings> UpdateStoreLightningAutomatedTransferProcessors(string storeId, string paymentMethod,LightningAutomatedTransferSettings request, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/transfer-processors/LightningAutomatedTransferSenderFactory/{paymentMethod}",null, request, HttpMethod.Put ), token);
            return await HandleResponse<LightningAutomatedTransferSettings>(response);
        }
        public virtual async Task<OnChainAutomatedTransferSettings> UpdateStoreOnChainAutomatedTransferProcessors(string storeId, string paymentMethod,OnChainAutomatedTransferSettings request, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/transfer-processors/OnChainAutomatedTransferSenderFactory/{paymentMethod}",null, request, HttpMethod.Put ), token);
            return await HandleResponse<OnChainAutomatedTransferSettings>(response);
        }
        
        public virtual async Task<IEnumerable<OnChainAutomatedTransferSettings>> GetStoreOnChainAutomatedTransferProcessors(string storeId, string? paymentMethod = null,
            CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(CreateHttpRequest($"api/v1/stores/{storeId}/transfer-processors/OnChainAutomatedTransferSenderFactory{(paymentMethod is null? string.Empty: $"/{paymentMethod}")}"), token);
            return await HandleResponse<IEnumerable<OnChainAutomatedTransferSettings>>(response);
        }
    }
}
