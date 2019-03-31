using System;
using NBitcoin;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Configuration;

namespace BTCPayServer.Services
{
    public class SocketFactory
    {
        private readonly BTCPayServerOptions _options;
        public SocketFactory(BTCPayServerOptions options)
        {
            _options = options;
        }
        public async Task<Socket> ConnectAsync(EndPoint endPoint, CancellationToken cancellationToken)
        {
            Socket socket = null;
            try
            {
                if (endPoint is IPEndPoint ipEndpoint)
                {
                    socket = new Socket(ipEndpoint.Address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    await socket.ConnectAsync(ipEndpoint).WithCancellation(cancellationToken);
                }
                else if (IsTor(endPoint))
                {
                    if (_options.SocksEndpoint == null)
                        throw new NotSupportedException("It is impossible to connect to an onion address without btcpay's -socksendpoint configured");
                    if (_options.SocksEndpoint.AddressFamily != AddressFamily.Unspecified)
                    {
                        socket = new Socket(_options.SocksEndpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    }
                    else
                    {
                        // If the socket is a DnsEndpoint, we allow either ipv6 or ipv4
                        socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
                        socket.DualMode = true;
                    }
                    await socket.ConnectAsync(_options.SocksEndpoint).WithCancellation(cancellationToken);
                    await NBitcoin.Socks.SocksHelper.Handshake(socket, endPoint, cancellationToken);
                }
                else if (endPoint is DnsEndPoint dnsEndPoint)
                {
                    var address = (await Dns.GetHostAddressesAsync(dnsEndPoint.Host)).FirstOrDefault();
                    socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    await socket.ConnectAsync(dnsEndPoint).WithCancellation(cancellationToken);
                }
                else
                    throw new NotSupportedException("Endpoint type not supported");
            }
            catch
            {
                CloseSocket(ref socket);
                throw;
            }
            return socket;
        }

        private bool IsTor(EndPoint endPoint)
        {
            if (endPoint is IPEndPoint)
                return endPoint.AsOnionDNSEndpoint() != null;
            if (endPoint is DnsEndPoint dns)
                return dns.Host.EndsWith(".onion", StringComparison.OrdinalIgnoreCase);
            return false;
        }

        private void CloseSocket(ref Socket s)
        {
            if (s == null)
                return;
            try
            {
                s.Shutdown(SocketShutdown.Both);
            }
            catch
            {
                try
                {
                    s.Dispose();
                }
                catch { }
            }
            finally
            {
                s = null;
            }
        }
    }
}
