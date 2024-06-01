using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client;

public partial class BTCPayServerClient
{
    public virtual async Task<EmailSettingsData> GetStoreEmailSettings(string storeId, CancellationToken token = default)
    {
        return await SendHttpRequest<EmailSettingsData>($"api/v1/stores/{storeId}/email", null, HttpMethod.Get, token);
    }

    public virtual async Task<EmailSettingsData> UpdateStoreEmailSettings(string storeId, EmailSettingsData request, CancellationToken token = default)
    {
        return await SendHttpRequest<EmailSettingsData>($"api/v1/stores/{storeId}/email", request, method: HttpMethod.Put, token);
    }

    public virtual async Task SendEmail(string storeId, SendEmailRequest request, CancellationToken token = default)
    {
        await SendHttpRequest($"api/v1/stores/{storeId}/email/send", request, HttpMethod.Post, token);
    }
}
