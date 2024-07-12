using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client;

public partial class BTCPayServerClient
{
    public virtual async Task<GenericPaymentMethodData> UpdateStorePaymentMethod(string storeId, string paymentMethodId, UpdatePaymentMethodRequest request, CancellationToken token = default)
    {
        return await SendHttpRequest<GenericPaymentMethodData>($"api/v1/stores/{storeId}/payment-methods/{paymentMethodId}", request, HttpMethod.Put, token);
    }

    public virtual async Task RemoveStorePaymentMethod(string storeId, string paymentMethodId)
    {
        await SendHttpRequest($"api/v1/stores/{storeId}/payment-methods/{paymentMethodId}", null, HttpMethod.Delete, CancellationToken.None);
    }

    public virtual async Task<GenericPaymentMethodData> GetStorePaymentMethod(string storeId, string paymentMethodId, bool? includeConfig = null, CancellationToken token = default)
    {
        var query = new Dictionary<string, object>();
        if (includeConfig != null)
        {
            query.Add(nameof(includeConfig), includeConfig);
        }
        return await SendHttpRequest<GenericPaymentMethodData>($"api/v1/stores/{storeId}/payment-methods/{paymentMethodId}", query, HttpMethod.Get, token);
    }

    public virtual async Task<GenericPaymentMethodData[]> GetStorePaymentMethods(string storeId, bool? onlyEnabled = null, bool? includeConfig = null, CancellationToken token = default)
    {
        var query = new Dictionary<string, object>();
        if (onlyEnabled != null)
        {
            query.Add(nameof(onlyEnabled), onlyEnabled);
        }
        if (includeConfig != null)
        {
            query.Add(nameof(includeConfig), includeConfig);
        }

        return await SendHttpRequest<GenericPaymentMethodData[]>($"api/v1/stores/{storeId}/payment-methods", query, HttpMethod.Get, token);
    }
}
