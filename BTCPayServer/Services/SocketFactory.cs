using System;
using System.Linq;
using NBitcoin;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using com.LandonKey.SocksWebProxy;
using com.LandonKey.SocksWebProxy.Proxy;
using Microsoft.Extensions.Logging;
using NBitcoin.Logging;
using NBitcoin.Protocol.Connectors;
using NBitcoin.Protocol;

namespace BTCPayServer.Services
{
    public class SocketFactory
    {
        private readonly BTCPayServerOptions _options;
        public readonly Task<HttpClient> SocksClient;

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

        private Task<HttpClient> CreateHttpClientUsingSocks()
        {
            return Task.Run(() =>
            {
                try
                {
                    var proxyConfig = new ProxyConfig() {Version = ProxyConfig.SocksVersion.Five};
                    switch (_options.SocksEndpoint)
                    {
                        case null:
                            return null;
                        case IPEndPoint ipEndPoint:
                            proxyConfig.SocksPort = ipEndPoint.Port;
                            proxyConfig.SocksAddress = ipEndPoint.Address;
                            break;
                        case DnsEndPoint dnsEndPoint:

                            proxyConfig.SocksPort = dnsEndPoint.Port;
                            var ip = Dns.GetHostEntry(dnsEndPoint.Host).AddressList
                                .SingleOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork);
                            if (ip == null)
                            {
                                Logs.Utils.LogWarning( $"Could not find ip for {dnsEndPoint.Host}");
                                return null;
                            }

                            proxyConfig.SocksAddress = ip;
                            break;

                        default:
                            return null;
                    }
                    Logs.Utils.LogWarning( $"Created socks proxied http client!");
                    return new HttpClient(new HttpClientHandler
                    {
                        Proxy = new SocksWebProxy(proxyConfig), UseProxy = true
                    });
                }
                catch (Exception e)
                {
                    Logs.Utils.LogError(e, "Could not create Tor client");
                    return null;
                }
            });
        }
    }
}
