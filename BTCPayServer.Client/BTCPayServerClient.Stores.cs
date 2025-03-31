using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client;

public partial class BTCPayServerClient
{
    public virtual async Task<IEnumerable<StoreData>> GetStores(CancellationToken token = default)
    {
        return await SendHttpRequest<IEnumerable<StoreData>>("api/v1/stores", null, HttpMethod.Get, token);
    }

    public virtual async Task<StoreData> GetStore(string storeId, CancellationToken token = default)
    {
        return await SendHttpRequest<StoreData>($"api/v1/stores/{storeId}", null, HttpMethod.Get, token);
    }

    public virtual async Task RemoveStore(string storeId, CancellationToken token = default)
    {
        await SendHttpRequest($"api/v1/stores/{storeId}", null, HttpMethod.Delete, token);
    }

    public virtual async Task<StoreData> CreateStore(CreateStoreRequest request, CancellationToken token = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        return await SendHttpRequest<StoreData>("api/v1/stores", request, HttpMethod.Post, token);
    }

    public async Task<StoreData> UpdateStore(string storeId, StoreData request, CancellationToken token = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        return await UpdateStore(storeId, Newtonsoft.Json.JsonConvert.DeserializeObject<UpdateStoreRequest>(Newtonsoft.Json.JsonConvert.SerializeObject(request)), token);
    }
    public virtual async Task<StoreData> UpdateStore(string storeId, UpdateStoreRequest request, CancellationToken token = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (storeId == null) throw new ArgumentNullException(nameof(storeId));
        return await SendHttpRequest<StoreData>($"api/v1/stores/{storeId}", request, HttpMethod.Put, token);
    }

    public virtual async Task<StoreData> UploadStoreLogo(string storeId, string filePath, string mimeType, CancellationToken token = default)
    {
        return await UploadFileRequest<StoreData>($"api/v1/stores/{storeId}/logo", filePath, mimeType, "file", HttpMethod.Post, token);
    }

    public virtual async Task DeleteStoreLogo(string storeId, CancellationToken token = default)
    {
        await SendHttpRequest($"api/v1/stores/{storeId}/logo", null, HttpMethod.Delete, token);
    }
}
