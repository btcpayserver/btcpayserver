using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning;

namespace BTCPayServer.Services
{
    public class LightningClientFactoryService
    {
        private readonly IHttpClientFactory _httpClientFactory;

        private readonly IEnumerable<Func<HttpClient, ILightningConnectionStringHandler>>
            _lightningConnectionStringHandlersFactories;

        private readonly IEnumerable<ILightningConnectionStringHandler> _lightningConnectionStringHandlers;

        public LightningClientFactoryService(IHttpClientFactory httpClientFactory,
            IEnumerable<Func<HttpClient, ILightningConnectionStringHandler>> lightningConnectionStringHandlersFactories, IEnumerable<ILightningConnectionStringHandler> lightningConnectionStringHandlers)
        {
            _httpClientFactory = httpClientFactory;
            _lightningConnectionStringHandlersFactories = lightningConnectionStringHandlersFactories;
            _lightningConnectionStringHandlers = lightningConnectionStringHandlers;
        }

        private LightningClientFactory GetFactory(string namedClient, BTCPayNetwork network)
        {
            var httpClient = _httpClientFactory.CreateClient(namedClient);
            
            return new LightningClientFactory(_lightningConnectionStringHandlersFactories
                .Select(handler => handler(httpClient)).Concat(_lightningConnectionStringHandlers)
                .ToArray(), network.NBitcoinNetwork);
        }

        public static string OnionNamedClient { get; set; } = "lightning.onion";
        public static string NamedClient { get; set; } = "lightning";
        public static string SafeNamedClient { get; set; } = "lightning.safe";

        public static async ValueTask<Stream> ConnectPublicEndpoint(SocketsHttpConnectionContext context,
            CancellationToken cancellationToken)
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host,
                AddressFamily.Unspecified, cancellationToken);
            if (addresses.Length is 0 || addresses.Any(a => BTCPayServer.Extensions.IsLocalNetwork(a.ToString())))
                throw new HttpRequestException("The Lightning endpoint does not resolve exclusively to public addresses");

            Exception lastException = null;
            foreach (var address in addresses)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    socket.Dispose();
                    lastException = ex;
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }

            throw new HttpRequestException("Could not connect to the Lightning endpoint", lastException);
        }

        public ILightningClient Create(string lightningConnectionString, BTCPayNetwork network)
            => Create(lightningConnectionString, network, true);

        public ILightningClient Create(string lightningConnectionString, BTCPayNetwork network, bool allowUnsafe)
        {
            ArgumentNullException.ThrowIfNull(lightningConnectionString);
            ArgumentNullException.ThrowIfNull(network);

            var isOnion = BTCPayServer.Extensions.TryGetLightningServer(lightningConnectionString, out var server) &&
                          server.DnsSafeHost.TrimEnd('.').EndsWith(".onion", StringComparison.OrdinalIgnoreCase);
            var httpClient = (isOnion, allowUnsafe) switch
            {
                (true, _) => OnionNamedClient,
                (false, false) => SafeNamedClient,
                _ => NamedClient
            };

            return GetFactory(httpClient, network).Create(lightningConnectionString);
        }
    }
}
