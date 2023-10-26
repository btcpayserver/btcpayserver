using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BTCPayServer.Common.Altcoins.Chia.RPC
{
    /// <summary>
    /// Class that handles core communication with the rpc endpoint using a websocket (wss).
    /// Only the daemon endpoint supports websockets, but it can proxy communication to other services.
    /// <see cref="Message.Destination"/>
    /// </summary>
    public class WebSocketRpcClient : IDisposable
    {
        private readonly ClientWebSocket _webSocket = new();
        private readonly CancellationTokenSource _receiveCancellationTokenSource = new();
        private readonly ConcurrentDictionary<string, Message> _pendingRequests = new();
        private readonly ConcurrentDictionary<string, Message> _pendingResponses = new();

        private bool _disposedValue;

        /// <summary>
        /// ctor
        /// </summary>
        /// <param name="endpoint">Details of the websocket endpoint</param>        
        public WebSocketRpcClient(EndpointInfo endpoint)
        {
            Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            
            _webSocket.Options.ClientCertificates.Add(CertLoader.GetCerts(Endpoint.CertPath, Endpoint.KeyPath)[0]);
            _webSocket.Options.RemoteCertificateValidationCallback += ValidateServerCertificate;
        }

        /// <summary>
        /// Details of the RPC service endpoint
        /// </summary>
        private EndpointInfo Endpoint { get; init; }

        /// <summary>
        /// Opens the websocket and starts the receive loop
        /// </summary>
        /// <param name="cancellationToken">A token to allow the call to be cancelled</param>
        /// <returns>An awaitable <see cref="Task"/></returns>
        public async Task Connect(CancellationToken cancellationToken = default)
        {
            if (_disposedValue)
            {
                throw new ObjectDisposedException(nameof(WebSocketRpcClient));
            }

            if (_webSocket.State is WebSocketState.Connecting or WebSocketState.Open)
            {
                throw new InvalidOperationException("RpcClient connection is already open");
            }

            Console.WriteLine(Endpoint);

            await _webSocket.ConnectAsync(Endpoint.Uri, cancellationToken).ConfigureAwait(false);
            Console.WriteLine("Connected");

            _ = Task.Factory.StartNew(ReceiveLoop, _receiveCancellationTokenSource.Token,
                TaskCreationOptions.LongRunning, TaskScheduler.Default).ConfigureAwait(false);
            OnConnected();
        }

        /// <summary>
        /// Called after <see cref="Connect(CancellationToken)"/> completes successfully. Lets derived classess know that they can do
        /// post connection initialization 
        /// </summary>
        protected virtual void OnConnected()
        {
            Console.WriteLine("OnConnected");
        }

        /// <summary>
        /// Cancels the receive loop and closes the websocket
        /// </summary>
        /// <param name="cancellationToken">A token to allow the call to be cancelled</param>
        /// <returns>Awaitable <see cref="Task"/></returns>
        public async Task Close(CancellationToken cancellationToken = default)
        {
            if (_disposedValue)
            {
                throw new ObjectDisposedException(nameof(WebSocketRpcClient));
            }

            _receiveCancellationTokenSource.Cancel();
            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Posts a <see cref="Message"/> to the websocket but does not wait for a response
        /// </summary>
        /// <param name="message">The message to post</param>
        /// <param name="cancellationToken">A token to allow the call to be cancelled</param>
        /// <remarks>Awaiting this method waits for the message to be sent only. It doesn't await a response.</remarks>
        /// <returns>Awaitable <see cref="Task"/></returns>
        public virtual async Task PostMessage(Message message, CancellationToken cancellationToken = default)
        {
            if (_disposedValue)
            {
                throw new ObjectDisposedException(nameof(WebSocketRpcClient));
            }

            var json = JsonConvert.SerializeObject(message);
            await _webSocket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Sends a <see cref="Message"/> to the endpoint and waits for a response
        /// </summary>
        /// <param name="message">The message to send</param>
        /// <param name="cancellationToken">A token to allow the call to be cancelled</param>
        /// <remarks>Awaiting this method will block until a response is received from the <see cref="WebSocket"/> or the A token to allow the call to be cancelled is cancelled</remarks>
        /// <returns>The response message</returns>
        /// <exception cref="ResponseException">Throws when <see cref="Message.IsSuccessfulResponse"/> is False</exception>
        public async Task<dynamic> SendMessage(Message message, CancellationToken cancellationToken = default)
        {
            if (_disposedValue)
            {
                throw new ObjectDisposedException(nameof(WebSocketRpcClient));
            }

            // capture the message to be sent
            if (!_pendingRequests.TryAdd(message.RequestId, message))
            {
                throw new InvalidOperationException(
                    $"A message with an id of {message.RequestId} has already been sent");
            }

            try
            {
                await PostMessage(message, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
                _ = _pendingRequests.TryRemove(message.RequestId, out _);
                throw;
            }

            // wait here until a response shows up or we get cancelled
            Message response;
            while (!_pendingResponses.TryRemove(message.RequestId, out response!) &&
                   !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }

            // the receive loop cleans up but make sure we do so on cancellation too
            if (_pendingRequests.ContainsKey(message.RequestId))
            {
                _ = _pendingRequests.TryRemove(message.RequestId, out _);
            }

            return response is null
                ? throw new ResponseException(message, "The websocket did not respond")
                : !response.IsSuccessfulResponse
                    ? throw new ResponseException(message, response.Data?.error?.ToString())
                    : response.Data ?? new ExpandoObject();
        }

        public async Task<TResponse> SendCommandAsync<TRequest, TResponse>(string method, TRequest data,
            CancellationToken cts = default(CancellationToken))
        {
            var jsonSerializer = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            };
            var message = new Message()
            {
                Command = method,
                Data = new StringContent(
                    JsonConvert.SerializeObject(data, jsonSerializer),
                    Encoding.UTF8, "application/json")
            };
            Console.WriteLine(message);

            var rawResult = await SendMessage(message, cts);
            var rawJson = await rawResult.Content.ReadAsStringAsync();
            Console.WriteLine(rawJson);

            rawResult.EnsureSuccessStatusCode();
            var response = JsonConvert.DeserializeObject<TResponse>(rawJson, jsonSerializer);
            return response;
        }

        /// <summary>
        /// Event raised when a message is received from the endpoint that was either not in response to a request
        /// or was a response from a posted message (i.e. we didn't register to receive the response)
        /// Pooling state_changed messages come through this event
        /// </summary>
        public event EventHandler<Message>? BroadcastMessageReceived;

        /// <summary>
        /// Raises the <see cref="BroadcastMessageReceived"/> event
        /// </summary>
        /// <param name="message">The message to broadcast</param>
        protected virtual void OnBroadcastMessageReceived(Message message)
        {
            if (message is null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            // Debug.WriteLine("Broadcast message:");
            // Debug.WriteLine(message.ToJson());

            BroadcastMessageReceived?.Invoke(this, message);
        }

        private async Task ReceiveLoop()
        {
            Console.WriteLine("ReceiveLoop");
            var buffer = new ArraySegment<byte>(new byte[2048]);
            do
            {
                Console.WriteLine("Loop");
                using var ms = new MemoryStream();

                WebSocketReceiveResult result;
                do
                {
                    result = await _webSocket.ReceiveAsync(buffer, _receiveCancellationTokenSource.Token)
                        .ConfigureAwait(false);
#pragma warning disable CS8604 // Possible null reference argument.
                    ms.Write(buffer.Array, buffer.Offset, result.Count);
#pragma warning restore CS8604 // Possible null reference argument.
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                _ = ms.Seek(0, SeekOrigin.Begin);
                using var reader = new StreamReader(ms, Encoding.UTF8);
                var response = await reader.ReadToEndAsync().ConfigureAwait(false);
                var message = JsonConvert.DeserializeObject<Message>(response) ?? new Message();

                // if we have a message pending with this id, capture the response and remove the request from the pending dictionary                
                if (_pendingRequests.TryRemove(message.RequestId, out _))
                {
                    _pendingResponses[message.RequestId] = message;
                }
                else //if (message.RequestId != string.Empty) // only broadcast if it's an actual response
                {
                    OnBroadcastMessageReceived(message);
                }
            } while (!_receiveCancellationTokenSource.IsCancellationRequested);
            Console.WriteLine("End");
        }

        private static bool ValidateServerCertificate(object sender, X509Certificate? certificate, X509Chain? chain,
            SslPolicyErrors sslPolicyErrors)
        {
            return (sslPolicyErrors & SslPolicyErrors.RemoteCertificateNotAvailable) !=
                   SslPolicyErrors.RemoteCertificateNotAvailable;
        }

        /// <summary>
        /// Called when the instance is being disposed or finalized
        /// </summary>
        /// <param name="disposing">Invoke from <see cref="IDisposable.Dispose"/></param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _receiveCancellationTokenSource.Cancel();
                    _pendingRequests.Clear();
                    _pendingResponses.Clear();
                    _webSocket.Dispose();
                    _receiveCancellationTokenSource.Dispose();
                }

                _disposedValue = true;
            }
        }

        /// <summary>
        /// <see cref="IDisposable.Dispose"/>
        /// </summary>
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
