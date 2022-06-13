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
    }
}
