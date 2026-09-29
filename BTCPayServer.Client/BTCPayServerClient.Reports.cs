using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client;

public partial class BTCPayServerClient
{
    public virtual async Task<IEnumerable<string>> GetStoreReports(string storeId,
        CancellationToken token = default)
    {
        return await SendHttpRequest<IEnumerable<string>>($"api/v1/stores/{storeId}/reports", null,
            HttpMethod.Get, token);
    }

    public virtual async Task<StoreReportResponse> RunStoreReport(string storeId, StoreReportRequest request,
        CancellationToken token = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        return await SendHttpRequest<StoreReportResponse>($"api/v1/stores/{storeId}/reports", request,
            HttpMethod.Post, token);
    }
}
