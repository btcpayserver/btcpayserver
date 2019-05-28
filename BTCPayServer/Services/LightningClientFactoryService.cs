using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
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
            if (lightningConnectionString == null)
                throw new ArgumentNullException(nameof(lightningConnectionString));
            if (network == null)
                throw new ArgumentNullException(nameof(network));
            return new Lightning.LightningClientFactory(network.NBitcoinNetwork)
            {
                HttpClient = _httpClientFactory.CreateClient($"{network.CryptoCode}: Lightning client")
            }.Create(lightningConnectionString);
        }
    }
}
