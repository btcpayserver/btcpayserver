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
