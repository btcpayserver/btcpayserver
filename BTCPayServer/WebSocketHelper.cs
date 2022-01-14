using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NBXplorer;

namespace BTCPayServer
{
    public class WebSocketHelper
    {
        private readonly WebSocket _Socket;
        public WebSocket Socket
        {
            get
            {
                return _Socket;
            }
        }

        public WebSocketHelper(WebSocket socket)
        {
            _Socket = socket;
            var buffer = new byte[ORIGINAL_BUFFER_SIZE];
            _Buffer = new ArraySegment<byte>(buffer, 0, buffer.Length);
        }

        const int ORIGINAL_BUFFER_SIZE = 1024 * 5;
        const int MAX_BUFFER_SIZE = 1024 * 1024 * 5;
        readonly ArraySegment<byte> _Buffer;
        readonly UTF8Encoding UTF8 = new UTF8Encoding(false, true);
        public async Task<string> NextMessageAsync(CancellationToken cancellation)
        {
            var buffer = _Buffer;
            var array = _Buffer.Array;
            var originalSize = _Buffer.Array.Length;
            var newSize = _Buffer.Array.Length;
            while (true)
            {
                var message = await Socket.ReceiveAndPingAsync(buffer, cancellation);
                if (message.MessageType == WebSocketMessageType.Close)
                {
                    await CloseSocketAndThrow(WebSocketCloseStatus.NormalClosure, "Close message received from the peer", cancellation);
                    break;
                }
                if (message.MessageType != WebSocketMessageType.Text)
                {
                    await CloseSocketAndThrow(WebSocketCloseStatus.InvalidMessageType, "Only Text is supported", cancellation);
                    break;
                }
                if (message.EndOfMessage)
                {
                    buffer = new ArraySegment<byte>(array, 0, buffer.Offset + message.Count);
                    try
                    {
                        var o = UTF8.GetString(buffer.Array, 0, buffer.Count);
                        if (newSize != originalSize)
                        {
                            Array.Resize(ref array, originalSize);
                        }
                        return o;
                    }
                    catch (Exception ex)
                    {
                        await CloseSocketAndThrow(WebSocketCloseStatus.InvalidPayloadData, $"Invalid payload: {ex.Message}", cancellation);
                    }
                }
                else
                {
                    if (buffer.Count - message.Count <= 0)
                    {
                        newSize *= 2;
                        if (newSize > MAX_BUFFER_SIZE)
                            await CloseSocketAndThrow(WebSocketCloseStatus.MessageTooBig, "Message is too big", cancellation);
                        Array.Resize(ref array, newSize);
                        buffer = new ArraySegment<byte>(array, buffer.Offset, newSize - buffer.Offset);
                    }

                    buffer = buffer.Slice(message.Count, buffer.Count - message.Count);
                }
            }
            throw new InvalidOperationException("Should never happen");
        }

        private async Task CloseSocketAndThrow(WebSocketCloseStatus status, string description, CancellationToken cancellation)
        {
            var array = _Buffer.Array;
            if (array.Length != ORIGINAL_BUFFER_SIZE)
                Array.Resize(ref array, ORIGINAL_BUFFER_SIZE);
            await Socket.CloseSocket(status, description, cancellation);
            throw new WebSocketException($"The socket has been closed ({status}: {description})");
        }

        public async Task Send(string evt, CancellationToken cancellation = default)
        {
            var bytes = UTF8.GetBytes(evt);
            using var cts = new CancellationTokenSource(5000);
            using var cts2 = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            await Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts2.Token);
        }

        public async Task DisposeAsync(CancellationToken cancellation)
        {
            try
            {
                await Socket.CloseSocket(WebSocketCloseStatus.NormalClosure, "Disposing NotificationServer", cancellation);
            }
            catch { }
            finally { try { Socket.Dispose(); } catch { } }
        }
    }
}
