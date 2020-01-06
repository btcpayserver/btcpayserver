using NBitcoin;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using com.LandonKey.SocksWebProxy;
using com.LandonKey.SocksWebProxy.Proxy;
using NBitcoin.Protocol.Connectors;
using NBitcoin.Protocol;

namespace BTCPayServer.Services
{
    public class SocketFactory
    {
        private readonly BTCPayServerOptions _options;
        public readonly HttpClient SocksClient;
        public SocketFactory(BTCPayServerOptions options)
        {
            _options = options;
            SocksClient = CreateHttpClientUsingSocks();
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

        internal static void SafeCloseSocket(System.Net.Sockets.Socket socket)
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

        private HttpClient CreateHttpClientUsingSocks()
        {
            if (_options.SocksEndpoint == null)
                return null;
            return new HttpClient(new HttpClientHandler
            {
                Proxy = new SocksWebProxy(new ProxyConfig()
                {
                    Version = ProxyConfig.SocksVersion.Five,
                    SocksAddress = _options.SocksEndpoint.AsOnionCatIPEndpoint().Address,
                    SocksPort = _options.SocksEndpoint.AsOnionCatIPEndpoint().Port,
                }),
                UseProxy = true
            });
        }
    }
}
