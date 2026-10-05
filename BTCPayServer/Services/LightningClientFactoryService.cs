using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
