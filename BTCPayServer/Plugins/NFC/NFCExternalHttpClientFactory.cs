using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.Plugins.NFC
{
    public sealed class NFCExternalHttpClientFactory(IHttpClientFactory httpClientFactory)
    {
        internal const string ClearnetNamedClient = nameof(NFCExternalHttpClientFactory) + "-Clearnet";
        internal const string OnionNamedClient = nameof(NFCExternalHttpClientFactory) + "-Onion";

        private static readonly IPNetwork[] NonPublicIPv4Networks =
        [
            IPNetwork.Parse("0.0.0.0/8"),
            IPNetwork.Parse("10.0.0.0/8"),
            IPNetwork.Parse("100.64.0.0/10"),
            IPNetwork.Parse("127.0.0.0/8"),
            IPNetwork.Parse("169.254.0.0/16"),
            IPNetwork.Parse("172.16.0.0/12"),
            IPNetwork.Parse("192.0.0.0/24"),
            IPNetwork.Parse("192.0.2.0/24"),
            IPNetwork.Parse("192.88.99.0/24"),
            IPNetwork.Parse("192.168.0.0/16"),
            IPNetwork.Parse("198.18.0.0/15"),
            IPNetwork.Parse("198.51.100.0/24"),
            IPNetwork.Parse("203.0.113.0/24"),
            IPNetwork.Parse("224.0.0.0/4"),
            IPNetwork.Parse("240.0.0.0/4")
        ];

        private static readonly IPNetwork GlobalIPv6Network = IPNetwork.Parse("2000::/3");
        private static readonly IPNetwork[] NonPublicIPv6Networks =
        [
            IPNetwork.Parse("2001::/23"),
            IPNetwork.Parse("2001:db8::/32"),
            IPNetwork.Parse("2002::/16"),
            IPNetwork.Parse("3fff::/20")
        ];

        public HttpClient CreateClient(Uri uri)
        {
            if (!TryValidateUri(uri, out var error))
            {
                throw new ArgumentException(error, nameof(uri));
            }

            return httpClientFactory.CreateClient(uri.IsOnion() ? OnionNamedClient : ClearnetNamedClient);
        }

        internal static bool TryValidateUri(Uri uri, out string error)
        {
            if (uri is null || !uri.IsAbsoluteUri)
            {
                error = "LNURL must be an absolute URI";
                return false;
            }

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                error = "LNURL scheme must be http or https";
                return false;
            }

            if (uri.IsOnion())
            {
                error = null;
                return true;
            }

            if (IPAddress.TryParse(uri.DnsSafeHost, out var address) && !IsSafeAddress(address))
            {
                error = "LNURL must not point to a local or non-routable network host";
                return false;
            }

            error = null;
            return true;
        }

        internal static SocketsHttpHandler CreateClearnetHandler()
        {
            return new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
                ConnectCallback = ConnectAsync
            };
        }

        internal static bool IsSafeAddress(IPAddress address)
        {
            ArgumentNullException.ThrowIfNull(address);

            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            return address.AddressFamily switch
            {
                AddressFamily.InterNetwork => NonPublicIPv4Networks.All(network => !network.Contains(address)),
                AddressFamily.InterNetworkV6 => GlobalIPv6Network.Contains(address) &&
                                                 NonPublicIPv6Networks.All(network => !network.Contains(address)),
                _ => false
            };
        }

        internal static void EnsureSafeAddresses(string host, IPAddress[] addresses)
        {
            if (addresses is null || addresses.Length == 0)
            {
                throw new HttpRequestException($"Could not resolve LNURL host '{host}'");
            }

            if (addresses.Any(address => !IsSafeAddress(address)))
            {
                throw new HttpRequestException("LNURL host resolved to a local or non-routable network address");
            }
        }

        private static async ValueTask<Stream> ConnectAsync(
            SocketsHttpConnectionContext context,
            CancellationToken cancellationToken)
        {
            var host = context.DnsEndPoint.Host;
            var addresses = IPAddress.TryParse(host, out var address)
                ? [address]
                : await Dns.GetHostAddressesAsync(host, cancellationToken);
            EnsureSafeAddresses(host, addresses);

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };
            try
            {
                await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    }
}
