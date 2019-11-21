using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace BTCPayServer
{
    public class HwiWebSocketTransport : Hwi.Transports.ITransport
    {
        private readonly WebSocketHelper _webSocket;

        public HwiWebSocketTransport(WebSocket webSocket)
        {
            _webSocket = new WebSocketHelper(webSocket);
        }
        public async Task<string> SendCommandAsync(string[] arguments, CancellationToken cancel)
        {
            JObject request = new JObject();
            request.Add("params", new JArray(arguments));
            await _webSocket.Send(request.ToString(), cancel);
            return await _webSocket.NextMessageAsync(cancel);
        }
    }
}
