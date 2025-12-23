using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer
{
    public static class WebsocketExtensions
    {
        /// <summary>
        /// NGINX closes websocket connections after 1 min if there is no activity, so here we do some busy work every 30s
        /// </summary>
        /// <returns></returns>
        public static async Task<WebSocketReceiveResult> ReceiveAndPingAsync(this WebSocket webSocket, ArraySegment<byte> buffer, CancellationToken cancellationToken = default)
        {
            var waiting = Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            var receiving = webSocket.ReceiveAsync(buffer, cancellationToken);
wait:
            var completed = await Task.WhenAny(waiting, receiving);
            if (completed == waiting)
            {
                await webSocket.SendAsync(Encoding.UTF8.GetBytes("ping"), WebSocketMessageType.Text, true, cancellationToken);
                waiting = Task.Delay(TimeSpan.FromSeconds(25), cancellationToken);
                goto wait;
            }
            return await receiving;
        }
    }
}
