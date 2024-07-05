using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client;

public partial class BTCPayServerClient
{
    public virtual async Task<IEnumerable<NotificationData>> GetNotifications(bool? seen = null, int? skip = null,
        int? take = null, string[] storeId = null, CancellationToken token = default)
    {
        var queryPayload = new Dictionary<string, object>();
        if (seen != null)
            queryPayload.Add(nameof(seen), seen);
        if (skip != null)
            queryPayload.Add(nameof(skip), skip);
        if (take != null)
            queryPayload.Add(nameof(take), take);
        if (storeId != null)
            queryPayload.Add(nameof(storeId), storeId);
        return await SendHttpRequest<IEnumerable<NotificationData>>("api/v1/users/me/notifications", queryPayload, HttpMethod.Get, token);
    }

    public virtual async Task<NotificationData> GetNotification(string notificationId,
        CancellationToken token = default)
    {
        return await SendHttpRequest<NotificationData>($"api/v1/users/me/notifications/{notificationId}", null, HttpMethod.Get, token);
    }

    public virtual async Task<NotificationData> UpdateNotification(string notificationId, bool? seen,
        CancellationToken token = default)
    {
        return await SendHttpRequest<NotificationData>($"api/v1/users/me/notifications/{notificationId}", new UpdateNotification { Seen = seen }, HttpMethod.Put, token);
    }

    public virtual async Task<NotificationSettingsData> GetNotificationSettings(CancellationToken token = default)
    {
        return await SendHttpRequest<NotificationSettingsData>("api/v1/users/me/notification-settings", null, HttpMethod.Get, token);
    }

    public virtual async Task<NotificationSettingsData> UpdateNotificationSettings(UpdateNotificationSettingsRequest request, CancellationToken token = default)
    {
        return await SendHttpRequest<NotificationSettingsData>("api/v1/users/me/notification-settings", request, HttpMethod.Put, token);
    }

    public virtual async Task RemoveNotification(string notificationId, CancellationToken token = default)
    {
        await SendHttpRequest($"api/v1/users/me/notifications/{notificationId}", null, HttpMethod.Delete, token);
    }
}
