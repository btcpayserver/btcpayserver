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
        public virtual async Task<OnChainWalletObjectData> GetOnChainWalletObject(string storeId, string cryptoCode, OnChainWalletObjectId objectId, bool? includeNeighbourData = null, CancellationToken token = default)
        {
            Dictionary<string, object> parameters = new Dictionary<string, object>();
            if (includeNeighbourData is bool v)
                parameters.Add("includeNeighbourData", v);
            var response =
            await _httpClient.SendAsync(
            CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}/wallet/objects/{objectId.Type}/{objectId.Id}", parameters, method: HttpMethod.Get), token);
            try
            {
                return await HandleResponse<OnChainWalletObjectData>(response);
            }
            catch (GreenfieldAPIException err) when (err.APIError.Code == "wallet-object-not-found")
            {
                return null;
            }
        }
        public virtual async Task<OnChainWalletObjectData[]> GetOnChainWalletObjects(string storeId, string cryptoCode, GetWalletObjectsRequest query = null, CancellationToken token = default)
        {
            Dictionary<string, object> parameters = new Dictionary<string, object>();
            if (query?.Type is string s)
                parameters.Add("type", s);
            if (query?.Ids is string[] ids)
                parameters.Add("ids", ids);
            if (query?.IncludeNeighbourData is bool v)
                parameters.Add("includeNeighbourData", v);
            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}/wallet/objects", parameters, method: HttpMethod.Get), token);
            return await HandleResponse<OnChainWalletObjectData[]>(response);
        }
        public virtual async Task RemoveOnChainWalletObject(string storeId, string cryptoCode, OnChainWalletObjectId objectId,
            CancellationToken token = default)
        {
            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}/wallet/objects/{objectId.Type}/{objectId.Id}", method: HttpMethod.Delete), token);
            await HandleResponse(response);
        }
        public virtual async Task<OnChainWalletObjectData> AddOrUpdateOnChainWalletObject(string storeId, string cryptoCode, AddOnChainWalletObjectRequest request,
            CancellationToken token = default)
        {
            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}/wallet/objects", method: HttpMethod.Post, bodyPayload: request), token);
            return await HandleResponse<OnChainWalletObjectData>(response);
        }
        public virtual async Task AddOrUpdateOnChainWalletLink(string storeId, string cryptoCode,
            OnChainWalletObjectId objectId,
            AddOnChainWalletObjectLinkRequest request = null,
            CancellationToken token = default)
        {
            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}/wallet/objects/{objectId.Type}/{objectId.Id}/links", method: HttpMethod.Post, bodyPayload: request), token);
            await HandleResponse(response);
        }
        public virtual async Task RemoveOnChainWalletLinks(string storeId, string cryptoCode,
            OnChainWalletObjectId objectId,
            OnChainWalletObjectId link,
            CancellationToken token = default)
        {
            var response =
                await _httpClient.SendAsync(
                    CreateHttpRequest($"api/v1/stores/{storeId}/payment-methods/onchain/{cryptoCode}/wallet/objects/{objectId.Type}/{objectId.Id}/links/{link.Type}/{link.Id}", method: HttpMethod.Delete), token);
            await HandleResponse(response);
        }
    }
}
