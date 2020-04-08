using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Services.Proxy;
using NBitcoin;
using NBitcoin.Protocol.Connectors;
using NBitcoin.Protocol;

namespace BTCPayServer.Services
{
    public class Socks5HttpClientFactory : IHttpClientFactory
    {
        private readonly BTCPayServerOptions _options;

        public Socks5HttpClientFactory(BTCPayServerOptions options)
        {
            _options = options;
        }
        private ConcurrentDictionary<string, HttpClient> cachedClients = new ConcurrentDictionary<string, HttpClient>();
        public HttpClient CreateClient(string name)
        {
            return cachedClients.GetOrAdd(name, s =>
            {
                if (_options.SocksEndpoint == null)
                {
                    return null;
                }

                var proxy = new ProxyClient(_options.SocksEndpoint.ToEndpointString(), ProxyClient.ProxyType.Socks5);
                return new HttpClient(
                    new HttpClientHandler {Proxy = proxy, },
                    true);
            });
        }
    }

    public class SocketFactory
    {
        private readonly BTCPayServerOptions _options;

        public SocketFactory(BTCPayServerOptions options)
        {
            _options = options;
        }

        public async Task<Socket> ConnectAsync(EndPoint endPoint, CancellationToken cancellationToken)
        {
            DefaultEndpointConnector connector = new DefaultEndpointConnector();
            NodeConnectionParameters connectionParameters = new NodeConnectionParameters();
            if (_options.SocksEndpoint != null)
            {
                connectionParameters.TemplateBehaviors.Add(new NBitcoin.Protocol.Behaviors.SocksSettingsBehavior()
                {
                    SocksEndpoint = _options.SocksEndpoint
                });
            }

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await connector.ConnectSocket(socket, endPoint, connectionParameters, cancellationToken);
            }
            catch
            {
                SafeCloseSocket(socket);
            }

            return socket;
        }

        internal static void SafeCloseSocket(Socket socket)
        {
            try
            {
                socket.Shutdown(SocketShutdown.Both);
            }
            catch
            {
            }

            try
            {
                socket.Dispose();
            }
            catch
            {
            }
        }
    }
}
