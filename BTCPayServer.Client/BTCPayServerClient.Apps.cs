using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client
{
    public partial class BTCPayServerClient
    {

        public virtual async Task<PointOfSaleAppData> CreatePointOfSaleApp(string storeId,
            CreatePointOfSaleAppRequest request, CancellationToken token = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/apps/pos", bodyPayload: request,
                    method: HttpMethod.Post), token);
            return await HandleResponse<PointOfSaleAppData>(response);
        }

        public virtual async Task<CrowdfundAppData> CreateCrowdfundApp(string storeId,
            CreateCrowdfundAppRequest request, CancellationToken token = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/apps/crowdfund", bodyPayload: request,
                    method: HttpMethod.Post), token);
            return await HandleResponse<CrowdfundAppData>(response);
        }

        public virtual async Task<PointOfSaleAppData> UpdatePointOfSaleApp(string appId,
            CreatePointOfSaleAppRequest request, CancellationToken token = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/apps/pos/{appId}", bodyPayload: request,
                    method: HttpMethod.Put), token);
            return await HandleResponse<PointOfSaleAppData>(response);
        }

        public virtual async Task<AppDataBase> GetApp(string appId, CancellationToken token = default)
        {
            if (appId == null)
                throw new ArgumentNullException(nameof(appId));
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/apps/{appId}",
                    method: HttpMethod.Get), token);
            return await HandleResponse<AppDataBase>(response);
        }

        public virtual async Task<AppDataBase[]> GetAllApps(string storeId, CancellationToken token = default)
        {
            if (storeId == null)
                throw new ArgumentNullException(nameof(storeId));
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/stores/{storeId}/apps",
                    method: HttpMethod.Get), token);
            return await HandleResponse<AppDataBase[]>(response);
        }

        public virtual async Task<AppDataBase[]> GetAllApps(CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/apps",
                    method: HttpMethod.Get), token);
            return await HandleResponse<AppDataBase[]>(response);
        }

        public virtual async Task<PointOfSaleAppData> GetPosApp(string appId, CancellationToken token = default)
        {
            if (appId == null)
                throw new ArgumentNullException(nameof(appId));
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/apps/pos/{appId}",
                    method: HttpMethod.Get), token);
            return await HandleResponse<PointOfSaleAppData>(response);
        }

        public virtual async Task<CrowdfundAppData> GetCrowdfundApp(string appId, CancellationToken token = default)
        {
            if (appId == null)
                throw new ArgumentNullException(nameof(appId));
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/apps/crowdfund/{appId}",
                    method: HttpMethod.Get), token);
            return await HandleResponse<CrowdfundAppData>(response);
        }

        public virtual async Task DeleteApp(string appId, CancellationToken token = default)
        {
            if (appId == null)
                throw new ArgumentNullException(nameof(appId));
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/apps/{appId}",
                    method: HttpMethod.Delete), token);
            await HandleResponse(response);
        }
    }
}
