using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services.Stores;
using NBitcoin;

namespace BTCPayServer.Payments.Changelly
{
    public class ChangellyClientProvider
    {
        private readonly StoreRepository _storeRepository;
        private readonly IHttpClientFactory _httpClientFactory;

        private readonly ConcurrentDictionary<string, Changelly> _clientCache =
            new ConcurrentDictionary<string, Changelly>();

        public ChangellyClientProvider(StoreRepository storeRepository, IHttpClientFactory httpClientFactory)
        {
            _storeRepository = storeRepository;
            _httpClientFactory = httpClientFactory;
        }

        public void InvalidateClient(string storeId)
        {
            if (_clientCache.ContainsKey(storeId))
            {
                _clientCache.Remove(storeId, out var value);
            }
        }


        public virtual async Task<Changelly> TryGetChangellyClient(string storeId, StoreData storeData = null)
        {
            if (_clientCache.ContainsKey(storeId))
            {
                return _clientCache[storeId];
            }

            if (storeData == null)
            {
                storeData = await _storeRepository.FindStore(storeId);
                if (storeData == null)
                {
                    throw new ChangellyException("Store not found");
                }
            }

            var blob = storeData.GetStoreBlob();
            var changellySettings = blob.ChangellySettings;


            if (changellySettings == null || !changellySettings.IsConfigured())
            {
                throw new ChangellyException("Changelly not configured for this store");
            }

            if (!changellySettings.Enabled)
            {
                throw new ChangellyException("Changelly not enabled for this store");
            }

            var changelly = new Changelly(_httpClientFactory, changellySettings.ApiKey, changellySettings.ApiSecret,
                changellySettings.ApiUrl, changellySettings.ShowFiat);
            _clientCache.AddOrReplace(storeId, changelly);
            return changelly;
        }
    }
}
