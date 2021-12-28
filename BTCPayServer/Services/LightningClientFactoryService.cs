using System;
using System.Net.Http;
using BTCPayServer.Lightning;

namespace BTCPayServer.Services
{
    public class LightningClientFactoryService
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public LightningClientFactoryService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public ILightningClient Create(LightningConnectionString lightningConnectionString, BTCPayNetwork network)
        {
            ArgumentNullException.ThrowIfNull(lightningConnectionString);
            ArgumentNullException.ThrowIfNull(network);
            return new Lightning.LightningClientFactory(network.NBitcoinNetwork)
            {
                HttpClient = _httpClientFactory.CreateClient($"{network.CryptoCode}: Lightning client")
            }.Create(lightningConnectionString);
        }
    }
}
