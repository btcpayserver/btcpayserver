using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client;

public partial class BTCPayServerClient
{
    public virtual async Task<LightningAddressData[]> GetStoreLightningAddresses(string storeId,
        CancellationToken token = default)
    {
        return await SendHttpRequest<LightningAddressData[]>($"api/v1/stores/{storeId}/lightning-addresses", null, HttpMethod.Get, token);
    }

    public virtual async Task<LightningAddressData> GetStoreLightningAddress(string storeId, string username,
        CancellationToken token = default)
    {
        return await SendHttpRequest<LightningAddressData>($"api/v1/stores/{storeId}/lightning-addresses/{username}", null, HttpMethod.Get, token);
    }

    public virtual async Task RemoveStoreLightningAddress(string storeId, string username,
        CancellationToken token = default)
    {
        await SendHttpRequest($"api/v1/stores/{storeId}/lightning-addresses/{username}", null, HttpMethod.Delete, token);
    }

    public virtual async Task<LightningAddressData> AddOrUpdateStoreLightningAddress(string storeId,
        string username, LightningAddressData data,
        CancellationToken token = default)
    {
        return await SendHttpRequest<LightningAddressData>($"api/v1/stores/{storeId}/lightning-addresses/{username}", data, HttpMethod.Post, token);
    }
}
