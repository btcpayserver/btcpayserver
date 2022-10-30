using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using NBitcoin;

namespace BTCPayServer.Client
{
    public partial class BTCPayServerClient
    {
        public virtual async Task<OnChainWalletObjectData[]> GetOnChainWalletObjects(string storeId, string cryptoCode,  OnChainWalletObjectQuery query,
            CancellationToken token = default)
        {
            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}/wallet/objects", method:HttpMethod.Get, bodyPayload: query), token);
            return await HandleResponse<OnChainWalletObjectData[]>(response);
        }
        public virtual async Task RemoveOnChainWalletObjects(string storeId, string cryptoCode,  OnChainWalletObjectQuery query,
            CancellationToken token = default)
        {
            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}/wallet/objects", method:HttpMethod.Delete, bodyPayload: query), token);
            await HandleResponse(response);
        }
        public virtual async Task AddOrUpdateOnChainWalletObjects(string storeId, string cryptoCode,  OnChainWalletObjectData[] request,
            CancellationToken token = default)
        {
            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}/wallet/objects", method:HttpMethod.Post, bodyPayload: request), token);
            await HandleResponse(response);
        }
        public virtual async Task AddOrUpdateOnChainWalletLinks(string storeId, string cryptoCode,  AddOnChainWalletObjectLinkRequest[] request,
            CancellationToken token = default)
        {
            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}/wallet/object-links", method:HttpMethod.Post, bodyPayload: request), token);
            await HandleResponse(response);
        }
        public virtual async Task RemoveOnChainWalletLinks(string storeId, string cryptoCode,
            RemoveOnChainWalletObjectLinkRequest[] request,
            CancellationToken token = default)
        {
            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}/wallet/object-links", method:HttpMethod.Delete, bodyPayload: request), token);
            await HandleResponse(response);
        }
    }
}
