using System;
using System.Collections.Concurrent;
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
            _lightningConnectionStringHandlers;

        private readonly ConcurrentDictionary<string, LightningClientFactory> _factories = new();

        public LightningClientFactoryService(IHttpClientFactory httpClientFactory,
            IEnumerable<Func<HttpClient, ILightningConnectionStringHandler>> lightningConnectionStringHandlers)
        {
            _httpClientFactory = httpClientFactory;
            _lightningConnectionStringHandlers = lightningConnectionStringHandlers;
        }

        private LightningClientFactory GetFactory(string namedClient, BTCPayNetwork network)
        {
            var key = $"{network.CryptoCode}:{namedClient}";
            return _factories.GetOrAdd(key, s =>
            {
                var httpClient = _httpClientFactory.CreateClient(s);
                return new LightningClientFactory(_lightningConnectionStringHandlers
                    .Select(handler => handler(httpClient))
                    .ToArray(), network.NBitcoinNetwork);
            });
        }

        public static string OnionNamedClient { get; set; } = "lightning.onion";

        public ILightningClient Create(string lightningConnectionString, BTCPayNetwork network)
        {
            ArgumentNullException.ThrowIfNull(lightningConnectionString);
            ArgumentNullException.ThrowIfNull(network);

            var httpClient = lightningConnectionString.Contains(".onion")
                ? OnionNamedClient
                : $"{network.CryptoCode}: Lightning client";

            return GetFactory(httpClient, network).Create(lightningConnectionString);
        }
    }
}
