using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Client
{
    public partial class BTCPayServerClient
    {
        public virtual async Task<IEnumerable<NotificationData>> GetNotifications(bool? seen = null, int? skip = null,
            int? take = null, CancellationToken token = default)
        {
            Dictionary<string, object> queryPayload = new Dictionary<string, object>();

            if (seen != null)
                queryPayload.Add(nameof(seen), seen);
            if (skip != null)
                queryPayload.Add(nameof(skip), skip);
            if (take != null)
                queryPayload.Add(nameof(take), take);

            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/users/me/notifications",
                    queryPayload), token);

            return await HandleResponse<IEnumerable<NotificationData>>(response);
        }

        public virtual async Task<NotificationData> GetNotification(string notificationId,
            CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/users/me/notifications/{notificationId}"), token);
            return await HandleResponse<NotificationData>(response);
        }

        public virtual async Task<NotificationData> UpdateNotification(string notificationId, bool? seen,
            CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/users/me/notifications/{notificationId}",
                    method: HttpMethod.Put, bodyPayload: new UpdateNotification() { Seen = seen }), token);
            return await HandleResponse<NotificationData>(response);
        }

        public virtual async Task RemoveNotification(string notificationId, CancellationToken token = default)
        {
            var response = await _httpClient.SendAsync(
                CreateHttpRequest($"api/v1/users/me/notifications/{notificationId}",
                    method: HttpMethod.Delete), token);
            await HandleResponse(response);
        }
    }
}
