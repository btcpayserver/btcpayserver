using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client;

public partial class BTCPayServerClient
{
    public virtual async Task<OnChainWalletObjectData> GetOnChainWalletObject(string storeId, string cryptoCode, OnChainWalletObjectId objectId, bool? includeNeighbourData = null, CancellationToken token = default)
    {
        var parameters = new Dictionary<string, object>();
        if (includeNeighbourData is bool v)
            parameters.Add("includeNeighbourData", v);
        try
        {
            return await SendHttpRequest<OnChainWalletObjectData>($"api/v1/stores/{storeId}/payment-methods/{cryptoCode}-CHAIN/wallet/objects/{objectId.Type}/{objectId.Id}", parameters, HttpMethod.Get, token);
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
        return await SendHttpRequest<OnChainWalletObjectData[]>($"api/v1/stores/{storeId}/payment-methods/{cryptoCode}-CHAIN/wallet/objects", parameters, HttpMethod.Get, token);
    }
    public virtual async Task RemoveOnChainWalletObject(string storeId, string cryptoCode, OnChainWalletObjectId objectId,
        CancellationToken token = default)
    {
        await SendHttpRequest($"api/v1/stores/{storeId}/payment-methods/{cryptoCode}-CHAIN/wallet/objects/{objectId.Type}/{objectId.Id}", null, HttpMethod.Delete, token);
    }
    public virtual async Task<OnChainWalletObjectData> AddOrUpdateOnChainWalletObject(string storeId, string cryptoCode, AddOnChainWalletObjectRequest request,
        CancellationToken token = default)
    {
        return await SendHttpRequest<OnChainWalletObjectData>($"api/v1/stores/{storeId}/payment-methods/{cryptoCode}-CHAIN/wallet/objects", request, HttpMethod.Post, token);
    }

    public virtual async Task AddOrUpdateOnChainWalletLink(string storeId, string cryptoCode,
        OnChainWalletObjectId objectId,
        AddOnChainWalletObjectLinkRequest request = null,
        CancellationToken token = default)
    {
        await SendHttpRequest($"api/v1/stores/{storeId}/payment-methods/{cryptoCode}-CHAIN/wallet/objects/{objectId.Type}/{objectId.Id}/links", request, HttpMethod.Post, token);
    }

    public virtual async Task RemoveOnChainWalletLinks(string storeId, string cryptoCode,
        OnChainWalletObjectId objectId,
        OnChainWalletObjectId link,
        CancellationToken token = default)
    {
        await SendHttpRequest($"api/v1/stores/{storeId}/payment-methods/{cryptoCode}-CHAIN/wallet/objects/{objectId.Type}/{objectId.Id}/links/{link.Type}/{link.Id}", null, HttpMethod.Delete, token);
    }
}
