using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Configuration;
using NBitcoin.Protocol;
using NBitcoin.Protocol.Connectors;

namespace BTCPayServer.Services
{
    public class SocketFactory
    {
        private readonly BTCPayServerOptions _options;

        public SocketFactory(BTCPayServerOptions options)
        {
            _options = options;
        }

        public Task<Socket> ConnectAsync(EndPoint endPoint, CancellationToken cancellationToken)
            => ConnectAsync(endPoint, true, cancellationToken);

        public async Task<Socket> ConnectAsync(EndPoint endPoint, bool allowUnsafe, CancellationToken cancellationToken)
        {
            if (!allowUnsafe && !_options.DisableSSRFProtection)
                endPoint = await GetAllowedEndpoint(endPoint, cancellationToken);
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

        private async Task<EndPoint> GetAllowedEndpoint(EndPoint endPoint, CancellationToken cancellationToken)
        {
            var (host, port) = endPoint switch
            {
                IPEndPoint ip => (ip.Address.ToString(), ip.Port),
                DnsEndPoint dns => (dns.Host, dns.Port),
                _ => throw new NotSupportedException($"Unsupported endpoint {endPoint}")
            };
            // Onion endpoints never connect directly, they go through the socks proxy
            if (NetworkExtensions.IsOnionHost(host))
                return endPoint;
            var addresses = await SSRFProtectionExtensions.GetAllowedAddresses(host, port, _options.SSRFExceptions, null, cancellationToken);
            if (addresses.Count is 0)
                throw new SocketException((int)SocketError.AccessDenied, "The endpoint does not resolve to an allowed network address");
            // Connect to the address which got checked, so the name can't resolve to something else later
            return new IPEndPoint(addresses[0], port);
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
