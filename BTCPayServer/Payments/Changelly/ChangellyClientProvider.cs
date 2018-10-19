using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
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


        public virtual bool TryGetChangellyClient(string storeId, out string error,
            out Changelly changelly)
        {
            if (_clientCache.ContainsKey(storeId))
            {
                changelly = _clientCache[storeId];
                error = null;
                return true;
            }

            changelly = null;


            var store = _storeRepository.FindStore(storeId).Result;
            if (store == null)
            {
                error = "Store not found";
                return false;
            }

            var blob = store.GetStoreBlob();
            var changellySettings = blob.ChangellySettings;


            if (changellySettings == null || !changellySettings.IsConfigured())
            {
                error = "Changelly not configured for this store";
                return false;
            }

            if (!changellySettings.Enabled)
            {
                error = "Changelly not enabled for this store";
                return false;
            }

            changelly = new Changelly(_httpClientFactory, changellySettings.ApiKey, changellySettings.ApiSecret,
                changellySettings.ApiUrl, changellySettings.ShowFiat);
            _clientCache.AddOrReplace(storeId, changelly);
            error = null;
            return true;
        }
    }
}
